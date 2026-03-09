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
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Api.Controllers
{
    /// <summary>
    /// 控制器
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class OperateController : OperateBaseController
    {
        /// <summary>
        /// 操作控制器<br/>
        /// 有参构造函数
        /// </summary>
        /// <param name="operate">管理操作</param>
        /// <param name="config">配置</param>
        public OperateController(ManageOperate operate, IOptions<ConfigModel> config, PoseEstimationCustomKeyPointColorHandler poseHandler) : base(operate, config, poseHandler)
        {

        }

        /// #<inheritdoc/>
        public override string Tag => "Cpu";

        /// <summary>
        /// 识别<br/>
        /// 追求速度，不记录任何数据
        /// </summary>
        /// <param name="onnxIndex">数据库模型下标</param>
        /// <param name="file">识别的文件</param>
        /// <param name="paramJson">识别基础属性<br/>
        /// Classification：{"Classes":1}:分类数据<br/>
        /// ObbDetection：{"Confidence":0.2,"Iou":0.7}:定向检测数据<br/>
        /// ObjectDetection：{"Confidence":0.2,"Iou":0.7}:检测数据<br/>
        /// PoseEstimation：{"Confidence":0.2,"Iou":0.7}:姿态识别数据<br/>
        /// Segmentation：{"Confidence":0.2,"Iou":0.7,"PixelConfedence":0.65}:分割数据</param>
        /// <returns>
        /// 识别结果<br/>
        /// 返回识别到的坐标数据
        /// </returns>
        [HttpPost]
        public async Task<OperateResult> IdentityAsync(int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, string paramJson)
        {
            OperateResult result = await QueryAsync(onnxIndex);
            if (result.GetDetails(out List<OnnxData>? datas))
            {
                OnnxData onnxData = datas[0];
                IdentityOperate operate = IdentityOperate.Instance(new IdentityData
                {
                    SN = $"{PublicHandler.DefaultSN}-{Tag}",
                    Hardware = new CpuExecutionProvider(Path.Combine(onnxData.path, onnxData.name)),
                    IdentifyType = onnxData.onnxType ??= OnnxType.ObjectDetection,
                });

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();
                IData data = null;
                switch (onnxData.onnxType ??= OnnxType.ObjectDetection)
                {
                    case OnnxType.ObjectDetection:
                        ObjectDetectionData objectDetection = paramJson.ToJsonEntity<ObjectDetectionData>();
                        objectDetection.File = bytes;
                        data = objectDetection;
                        break;
                    case OnnxType.Segmentation:
                        SegmentationData segmentation = paramJson.ToJsonEntity<SegmentationData>();
                        segmentation.File = bytes;
                        data = segmentation;
                        break;
                    case OnnxType.Classification:
                        ClassificationData classification = paramJson.ToJsonEntity<ClassificationData>();
                        classification.File = bytes;
                        data = classification;
                        break;
                    case OnnxType.PoseEstimation:
                        PoseEstimationData poseEstimation = paramJson.ToJsonEntity<PoseEstimationData>();
                        poseEstimation.File = bytes;
                        data = poseEstimation;
                        break;
                    case OnnxType.ObbDetection:
                        ObbDetectionData obbDetection = paramJson.ToJsonEntity<ObbDetectionData>();
                        obbDetection.File = bytes;
                        data = obbDetection;
                        break;
                }
                return await operate.RunAsync(data);
            }
            return result;
        }

        /// <summary>
        /// 识别<br/>
        /// 返回依据坐标数据处理完成的绘制后图片包含坐标数据<br/>
        /// 绘制将占用大量时间<br/>
        /// 会把识别的原图与标注的图与数据存储，方便二次查看
        /// </summary>
        /// <param name="onnxIndex">数据库模型下标</param>
        /// <param name="file">识别的文件</param>
        /// <param name="paramJson">识别基础属性<br/>
        /// Classification：{"Classes":1}:分类数据<br/>
        /// ObbDetection：{"Confidence":0.2,"Iou":0.7}:定向检测数据<br/>
        /// ObjectDetection：{"Confidence":0.2,"Iou":0.7}:检测数据<br/>
        /// PoseEstimation：{"Confidence":0.2,"Iou":0.7}:姿态识别数据<br/>
        /// Segmentation：{"Confidence":0.2,"Iou":0.7,"PixelConfedence":0.65}:分割数据</param>
        /// <returns>
        /// 识别结果<br/>
        /// 绘制后图片包含坐标数据
        /// </returns>
        [HttpPost]
        public async Task<OperateResult> IdentityDrawAsync(int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, string paramJson)
        {
            OperateResult result = await QueryAsync(onnxIndex);
            if (result.GetDetails(out List<OnnxData>? datas))
            {
                string ms = DateTime.Now.ToString(_config.NameFormat);
                TimeHandler.Instance(ms).StartRecord();

                OnnxData onnxData = datas[0];
                IdentityOperate operate = IdentityOperate.Instance(new IdentityData
                {
                    SN = $"{PublicHandler.DefaultSN}-{Tag}",
                    Hardware = new CpuExecutionProvider(Path.Combine(onnxData.path, onnxData.name)),
                    IdentifyType = onnxData.onnxType ??= OnnxType.ObjectDetection,
                });

                string suffix = file.GetSuffix();
                byte[] imageBytes = await file.GetBytesAsync();
                using SKImage image = SKImage.FromEncodedData(imageBytes);

                switch (onnxData.onnxType ??= OnnxType.ObjectDetection)
                {
                    case OnnxType.ObjectDetection:
                        ObjectDetectionData objectDetection = paramJson.ToJsonEntity<ObjectDetectionData>();
                        objectDetection.File = imageBytes;
                        result = await operate.RunAsync(objectDetection);
                        if (result.GetDetails(out List<ObjectDetectionResultData>? objectDetectionResultDatas))
                        {
                            if (objectDetectionResultDatas.Count > 0)
                            {
                                List<ObjectDetection> datasResult = objectDetectionResultDatas.ToObjectDetection();
                                using SKBitmap sKBitmap = image.Draw(datasResult);
                                byte[] ibytes = sKBitmap.GteImageByte(out string contentType);
                                string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, objectDetectionResultDatas, onnxData.onnxType.Value, _config);
                                string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ObjectDetectionResultData>>(objectDetectionResultDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                            }
                        }
                        break;
                    case OnnxType.Segmentation:
                        SegmentationData segmentation = paramJson.ToJsonEntity<SegmentationData>();
                        segmentation.File = imageBytes;
                        result = await operate.RunAsync(segmentation);
                        if (result.GetDetails(out List<SegmentationResultData>? segmentationDatas))
                        {
                            if (segmentationDatas.Count > 0)
                            {
                                List<Segmentation> datasResult = segmentationDatas.ToSegmentation();
                                using SKBitmap sKBitmap = image.Draw(datasResult);
                                byte[] ibytes = sKBitmap.GteImageByte(out string contentType);
                                string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, segmentationDatas, onnxData.onnxType.Value, _config);
                                string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<SegmentationResultData>>(segmentationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                            }
                        }
                        break;
                    case OnnxType.Classification:
                        ClassificationData classification = paramJson.ToJsonEntity<ClassificationData>();
                        classification.File = imageBytes;
                        result = await operate.RunAsync(classification);
                        if (result.GetDetails(out List<ClassificationResultData>? classificationDatas))
                        {
                            if (classificationDatas.Count > 0)
                            {
                                List<Classification> datasResult = classificationDatas.ToClassification();
                                using SKBitmap sKBitmap = image.Draw(datasResult);
                                byte[] ibytes = sKBitmap.GteImageByte(out string contentType);
                                string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, classificationDatas, onnxData.onnxType.Value, _config);
                                string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ClassificationResultData>>(classificationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                            }
                        }
                        break;
                    case OnnxType.PoseEstimation:
                        PoseEstimationData poseEstimation = paramJson.ToJsonEntity<PoseEstimationData>();
                        poseEstimation.File = imageBytes;
                        result = await operate.RunAsync(poseEstimation);
                        if (result.GetDetails(out List<PoseEstimationResultData>? poseEstimationDatas))
                        {
                            if (poseEstimationDatas.Count > 0)
                            {
                                List<PoseEstimation> datasResult = poseEstimationDatas.ToPoseEstimation();
                                using SKBitmap sKBitmap = image.Draw(datasResult, new PoseDrawingOptions { KeyPointMarkers = _poseHandler.GetKeyPoints(), PoseConfidence = poseEstimation.Confidence, BorderThickness = 3 });
                                byte[] ibytes = sKBitmap.GteImageByte(out string contentType);
                                string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, poseEstimationDatas, onnxData.onnxType.Value, _config);
                                string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<PoseEstimationResultData>>(poseEstimationDatas, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                            }
                        }
                        break;
                    case OnnxType.ObbDetection:
                        ObbDetectionData obbDetection = paramJson.ToJsonEntity<ObbDetectionData>();
                        obbDetection.File = imageBytes;
                        result = await operate.RunAsync(obbDetection);
                        if (result.GetDetails(out List<ObbDetectionResultData>? obbDetections))
                        {
                            if (obbDetections.Count > 0)
                            {
                                List<OBBDetection> datasResult = obbDetections.ToObbDetection();
                                using SKBitmap sKBitmap = image.Draw(datasResult);
                                byte[] ibytes = sKBitmap.GteImageByte(out string contentType);
                                string name = await ImageHandler.SaveImageAsync(ibytes, imageBytes, obbDetections, onnxData.onnxType.Value, _config);
                                string GetMarkImageUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                string GetOriginalImageUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = onnxData.onnxType.Value }, Request.Scheme);
                                return OperateResult.CreateSuccessResult("Identity Success", new IdentityResultData<List<ObbDetectionResultData>>(obbDetections, GetMarkImageUrl, GetOriginalImageUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);
                            }
                        }
                        break;
                }
                return OperateResult.CreateFailureResult("Identity Failure", TimeHandler.Instance(ms).StopRecord().milliseconds);
            }
            return result;
        }
    }
}
