using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Api.Attribute;
using Snet.Yolo.Api.Handler;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.Extensions;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Api.Controllers
{
    /// <summary>
    /// 操作
    /// </summary>
    public class OperateBaseController : ControllerBase
    {
        /// <summary>
        /// 管理操作
        /// </summary>
        public ManageOperate _operate;
        /// <summary>
        /// 配置
        /// </summary>
        public ConfigModel _config;
        /// <summary>
        /// 姿态处理
        /// </summary>
        public PoseEstimationCustomKeyPointColorHandler _poseHandler;
        /// <summary>
        /// 标识符
        /// </summary>
        public virtual string Tag { get; set; } = string.Empty;
        /// <summary>
        /// 操作控制器<br/>
        /// 有参构造函数
        /// </summary>
        /// <param name="operate">管理操作</param>
        /// <param name="config">配置</param>
        /// <param name="poseHandler">姿态关键点颜色处理器。</param>
        public OperateBaseController(ManageOperate operate, IOptions<ConfigModel> config, PoseEstimationCustomKeyPointColorHandler poseHandler)
        {
            _operate = operate;
            _config = config.Value;
            _poseHandler = poseHandler;
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">模型类型</param>
        /// <returns>结果</returns>
        [HttpPost]
        public async Task<OperateResult> AddAsync([AllowedFileType([".onnx"])] IFormFile file, string describe, OnnxType onnxType)
        {
            if (file.Length <= 0 || file.Length > _config.MaxModelBytes)
            {
                return OperateResult.CreateFailureResult($"Model file size must be between 1 byte and {_config.MaxModelBytes} bytes.");
            }
            var savePath = Path.Combine(PublicHandler.DefaultPath, "onnxs");
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            // Sanitize filename and ensure uniqueness to prevent overwrites
            var safeName = Path.GetFileNameWithoutExtension(file.FileName).Replace("..", "").Replace("/", "").Replace("\\", "");
            var extension = Path.GetExtension(file.FileName);
            var filePath = Path.Combine(savePath, $"{safeName}_{Guid.NewGuid():N}{extension}");
            try
            {
                await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await file.CopyToAsync(stream, HttpContext.RequestAborted);
                }
                OperateResult result = await _operate.AddAsync(filePath, describe, onnxType);
                if (!result.Status)
                {
                    System.IO.File.Delete(filePath);
                }
                return result;
            }
            catch
            {
                if (System.IO.File.Exists(filePath)) { System.IO.File.Delete(filePath); }
                throw;
            }
        }

        /// <summary>
        /// 修改
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">类型</param>
        /// <returns>结果</returns>
        [HttpPost]
        public async Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null) => await _operate.UpdateAsync(index, describe, onnxType);

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="deleteFile">是否删除文件</param>
        /// <returns>结果</returns>
        [HttpPost]
        public async Task<OperateResult> DeleteAsync(int index, bool deleteFile = true) => await _operate.DeleteAsync(index, deleteFile);

        /// <summary>
        /// 指定查询
        /// </summary>
        /// <param name="index">下标</param>
        /// <returns>结果</returns>
        [HttpGet]
        public async Task<OperateResult> QueryAsync(int index) => await _operate.QueryAsync(index);

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <returns>结果</returns>
        [HttpGet]
        public async Task<OperateResult> QueryAllAsync() => await _operate.QueryAsync();

        /// <summary>
        /// 获取本地原始的图片
        /// </summary>
        /// <param name="name">
        /// 图片名称（文件名“时间区域”的一部分，不包含扩展名）
        /// </param>
        /// <param name="type">
        /// 模型类型（用于定位子目录）
        /// </param>
        /// <param name="date">可选历史日期（yyyy-MM-dd）；省略时自动查找最近匹配记录。</param>
        /// <returns>
        /// 成功时返回图片文件，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public IActionResult GetOriginalImage(string name, OnnxType type, string? date = null)
        {
            if (!IsSafeHistoryName(name)) { return BadRequest("Parameter 'name' is invalid."); }
            var path = FindHistoryFile(name, type, date, _config.OriginalImageNamingFormat, out _);
            return path is null ? NotFound("Target file not found.") : PhysicalFile(path, "image/jpeg");
        }

        /// <summary>
        /// 获取本地标注后的图片
        /// </summary>
        /// <param name="name">
        /// 图片名称（文件名“时间区域”的一部分，不包含扩展名）
        /// </param>
        /// <param name="type">
        /// 模型类型（用于定位子目录）
        /// </param>
        /// <param name="date">可选历史日期（yyyy-MM-dd）；省略时自动查找最近匹配记录。</param>
        /// <returns>
        /// 成功时返回图片文件，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public IActionResult GetMarkImage(string name, OnnxType type, string? date = null)
        {
            if (!IsSafeHistoryName(name)) { return BadRequest("Parameter 'name' is invalid."); }
            var path = FindHistoryFile(name, type, date, _config.ResultImageNamingFormat, out _);
            return path is null ? NotFound("Target file not found.") : PhysicalFile(path, "image/jpeg");
        }

        /// <summary>
        /// 获取本地图片的详情，有原图地址，标注后的图片地址，还有坐标
        /// </summary>
        /// <param name="name">
        /// 图片名称（文件名“时间区域”的一部分，不包含扩展名）
        /// </param>
        /// <param name="type">
        /// 模型类型（用于定位子目录）
        /// </param>
        /// <param name="date">可选历史日期（yyyy-MM-dd）；省略时自动查找最近匹配记录。</param>
        /// <returns>
        /// 成功时返回有原图地址，标注后的图片地址，还有坐标，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public async Task<OperateResult> GetImageDetails(string name, OnnxType type, string? date = null)
        {
            string ms = DateTime.Now.ToString(_config.NameFormat);
            TimeHandler.Instance(ms).StartRecord();
            if (!IsSafeHistoryName(name))
                return OperateResult.CreateFailureResult("Parameter 'name' is invalid.", TimeHandler.Instance(ms).StopRecord().milliseconds);

            if (!TryFindHistorySet(name, type, date, out var originalPath, out var resultPath, out var detailsPath, out var resolvedDate))
                return OperateResult.CreateFailureResult("Target file not found.", TimeHandler.Instance(ms).StopRecord().milliseconds);

            object? DetailsNamingFormatObject = (await System.IO.File.ReadAllTextAsync(detailsPath, HttpContext.RequestAborted)).ToJsonEntity<object>();
            if (DetailsNamingFormatObject is null) { return OperateResult.CreateFailureResult("Invalid details file.", TimeHandler.Instance(ms).StopRecord().milliseconds); }

            string OriginalImageNamingFormatUrl = Url.Action("GetOriginalImage", "Operate", new { name, type, date = resolvedDate }, Request.Scheme) ?? string.Empty;
            string ResultImageNamingFormatUrl = Url.Action("GetMarkImage", "Operate", new { name, type, date = resolvedDate }, Request.Scheme) ?? string.Empty;

            return OperateResult.CreateSuccessResult("GetImageDetails Success", new IdentityResultData<object>(DetailsNamingFormatObject, ResultImageNamingFormatUrl, OriginalImageNamingFormatUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);

        }

        private static bool IsSafeHistoryName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name is not "." and not "..";

        private IEnumerable<(string Directory, string Date)> HistoryDirectories(OnnxType type, string? date)
        {
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    var normalized = parsed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    yield return (Path.Combine(_config.BasePath, normalized, type.ToString()), normalized);
                }
                yield break;
            }

            if (!Directory.Exists(_config.BasePath)) { yield break; }
            foreach (var directory in Directory.EnumerateDirectories(_config.BasePath).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
            {
                var candidate = Path.GetFileName(directory);
                if (DateTime.TryParseExact(candidate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                {
                    yield return (Path.Combine(directory, type.ToString()), candidate);
                }
            }
        }

        private string? FindHistoryFile(string name, OnnxType type, string? date, string namingFormat, out string? resolvedDate)
        {
            var expectedName = string.Format(namingFormat, name);
            if (expectedName != Path.GetFileName(expectedName)) { resolvedDate = null; return null; }
            foreach (var candidate in HistoryDirectories(type, date))
            {
                var path = Path.Combine(candidate.Directory, expectedName);
                if (System.IO.File.Exists(path)) { resolvedDate = candidate.Date; return path; }
            }
            resolvedDate = null;
            return null;
        }

        private bool TryFindHistorySet(string name, OnnxType type, string? date, out string originalPath, out string resultPath,
            out string detailsPath, out string resolvedDate)
        {
            foreach (var candidate in HistoryDirectories(type, date))
            {
                var originalName = string.Format(_config.OriginalImageNamingFormat, name);
                var resultName = string.Format(_config.ResultImageNamingFormat, name);
                var detailsName = string.Format(_config.DetailsNamingFormat, name);
                if (originalName != Path.GetFileName(originalName) || resultName != Path.GetFileName(resultName) || detailsName != Path.GetFileName(detailsName)) { break; }
                originalPath = Path.Combine(candidate.Directory, originalName);
                resultPath = Path.Combine(candidate.Directory, resultName);
                detailsPath = Path.Combine(candidate.Directory, detailsName);
                if (System.IO.File.Exists(originalPath) && System.IO.File.Exists(resultPath) && System.IO.File.Exists(detailsPath))
                {
                    resolvedDate = candidate.Date;
                    return true;
                }
            }
            originalPath = resultPath = detailsPath = resolvedDate = string.Empty;
            return false;
        }

        #region 识别核心方法

        private Task<OperateResult> RunIdentityAsync(IdentityOperate operate, IData data)
        {
            CancellationToken token = HttpContext.RequestAborted;
            return data switch
            {
                ObjectDetectionData value => operate.RunAsync(value, token),
                SegmentationData value => operate.RunAsync(value, token),
                ClassificationData value => operate.RunAsync(value, token),
                PoseEstimationData value => operate.RunAsync(value, token),
                ObbDetectionData value => operate.RunAsync(value, token),
                _ => Task.FromResult(OperateResult.CreateFailureResult("Unsupported inference data type."))
            };
        }

        /// <summary>
        /// 识别核心逻辑（仅返回坐标数据，不绘制图片）
        /// </summary>
        /// <param name="onnxIndex">数据库模型下标</param>
        /// <param name="file">识别的文件</param>
        /// <param name="paramJson">识别基础属性 JSON</param>
        /// <param name="createProvider">根据模型路径创建硬件执行提供程序的委托</param>
        /// <returns>识别结果</returns>
        protected async Task<OperateResult> IdentityCoreAsync(int onnxIndex, IFormFile file, string paramJson, Func<string, IExecutionProvider> createProvider)
        {
            if (file.Length <= 0 || file.Length > _config.MaxImageBytes)
            {
                return OperateResult.CreateFailureResult($"Image file size must be between 1 byte and {_config.MaxImageBytes} bytes.");
            }
            OperateResult result = await QueryAsync(onnxIndex);
            if (result.GetDetails(out List<OnnxData>? datas) && datas is { Count: > 0 })
            {
                OnnxData onnxData = datas[0];
                if (string.IsNullOrWhiteSpace(onnxData.path) || string.IsNullOrWhiteSpace(onnxData.name)) { return OperateResult.CreateFailureResult("Model file is missing."); }
                IdentityOperate operate = IdentityOperate.Instance(new IdentityData
                {
                    SN = $"{PublicHandler.DefaultSN}-{Tag}-{onnxIndex}",
                    Hardware = createProvider(Path.Combine(onnxData.path, onnxData.name)),
                    IdentifyType = onnxData.onnxType ?? OnnxType.ObjectDetection,
                });

                byte[] bytes = await file.GetBytesAsync(HttpContext.RequestAborted);
                IData data = (onnxData.onnxType ?? OnnxType.ObjectDetection) switch
                {
                    OnnxType.ObjectDetection => paramJson.ToJsonEntity<ObjectDetectionData>() ?? new ObjectDetectionData(),
                    OnnxType.Segmentation => paramJson.ToJsonEntity<SegmentationData>() ?? new SegmentationData(),
                    OnnxType.Classification => paramJson.ToJsonEntity<ClassificationData>() ?? new ClassificationData(),
                    OnnxType.PoseEstimation => paramJson.ToJsonEntity<PoseEstimationData>() ?? new PoseEstimationData(),
                    OnnxType.ObbDetection => paramJson.ToJsonEntity<ObbDetectionData>() ?? new ObbDetectionData(),
                    _ => new ObjectDetectionData(),
                };
                switch (data)
                {
                    case ObjectDetectionData value: value.File = bytes; break;
                    case SegmentationData value: value.File = bytes; break;
                    case ClassificationData value: value.File = bytes; break;
                    case PoseEstimationData value: value.File = bytes; break;
                    case ObbDetectionData value: value.File = bytes; break;
                }
                return await RunIdentityAsync(operate, data);
            }
            return result;
        }

        /// <summary>
        /// 识别核心逻辑（含绘制图片、保存原图与标注图、返回 URL）
        /// </summary>
        /// <param name="onnxIndex">数据库模型下标</param>
        /// <param name="file">识别的文件</param>
        /// <param name="paramJson">识别基础属性 JSON</param>
        /// <param name="createProvider">根据模型路径创建硬件执行提供程序的委托</param>
        /// <returns>识别结果（含绘制后图片 URL 与坐标数据）</returns>
        protected async Task<OperateResult> IdentityDrawCoreAsync(int onnxIndex, IFormFile file, string paramJson, Func<string, IExecutionProvider> createProvider)
        {
            if (file.Length <= 0 || file.Length > _config.MaxImageBytes)
            {
                return OperateResult.CreateFailureResult($"Image file size must be between 1 byte and {_config.MaxImageBytes} bytes.");
            }
            byte[] imageBytes = await file.GetBytesAsync(HttpContext.RequestAborted);
            using SKImage? image = SKImage.FromEncodedData(imageBytes);
            if (image is null)
            {
                return OperateResult.CreateFailureResult("The uploaded file is not a valid image.");
            }

            OperateResult result = await QueryAsync(onnxIndex);
            if (result.GetDetails(out List<OnnxData>? datas) && datas is { Count: > 0 })
            {
                string ms = DateTime.Now.ToString(_config.NameFormat);
                TimeHandler.Instance(ms).StartRecord();

                OnnxData onnxData = datas[0];
                if (string.IsNullOrWhiteSpace(onnxData.path) || string.IsNullOrWhiteSpace(onnxData.name)) { return OperateResult.CreateFailureResult("Model file is missing."); }
                var modelType = onnxData.onnxType ?? OnnxType.ObjectDetection;
                IdentityOperate operate = IdentityOperate.Instance(new IdentityData
                {
                    SN = $"{PublicHandler.DefaultSN}-{Tag}-{onnxIndex}",
                    Hardware = createProvider(Path.Combine(onnxData.path, onnxData.name)),
                    IdentifyType = modelType,
                });

                switch (modelType)
                {
                    case OnnxType.ObjectDetection:
                        ObjectDetectionData objectDetection = paramJson.ToJsonEntity<ObjectDetectionData>() ?? new ObjectDetectionData();
                        objectDetection.File = imageBytes;
                        result = await operate.RunAsync(objectDetection, HttpContext.RequestAborted);
                        if (result.GetDetails(out List<ObjectDetectionResultData>? objectDetectionResultDatas) && objectDetectionResultDatas is { Count: > 0 })
                        {
                            List<ObjectDetection> datasResult = objectDetectionResultDatas.ToObjectDetection();
                            using SKBitmap sKBitmap = image.Draw(datasResult);
                            byte[] ibytes = sKBitmap.GetImageByte(out _);
                            string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, objectDetectionResultDatas, modelType, _config);
                            string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ObjectDetectionResultData>>(objectDetectionResultDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                        }
                        break;
                    case OnnxType.Segmentation:
                        SegmentationData segmentation = paramJson.ToJsonEntity<SegmentationData>() ?? new SegmentationData();
                        segmentation.File = imageBytes;
                        result = await operate.RunAsync(segmentation, HttpContext.RequestAborted);
                        if (result.GetDetails(out List<SegmentationResultData>? segmentationDatas) && segmentationDatas is { Count: > 0 })
                        {
                            List<Segmentation> datasResult = segmentationDatas.ToSegmentation();
                            using SKBitmap sKBitmap = image.Draw(datasResult);
                            byte[] ibytes = sKBitmap.GetImageByte(out _);
                            string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, segmentationDatas, modelType, _config);
                            string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<SegmentationResultData>>(segmentationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                        }
                        break;
                    case OnnxType.Classification:
                        ClassificationData classification = paramJson.ToJsonEntity<ClassificationData>() ?? new ClassificationData();
                        classification.File = imageBytes;
                        result = await operate.RunAsync(classification, HttpContext.RequestAborted);
                        if (result.GetDetails(out List<ClassificationResultData>? classificationDatas) && classificationDatas is { Count: > 0 })
                        {
                            List<Classification> datasResult = classificationDatas.ToClassification();
                            using SKBitmap sKBitmap = image.Draw(datasResult);
                            byte[] ibytes = sKBitmap.GetImageByte(out _);
                            string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, classificationDatas, modelType, _config);
                            string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ClassificationResultData>>(classificationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                        }
                        break;
                    case OnnxType.PoseEstimation:
                        PoseEstimationData poseEstimation = paramJson.ToJsonEntity<PoseEstimationData>() ?? new PoseEstimationData();
                        poseEstimation.File = imageBytes;
                        result = await operate.RunAsync(poseEstimation, HttpContext.RequestAborted);
                        if (result.GetDetails(out List<PoseEstimationResultData>? poseEstimationDatas) && poseEstimationDatas is { Count: > 0 })
                        {
                            List<PoseEstimation> datasResult = poseEstimationDatas.ToPoseEstimation();
                            using SKBitmap sKBitmap = image.Draw(datasResult, new PoseDrawingOptions { KeyPointMarkers = _poseHandler.GetKeyPoints(), PoseConfidence = poseEstimation.Confidence, BorderThickness = 3 });
                            byte[] ibytes = sKBitmap.GetImageByte(out _);
                            string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, poseEstimationDatas, modelType, _config);
                            string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<PoseEstimationResultData>>(poseEstimationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                        }
                        break;
                    case OnnxType.ObbDetection:
                        ObbDetectionData obbDetection = paramJson.ToJsonEntity<ObbDetectionData>() ?? new ObbDetectionData();
                        obbDetection.File = imageBytes;
                        result = await operate.RunAsync(obbDetection, HttpContext.RequestAborted);
                        if (result.GetDetails(out List<ObbDetectionResultData>? obbDetections) && obbDetections is { Count: > 0 })
                        {
                            List<OBBDetection> datasResult = obbDetections.ToObbDetection();
                            using SKBitmap sKBitmap = image.Draw(datasResult);
                            byte[] ibytes = sKBitmap.GetImageByte(out _);
                            string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, obbDetections, modelType, _config);
                            string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = modelType }, Request.Scheme) ?? string.Empty;
                            return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ObbDetectionResultData>>(obbDetections, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                        }
                        break;
                }
                return OperateResult.CreateFailureResult("Identity Failure", TimeHandler.Instance(ms).StopRecord().milliseconds);
            }
            return result;
        }

        #endregion

    }
}
