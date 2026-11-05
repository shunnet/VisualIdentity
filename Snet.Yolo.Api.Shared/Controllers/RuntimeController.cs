using Microsoft.AspNetCore.Mvc;
using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Api.Services;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Api.Controllers;

/// <summary>查询与设置整个 API 实例的 YOLO、Anomalib ONNX Runtime 运算硬件。</summary>
/// <param name="runtime">硬件配置与 YOLO 缓存切换服务。</param>
/// <param name="anomalib">需要退役旧硬件缓存的 Anomalib 推理服务。</param>
[ApiController]
[Route("api/runtime/hardware")]
public sealed class RuntimeController(OnnxRuntimeService runtime, AnomalibOnnxInference anomalib) : ControllerBase
{
    /// <summary>查询当前配置；全新部署默认 GPU 0，查询不初始化 GPU。</summary>
    /// <returns>执行硬件 Cpu/Cuda 及 GPU 编号。</returns>
    [HttpGet]
    public ActionResult<OnnxRuntimeSettings> Get() => Ok(runtime.Current);

    /// <summary>持久保存全局硬件配置，新请求生效；活动识别完成后释放旧会话。</summary>
    /// <param name="request">Cpu/Cuda 与非负 GPU 编号；Cuda 会先检查原生环境。</param>
    /// <returns>保存后的配置；无效参数返回 400，CUDA 不可用返回 503，写入失败返回 500。</returns>
    [HttpPut]
    public ActionResult<OnnxRuntimeSettings> Set([FromBody] SetRuntimeHardwareRequest request)
    {
        try
        {
            var settings = runtime.Configure(new OnnxRuntimeSettings { Hardware = request.Hardware, GpuId = request.GpuId }, HttpContext.RequestAborted);
            anomalib.Clear();
            return Ok(settings);
        }
        catch (ArgumentException error) { return BadRequest(new ProblemDetails { Title = "运算硬件参数无效。", Detail = error.Message, Status = 400 }); }
        catch (Exception error) when (error is OnnxRuntimeException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        { return Problem(title: "运算硬件不可用，已保留原配置。", detail: error.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Problem(title: "运算硬件配置保存失败，已保留原配置。", detail: error.Message, statusCode: StatusCodes.Status500InternalServerError); }
    }
}
