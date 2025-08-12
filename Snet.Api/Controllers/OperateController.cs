using Microsoft.AspNetCore.Mvc;
using Snet.Api.Attribute;
using Snet.Model.data;
using Snet.Server;
using Snet.Server.handler;
using Snet.Server.@interface;
using Snet.Server.models.data;
using Snet.Server.models.@enum;
using Snet.Unility;
using YoloDotNet.Core;

namespace Snet.Api.Controllers
{
    /// <summary>
    /// 操作
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class OperateController : ControllerBase
    {
        private OnnxOperate _operate;

        public OperateController(OnnxOperate operate)
        {
            _operate = operate;
        }

        /// <summary>
        /// 识别
        /// </summary>
        /// <param name="onnxIndex">数据库模型下标</param>
        /// <param name="file">识别的文件</param>
        /// <param name="paramJson">识别基础属性<br/>
        /// Classification：{"Classes":1}:分类数据<br/>
        /// ObbDetection：{"Confidence":0.2,"Iou":0.7}:定向检测数据<br/>
        /// ObjectDetection：{"Confidence":0.2,"Iou":0.7}:检测数据<br/>
        /// PoseEstimation：{"Confidence":0.2,"Iou":0.7}:姿态识别数据<br/>
        /// Segmentation：{"Confidence":0.2,"Iou":0.7,"PixelConfedence":0.65}:分割数据</param>
        /// <param name="hardwareJson">使用什么硬件来进行运算<br/>
        /// CPU：{"CpuExecutionProvider":{}}<br/>
        /// CUDA：{"CudaExecutionProvider":{"GpuId":0,"PrimeGpu":false}}<br/>
        /// NVIDIA：{"TensorRtExecutionProvider":{"Precision":0,"GpuId":0,"BuilderOptimizationLevel":3,"EngineCachePath":null,"Int8CalibrationCacheFile":null,"EngineCachePrefix":null}}<br/></param>
        /// <returns>
        /// 识别结果<br/>
        /// 返回识别到的坐标数据
        /// </returns>
        [HttpPost]
        public async Task<OperateResult> IdentityAsync(int onnxIndex, [AllowedFileType([".jpg", ".jpeg", ".png", ".bmp"])] IFormFile file, string paramJson, string hardwareJson)
        {
            OperateResult result = await QueryAsync(onnxIndex);
            if (result.GetDetails(out List<OnnxData>? datas))
            {
                OnnxData onnxData = datas[0];
                IdentityOperate operate = IdentityOperate.Instance(new IdentityData
                {
                    SN = PublicHandler.DefaultSN,
                    OnnxPath = Path.Combine(onnxData.path, onnxData.name),
                    Hardware = hardwareJson.ToJsonEntity<HardwareData>()?.GetHardware() ?? new CpuExecutionProvider(),
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
        /// 添加
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">模型类型</param>
        /// <returns>结果</returns>
        [HttpPost]
        public async Task<OperateResult> AddAsync([AllowedFileType([".onnx"])] IFormFile file, string describe, OnnxType onnxType)
        {
            var savePath = Path.Combine(PublicHandler.DefaultPath, "onnxs");
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            var filePath = Path.Combine(savePath, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            OperateResult result = await _operate.AddAsync(filePath, describe, onnxType);
            if (!result.Status)
            {
                System.IO.File.Delete(filePath);
            }
            return result;
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
        [HttpPost]
        public async Task<OperateResult> QueryAsync(int index) => await _operate.QueryAsync(index);

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <returns>结果</returns>
        [HttpPost]
        public async Task<OperateResult> QuerysAsync() => await _operate.QueryAsync();
    }
}
