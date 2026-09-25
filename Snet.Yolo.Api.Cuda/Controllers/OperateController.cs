using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Snet.Model.data;
using Snet.Yolo.Api.Attribute;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Api.Services;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.ExecutionProvider.Cuda.TensorRT;

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
        /// <param name="poseHandler">姿态关键点颜色处理器。</param>
        /// <param name="sessionCache">可复用的识别会话缓存。</param>
        public OperateController(ManageOperate operate, IOptions<ConfigModel> config, PoseEstimationCustomKeyPointColorHandler poseHandler, InferenceSessionCache sessionCache) : base(operate, config, poseHandler, sessionCache)
        {

        }

        /// <inheritdoc/>
        public override string Tag => "Cuda";

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
        /// <param name="gpuid">显卡ID，默认0</param>
        /// <param name="trtConfig">TensorRt 配置，可为空</param>
        /// <returns>
        /// 识别结果<br/>
        /// 返回识别到的坐标数据
        /// </returns>
        [HttpPost]
        public Task<OperateResult> IdentityAsync([FromForm] int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, [FromForm] string paramJson, [FromForm] int gpuid = 0, [FromForm] TensorRt? trtConfig = null)
        {
            if (!TryCreateTensorRtConfig(onnxIndex, gpuid, trtConfig, out var safeConfig, out var error))
            {
                return Task.FromResult(OperateResult.CreateFailureResult(error!));
            }
            return IdentityCoreAsync(onnxIndex, file, paramJson, ProviderKey(gpuid, safeConfig), path => new CudaExecutionProvider(path, gpuid, safeConfig));
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
        /// <param name="gpuid">显卡ID，默认0</param>
        /// <param name="trtConfig">TensorRt 配置，可为空</param>
        /// <returns>
        /// 识别结果<br/>
        /// 绘制后图片包含坐标数据
        /// </returns>
        [HttpPost]
        public Task<OperateResult> IdentityDrawAsync([FromForm] int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, [FromForm] string paramJson, [FromForm] int gpuid = 0, [FromForm] TensorRt? trtConfig = null)
        {
            if (!TryCreateTensorRtConfig(onnxIndex, gpuid, trtConfig, out var safeConfig, out var error))
            {
                return Task.FromResult(OperateResult.CreateFailureResult(error!));
            }
            return IdentityDrawCoreAsync(onnxIndex, file, paramJson, ProviderKey(gpuid, safeConfig), path => new CudaExecutionProvider(path, gpuid, safeConfig));
        }

        /// <summary>将 TensorRT 文件访问限制在服务器管理的缓存目录内。</summary>
        private static bool TryCreateTensorRtConfig(int modelIndex, int gpuId, TensorRt? requested, out TensorRt? result, out string? error)
        {
            result = null;
            error = null;
            if (gpuId < 0) { error = "GPU id cannot be negative."; return false; }
            if (requested is null) { return true; }
            if (requested.BuilderOptimizationLevel is < 0 or > 5)
            {
                error = "TensorRT builder optimization level must be between 0 and 5.";
                return false;
            }

            string? calibration = null;
            if (!string.IsNullOrWhiteSpace(requested.Int8CalibrationCacheFile))
            {
                var fileName = Path.GetFileName(requested.Int8CalibrationCacheFile);
                if (!string.Equals(fileName, requested.Int8CalibrationCacheFile, StringComparison.Ordinal))
                {
                    error = "TensorRT calibration cache must be a file name, not a filesystem path.";
                    return false;
                }
                calibration = Path.Combine(AppContext.BaseDirectory, "tensorrt-calibration", fileName);
                if (!System.IO.File.Exists(calibration))
                {
                    error = "The requested TensorRT calibration cache is not installed on the server.";
                    return false;
                }
            }

            var cacheDirectory = Path.Combine(AppContext.BaseDirectory, "tensorrt-cache", $"gpu-{gpuId}", $"model-{modelIndex}");
            Directory.CreateDirectory(cacheDirectory);
            result = requested with
            {
                EngineCachePath = cacheDirectory,
                EngineCachePrefix = $"model-{modelIndex}",
                Int8CalibrationCacheFile = calibration,
            };
            return true;
        }

        /// <summary>根据 CUDA 执行设置生成缓存区分键。</summary>
        private static string ProviderKey(int gpuId, TensorRt? config)
            => config is null
                ? $"cuda:{gpuId}"
                : $"cuda:{gpuId}:trt:{config.Precision}:{config.BuilderOptimizationLevel}:{config.Int8CalibrationCacheFile}";
    }
}
