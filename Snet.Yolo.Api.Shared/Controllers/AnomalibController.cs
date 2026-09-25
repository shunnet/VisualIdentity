using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server.Anomalib;

namespace Snet.Yolo.Api.Controllers;

/// <summary>管理 Anomalib ONNX 模型包，并通过 Server 执行图片识别。</summary>
[ApiController]
[Route("api/anomalib")]
public sealed class AnomalibController(
    AnomalibModelRegistry models,
    AnomalibOnnxInference inference,
    IOptions<ConfigModel> configuration) : ControllerBase
{
    private const string ApiOwner = "snet";

    /// <summary>列出 API 服务账户的模型。</summary>
    [HttpGet("models")]
    public async Task<IActionResult> ListAsync()
        => Ok((await models.ListAsync(ApiOwner, HttpContext.RequestAborted)).Select(ToResponse));

    /// <summary>获取 API 服务账户的指定模型。</summary>
    [HttpGet("models/{projectId}/{runId}")]
    public async Task<IActionResult> GetAsync(string projectId, string runId)
    {
        var model = await models.FindAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted);
        return model is null ? NotFound() : Ok(ToResponse(model));
    }

    /// <summary>导入包含 model.onnx 和 model.manifest.json 的 ZIP 模型包。</summary>
    [HttpPost("models")]
    [RequestSizeLimit(AnomalibModelRegistry.MaximumPackageBytes + 1024 * 1024)]
    public async Task<IActionResult> ImportAsync(IFormFile file, [FromForm] string name,
        [FromForm] string? description, [FromForm] AnomalibModelKind modelKind)
    {
        if (file is null || file.Length is <= 0 or > AnomalibModelRegistry.MaximumPackageBytes
            || !Enum.IsDefined(modelKind))
        {
            return BadRequest("A valid Anomalib model ZIP and model kind are required.");
        }

        try
        {
            await using var source = file.OpenReadStream();
            var model = await models.ImportAsync(ApiOwner, source, name, description ?? string.Empty,
                modelKind, cancellationToken: HttpContext.RequestAborted);
            return Ok(ToResponse(model));
        }
        catch (AnomalibModelImportException error)
        {
            return BadRequest(new { error.ResourceKey });
        }
    }

    /// <summary>更新模型名称和描述；模型算法不可更改。</summary>
    [HttpPut("models/{projectId}/{runId}")]
    public async Task<IActionResult> UpdateAsync(string projectId, string runId,
        [FromBody] UpdateAnomalibModelRequest request)
    {
        if (request is null) { return BadRequest(); }
        try
        {
            if (!await models.UpdateAsync(ApiOwner, projectId, runId, request.Name,
                request.Description, HttpContext.RequestAborted))
            {
                return NotFound();
            }
            var updated = await models.FindAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted);
            return updated is null ? NotFound() : Ok(ToResponse(updated));
        }
        catch (AnomalibModelImportException error)
        {
            return BadRequest(new { error.ResourceKey });
        }
    }

    /// <summary>删除已注册模型，不删除所属项目的图片。</summary>
    [HttpDelete("models/{projectId}/{runId}")]
    public async Task<IActionResult> DeleteAsync(string projectId, string runId)
    {
        var model = await models.FindAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted);
        if (model is null) { return NotFound(); }
        inference.Release(model.OnnxPath);
        return await models.DeleteAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted)
            ? Ok() : NotFound();
    }

    /// <summary>将 ONNX 模型及其部署清单打包为 ZIP 下载。</summary>
    [HttpGet("models/{projectId}/{runId}/download")]
    public async Task<IActionResult> DownloadAsync(string projectId, string runId)
    {
        var model = await models.FindAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted);
        if (model is null) { return NotFound(); }
        var package = await AnomalibModelRegistry.OpenPackageDownloadAsync(model, HttpContext.RequestAborted);
        return File(package, "application/zip", $"{runId}.zip");
    }

    /// <summary>使用指定的已注册 Anomalib 模型识别单张图片。</summary>
    [HttpPost("models/{projectId}/{runId}/identify")]
    public async Task<IActionResult> IdentifyAsync(string projectId, string runId,
        IFormFile file, [FromForm] bool includeHeatmap = false)
    {
        var model = await models.FindAsync(ApiOwner, projectId, runId, HttpContext.RequestAborted);
        if (model is null) { return NotFound(); }
        var maxBytes = configuration.Value.MaxImageBytes;
        if (file is null || file.Length <= 0 || file.Length > maxBytes)
        {
            return BadRequest($"Image size must be between 1 and {maxBytes} bytes.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"snet-anomalib-{Guid.NewGuid():N}.image");
        try
        {
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(destination, HttpContext.RequestAborted);
            }

            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width is <= 0 or > 16384 || codec.Info.Height is <= 0 or > 16384
                || (long)codec.Info.Width * codec.Info.Height > 100_000_000)
            {
                return BadRequest("Image format or dimensions are invalid.");
            }

            var output = await inference.IdentifyAsync(model.OnnxPath, model.ManifestPath, path,
                HttpContext.RequestAborted, includeHeatmap);
            return Ok(output);
        }
        catch (InvalidDataException error)
        {
            return BadRequest(error.Message);
        }
        finally
        {
            if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
        }
    }

    private static object ToResponse(RegisteredAnomalibModel model) => new
    {
        model.ProjectId,
        model.RunId,
        model.Name,
        model.Description,
        model.Model,
        model.RegisteredAtUtc,
    };
}

/// <summary>可编辑的 Anomalib 模型展示信息。</summary>
public sealed class UpdateAnomalibModelRequest
{
    /// <summary>新模型名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>可选的模型描述。</summary>
    public string? Description { get; set; }
}
