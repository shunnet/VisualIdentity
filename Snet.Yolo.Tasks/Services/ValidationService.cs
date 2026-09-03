using Snet.Yolo.Server;
using Snet.Yolo.Server.@interface;
using YoloDotNet.ExecutionProvider.Cpu;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models;
using Snet.Yolo.Server.models.data;
using Snet.Model.data;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 验证服务：模型管理(ManageOperate) + 本机推理(IdentityOperate)。
/// </summary>
public sealed class ValidationService
{
    private readonly ManageOperate _manage;
    public ValidationService(ManageOperate manage) { _manage = manage; }

    /// <summary>查询全部模型（清理文件已不存在的失效行）。</summary>
    public async Task<IReadOnlyList<OnnxData>> GetModelsAsync()
    {
        var r = await _manage.QueryAsync();
        if (!r.GetDetails(out List<OnnxData>? list)) { return new List<OnnxData>(); }
        var valid = new List<OnnxData>();
        foreach (var m in list)
        {
            var p = Path.Combine(m.path ?? "", m.name ?? "");
            if (File.Exists(p)) { valid.Add(m); }
            else { await _manage.DeleteAsync(m.index, true); }
        }
        return valid;
    }

    /// <summary>添加模型（保存到程序集目录 wwwroot/onnxs，不删）。</summary>
    public async Task<OperateResult> AddModelAsync(Stream onnx, string fileName, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType type)
    {
        var savePath = Path.Combine(PublicHandler.DefaultPath, "onnxs");
        if (!Directory.Exists(savePath)) { Directory.CreateDirectory(savePath); }
        var safeName = (Path.GetFileNameWithoutExtension(fileName ?? "model").Replace("..", "").Replace("/", "").Replace("\\", "")) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".onnx";
        var filePath = Path.Combine(savePath, safeName);
        using (var f = File.Create(filePath)) { await onnx.CopyToAsync(f); }
        return await _manage.AddAsync(filePath, describe, type);
    }

    public Task<OperateResult> DeleteModelAsync(int index) => _manage.DeleteAsync(index, true);

    /// <summary>更新模型。</summary>
    public Task<OperateResult> UpdateModelAsync(int index, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType? type) => _manage.UpdateAsync(index, describe, type);

    /// <summary>本机推理（进程内 IdentityOperate，用法对齐 Api.Shared）。</summary>
    public async Task<OperateResult> RunLocalAsync(OnnxData model, byte[] image, string paramJson)
    {
        var provider = new CpuExecutionProvider(Path.Combine(model.path ?? "", model.name ?? ""));
        using var operate = IdentityOperate.Instance(new IdentityData
        {
            SN = $"{PublicHandler.DefaultSN}-local",
            Hardware = provider,
            IdentifyType = model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection,
        });
        IData data;
        switch (model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection)
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

    private static T FromJson<T>(string json) where T : new()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T(); }
        catch { return new T(); }
    }
}
