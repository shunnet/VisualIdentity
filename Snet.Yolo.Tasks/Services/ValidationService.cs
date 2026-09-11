using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using YoloDotNet.ExecutionProvider.Cpu;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 验证服务：模型管理(ManageOperate) + 本机推理(IdentityOperate)。
/// </summary>
public sealed class ValidationService
{
    private readonly ManageOperate _manage;
    private readonly CurrentUserContext _currentUser;
    public ValidationService(ManageOperate manage, CurrentUserContext currentUser) { _manage = manage; _currentUser = currentUser; }

    /// <summary>查询全部模型（清理文件已不存在的失效行）。</summary>
    public async Task<IReadOnlyList<OnnxData>> GetModelsAsync()
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var r = await _manage.QueryByOwnerAsync(owner);
        if (!r.GetDetails(out List<OnnxData>? list) || list is null) { return Array.Empty<OnnxData>(); }
        var valid = new List<OnnxData>();
        foreach (var m in list)
        {
            var p = Path.Combine(m.path ?? "", m.name ?? "");
            if (File.Exists(p)) { valid.Add(m); }
            else { await _manage.DeleteAsync(owner, m.index, true); }
        }
        return valid;
    }

    /// <summary>添加模型（保存到程序集目录 wwwroot/onnxs，不删）。</summary>
    public async Task<OperateResult> AddModelAsync(Stream onnx, string fileName, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType type)
        => await AddModelForOwnerAsync(await _currentUser.GetRequiredUserNameAsync(), onnx, fileName, describe, type);

    internal async Task<OperateResult> AddModelForOwnerAsync(string owner, Stream onnx, string fileName, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType type)
    {
        var savePath = Path.Combine(PublicHandler.DefaultPath, "onnxs", UserStoragePath.Segment(owner));
        if (!Directory.Exists(savePath)) { Directory.CreateDirectory(savePath); }
        var safeName = (Path.GetFileNameWithoutExtension(fileName ?? "model").Replace("..", "").Replace("/", "").Replace("\\", "")) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".onnx";
        var filePath = Path.Combine(savePath, safeName);
        try
        {
            await using (var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await onnx.CopyToAsync(file);
            }
            var result = await _manage.AddAsync(owner, filePath, describe, type);
            if (!result.Status) { File.Delete(filePath); }
            return result;
        }
        catch
        {
            try { File.Delete(filePath); } catch { }
            throw;
        }
    }

    public async Task<OperateResult> DeleteModelAsync(int index) => await _manage.DeleteAsync(await _currentUser.GetRequiredUserNameAsync(), index, true);

    public async Task DeleteValidationImageAsync(string? imageUrl)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var ownerSegment = UserStoragePath.Segment(owner);
        var prefix = $"/uploads/{ownerSegment}/validation/";
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(prefix, StringComparison.Ordinal)) { return; }
        var fileName = Uri.UnescapeDataString(imageUrl[prefix.Length..]);
        if (fileName != Path.GetFileName(fileName) || fileName is "." or "..") { return; }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"));
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>更新模型。</summary>
    public async Task<OperateResult> UpdateModelAsync(int index, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType? type) => await _manage.UpdateAsync(await _currentUser.GetRequiredUserNameAsync(), index, describe, type);

    /// <summary>返回当前用户的进程生命周期验证图片目录及 URL 前缀。</summary>
    public async ValueTask<(string Directory, string UrlPrefix)> GetValidationUploadLocationAsync()
    {
        var ownerSegment = UserStoragePath.Segment(await _currentUser.GetRequiredUserNameAsync());
        return (Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"), $"/uploads/{ownerSegment}/validation/");
    }

    /// <summary>本机推理（进程内 IdentityOperate，用法对齐 Api.Shared）。</summary>
    public async Task<OperateResult> RunLocalAsync(OnnxData model, byte[] image, string paramJson)
    {
        var provider = new CpuExecutionProvider(Path.Combine(model.path ?? "", model.name ?? ""));
        using var operate = new IdentityOperate(new IdentityData
        {
            SN = $"{PublicHandler.DefaultSN}-local",
            Hardware = provider,
            IdentifyType = model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection,
        });
        var dataType = model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection;
        paramJson = WithDefaults(paramJson, dataType);
        IData data;
        switch (dataType)
        {
            case global::Snet.Yolo.Server.models.@enum.OnnxType.Classification:
                { var d = FromJson<ClassificationData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.Segmentation:
                { var d = FromJson<SegmentationData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.ObbDetection:
                { var d = FromJson<ObbDetectionData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.PoseEstimation:
                { var d = FromJson<PoseEstimationData>(paramJson); d.File = image; data = d; }
                break;
            default:
                { var d = FromJson<ObjectDetectionData>(paramJson); d.File = image; data = d; }
                break;
        }
        return await operate.RunAsync(data);
    }

    /// <summary>识别参数兜底默认值（对齐 WPF 工具）：缺失键补齐，兼容历史键名(如 PixelConfedence)。</summary>
    private static string WithDefaults(string json, global::Snet.Yolo.Server.models.@enum.OnnxType type)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? new();
            void Set(string k, double v) { if (node[k] is null) { node[k] = v; } }
            Set("Confidence", 0.25); Set("Iou", 0.45);
            if (type == global::Snet.Yolo.Server.models.@enum.OnnxType.Segmentation)
            {
                node["PixelConfidence"] ??= node["PixelConfedence"] ?? 0.65;
            }
            if (type == global::Snet.Yolo.Server.models.@enum.OnnxType.Classification) { Set("Classes", 1); }
            return node.ToJsonString();
        }
        catch { return json; }
    }

    private static T FromJson<T>(string json) where T : new()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T(); }
        catch { return new T(); }
    }
}
