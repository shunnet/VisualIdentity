using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Api.Attribute;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;

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
        public virtual string Tag { get; set; }
        /// <summary>
        /// 操作控制器<br/>
        /// 有参构造函数
        /// </summary>
        /// <param name="operate">管理操作</param>
        /// <param name="config">配置</param>
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
        [HttpGet]
        public async Task<OperateResult> QueryAsync(int index) => await _operate.QueryAsync(index);

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <returns>结果</returns>
        [HttpGet]
        public async Task<OperateResult> QuerysAsync() => await _operate.QueryAsync();

        /// <summary>
        /// 获取本地原始的图片
        /// </summary>
        /// <param name="name">
        /// 图片名称（文件名“时间区域”的一部分，不包含扩展名）
        /// </param>
        /// <param name="type">
        /// 模型类型（用于定位子目录）
        /// </param>
        /// <returns>
        /// 成功时返回图片文件，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public IActionResult GetOriginalImage(string name, OnnxType type)
        {
            // 参数校验：name 不能为空
            if (string.IsNullOrEmpty(name))
                return BadRequest("Parameter 'name' cannot be null or empty.");

            // 拼接目录路径：BasePath/yyyy-MM-dd/OnnxType
            string directory = Path.Combine(_config.BasePath, DateTime.Now.ToString("yyyy-MM-dd"), type.ToString());

            // 判断目录是否存在
            if (!Directory.Exists(directory))
                return NotFound("Target directory does not exist.");

            // 获取目录下的所有文件
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);

            // 按照配置规则格式化目标文件名
            string expectedFileName = string.Format(_config.OriginalImageNamingFormat, name);

            // 查找第一个匹配的文件
            string path = files.FirstOrDefault(f => Path.GetFileName(f).Contains(expectedFileName));

            // 校验文件是否存在
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return NotFound("Target file not found.");

            // 读取文件字节数据
            byte[] bytes = System.IO.File.ReadAllBytes(path);

            // 以 image/jpeg 格式返回图片
            return File(bytes, "image/jpeg");
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
        /// <returns>
        /// 成功时返回图片文件，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public IActionResult GetMarkImage(string name, OnnxType type)
        {
            // 参数校验：name 不能为空
            if (string.IsNullOrEmpty(name))
                return BadRequest("Parameter 'name' cannot be null or empty.");

            // 拼接目录路径：BasePath/yyyy-MM-dd/OnnxType
            string directory = Path.Combine(_config.BasePath, DateTime.Now.ToString("yyyy-MM-dd"), type.ToString());

            // 判断目录是否存在
            if (!Directory.Exists(directory))
                return NotFound("Target directory does not exist.");

            // 获取目录下的所有文件
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);

            // 按照配置规则格式化目标文件名
            string expectedFileName = string.Format(_config.ResultImageNamingFormat, name);

            // 查找第一个匹配的文件
            string path = files.FirstOrDefault(f => Path.GetFileName(f).Contains(expectedFileName));

            // 校验文件是否存在
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return NotFound("Target file not found.");

            // 读取文件字节数据
            byte[] bytes = System.IO.File.ReadAllBytes(path);

            // 以 image/jpeg 格式返回图片
            return File(bytes, "image/jpeg");
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
        /// <returns>
        /// 成功时返回有原图地址，标注后的图片地址，还有坐标，失败时返回错误信息
        /// </returns>
        [HttpGet]
        public async Task<OperateResult> GetImageDetails(string name, OnnxType type)
        {
            string ms = DateTime.Now.ToString(_config.NameFormat);
            TimeHandler.Instance(ms).StartRecord();
            // 参数校验：name 不能为空
            if (string.IsNullOrEmpty(name))
                return OperateResult.CreateFailureResult("Parameter 'name' cannot be null or empty.", TimeHandler.Instance(ms).StopRecord().milliseconds);

            // 拼接目录路径：BasePath/yyyy-MM-dd/OnnxType
            string directory = Path.Combine(_config.BasePath, DateTime.Now.ToString("yyyy-MM-dd"), type.ToString());

            // 判断目录是否存在
            if (!Directory.Exists(directory))
                return OperateResult.CreateFailureResult("Target directory does not exist.", TimeHandler.Instance(ms).StopRecord().milliseconds);

            // 获取目录下的所有文件
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);

            // 原图
            string OriginalImageNamingFormat = string.Format(_config.OriginalImageNamingFormat, name);
            // 原图匹配的文件
            string OriginalImageNamingFormatPath = files.FirstOrDefault(f => Path.GetFileName(f).Contains(OriginalImageNamingFormat));


            // 标注后的图
            string ResultImageNamingFormat = string.Format(_config.ResultImageNamingFormat, name);
            // 标注后的图匹配的文件
            string ResultImageNamingFormatPath = files.FirstOrDefault(f => Path.GetFileName(f).Contains(ResultImageNamingFormat));


            // 详情
            string DetailsNamingFormat = string.Format(_config.DetailsNamingFormat, name);
            // 详情匹配的文件
            string DetailsNamingFormatPath = files.FirstOrDefault(f => Path.GetFileName(f).Contains(DetailsNamingFormat));
            // Json数据
            object DetailsNamingFormatObject = System.IO.File.ReadAllText(DetailsNamingFormatPath).ToJsonEntity<object>();

            // 校验文件是否存在
            if (string.IsNullOrEmpty(OriginalImageNamingFormatPath) || !System.IO.File.Exists(OriginalImageNamingFormatPath) &&
                string.IsNullOrEmpty(ResultImageNamingFormatPath) || !System.IO.File.Exists(ResultImageNamingFormatPath) &&
                string.IsNullOrEmpty(DetailsNamingFormatPath) || !System.IO.File.Exists(DetailsNamingFormatPath))
                return OperateResult.CreateFailureResult("Target file not found.", TimeHandler.Instance(ms).StopRecord().milliseconds);

            // 原图地址
            string OriginalImageNamingFormatUrl = Url.Action("GetOriginalImage", "Operate", new { name = name, type = type }, Request.Scheme);
            // 标注后地址
            string ResultImageNamingFormatUrl = Url.Action("GetMarkImage", "Operate", new { name = name, type = type }, Request.Scheme);

            return OperateResult.CreateSuccessResult("GetImageDetails Success", new IdentityResultData<object>(DetailsNamingFormatObject, ResultImageNamingFormatUrl, OriginalImageNamingFormatUrl), TimeHandler.Instance(ms).StopRecord().milliseconds);

        }

    }
}
