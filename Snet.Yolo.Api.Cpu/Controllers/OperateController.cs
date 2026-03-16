using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Snet.Model.data;
using Snet.Yolo.Api.Attribute;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using YoloDotNet.ExecutionProvider.Cpu;

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

        /// <inheritdoc/>
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
        public Task<OperateResult> IdentityAsync(int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, string paramJson)
            => IdentityCoreAsync(onnxIndex, file, paramJson, path => new CpuExecutionProvider(path));

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
        public Task<OperateResult> IdentityDrawAsync(int onnxIndex, [AllowedFileType(new[] { ".jpg", ".jpeg", ".png", ".bmp" })] IFormFile file, string paramJson)
            => IdentityDrawCoreAsync(onnxIndex, file, paramJson, path => new CpuExecutionProvider(path));
    }
}
