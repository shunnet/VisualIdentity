using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证页面可恢复的用户状态，不包含运行中的任务或模型会话。</summary>
public sealed record JointValidationSnapshot(
    string AnomalibModelKey, string YoloModelIndex,
    IReadOnlyList<JointValidationImage> Images, string? SelectedImageId,
    JointValidationMode Mode, IReadOnlyList<string> Logs,
    float PixelThreshold, int MinimumArea, IReadOnlyDictionary<string, double> YoloParameters);

/// <summary>联合验证图片及其独立识别结果；原图用于推理，缩略图仅用于列表展示。</summary>
public sealed record JointValidationImage(
    string Id, string Path, string Url, string Name, int Width, int Height,
    string PreviewUrl, JointValidationOutput? Output = null, string? Error = null);

/// <summary>按用户保存联合验证状态；页面刷新保留，应用重启清空。</summary>
public sealed class JointValidationState
{
    /// <summary>每位用户最多保留的图片数量，避免无限累积验证状态。</summary>
    public const int MaximumImages = 500;
    private readonly object _gate = new();
    private readonly Dictionary<string, JointValidationSnapshot> _users = new(StringComparer.OrdinalIgnoreCase);

    public JointValidationSnapshot Get(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            return _users.TryGetValue(owner, out var snapshot)
                ? Copy(snapshot) : new(string.Empty, string.Empty, [], null, JointValidationMode.Joint, [], 0.8f, 4, new Dictionary<string, double>());
        }
    }

    public void Save(string owner, JointValidationSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (snapshot.Images.Count > MaximumImages) { throw new ArgumentException("联合验证图片数量超过上限。", nameof(snapshot)); }
        new AnomalibInferenceOptions { PixelThreshold = snapshot.PixelThreshold, MinimumArea = snapshot.MinimumArea }.Validate();
        lock (_gate) { _users[owner] = Copy(snapshot); }
    }

    private static JointValidationSnapshot Copy(JointValidationSnapshot snapshot) => snapshot with
    {
        Logs = snapshot.Logs.TakeLast(1000).ToArray(),
        YoloParameters = snapshot.YoloParameters.ToDictionary(pair => pair.Key, pair => pair.Value),
        Images = snapshot.Images.Select(image => image with { Output = CopyOutput(image.Output) }).ToArray(),
    };

    private static JointValidationOutput? CopyOutput(JointValidationOutput? output) => output is null ? null : output with
    {
        Anomalib = AnomalibValidationState.CopyOutput(output.Anomalib),
        Matches = new JointMatchResult(
            output.Matches.Regions.Select(region => region with { Detections = region.Detections.ToArray() }).ToArray(),
            output.Matches.UnmatchedDetections.ToArray()),
    };
}
