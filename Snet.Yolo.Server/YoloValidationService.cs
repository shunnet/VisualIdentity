using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server;

/// <summary>使用 Server 的模型管理与推理内核执行 YOLO 验证。</summary>
public sealed class YoloValidationService(ManageOperate models, Func<string, IExecutionProvider> createProvider, Action<string>? normalizeMetadata = null)
{
    private readonly ConcurrentDictionary<string, byte> _normalizedModels = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>重新按用户查询模型后运行目标检测或实例分割。</summary>
    public Task<OperateResult> IdentifyAsync(string owner, int modelIndex, byte[] image, double confidence = 0.25, double iou = 0.45, double pixelConfidence = 0.65, CancellationToken cancellationToken = default)
        => IdentifyAsync(owner, modelIndex, image, JsonSerializer.Serialize(new { Confidence = confidence, Iou = iou, PixelConfidence = pixelConfidence }), cancellationToken);

    /// <summary>按模型类型执行验证页的全部 YOLO 推理模式。</summary>
    public async Task<OperateResult> IdentifyAsync(string owner, int modelIndex, byte[] image, string parametersJson, CancellationToken cancellationToken = default)
    {
        var model = (await models.ListAvailableByOwnerAsync(owner, cancellationToken)).FirstOrDefault(item => item.index == modelIndex);
        if (model is null) { return OperateResult.CreateFailureResult("模型不存在或无权访问。"); }
        var path = Path.Combine(model.path ?? "", model.name ?? "");
        if (_normalizedModels.TryAdd(path, 0)) { normalizeMetadata?.Invoke(path); }
        var type = model.onnxType ?? OnnxType.ObjectDetection;
        using var operate = new IdentityOperate(new IdentityData
        {
            SN = PublicHandler.DefaultSN + "-validation",
            Hardware = createProvider(path),
            IdentifyType = type,
        });
        return type switch
        {
            OnnxType.Segmentation => await operate.RunAsync(SetImage<SegmentationData>(parametersJson, image), cancellationToken),
            OnnxType.Classification => await operate.RunAsync(SetImage<ClassificationData>(parametersJson, image), cancellationToken),
            OnnxType.ObbDetection => await operate.RunAsync(SetImage<ObbDetectionData>(parametersJson, image), cancellationToken),
            OnnxType.PoseEstimation => await operate.RunAsync(SetImage<PoseEstimationData>(parametersJson, image), cancellationToken),
            _ => await operate.RunAsync(SetImage<ObjectDetectionData>(parametersJson, image), cancellationToken),
        };
    }

    private static T SetImage<T>(string json, byte[] image) where T : class, Snet.Yolo.Server.@interface.IData, new()
    {
        var input = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        switch (input)
        {
            case ObjectDetectionData data: data.File = image; break;
            case SegmentationData data: data.File = image; break;
            case ClassificationData data: data.File = image; break;
            case ObbDetectionData data: data.File = image; break;
            case PoseEstimationData data: data.File = image; break;
        }
        return input;
    }
}
