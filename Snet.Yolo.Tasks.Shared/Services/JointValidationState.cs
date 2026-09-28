using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证页面可恢复的用户状态，不包含运行中的任务或模型会话。</summary>
public sealed record JointValidationSnapshot(
    string AnomalibModelKey, string YoloModelIndex,
    string? ImagePath, string? ImageUrl, string? ImageName, int Width, int Height,
    JointValidationOutput? Output, string? Error, IReadOnlyList<string> Logs,
    float PixelThreshold, int MinimumArea, IReadOnlyDictionary<string, double> YoloParameters);

/// <summary>按用户保存联合验证状态；页面刷新保留，应用重启清空。</summary>
public sealed class JointValidationState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JointValidationSnapshot> _users = new(StringComparer.OrdinalIgnoreCase);

    public JointValidationSnapshot Get(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            return _users.TryGetValue(owner, out var snapshot)
                ? Copy(snapshot) : new(string.Empty, string.Empty, null, null, null, 0, 0, null, null, [], 0.8f, 4, new Dictionary<string, double>());
        }
    }

    public void Save(string owner, JointValidationSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        new AnomalibInferenceOptions { PixelThreshold = snapshot.PixelThreshold, MinimumArea = snapshot.MinimumArea }.Validate();
        lock (_gate) { _users[owner] = Copy(snapshot); }
    }

    private static JointValidationSnapshot Copy(JointValidationSnapshot snapshot) => snapshot with
    {
        Logs = snapshot.Logs.TakeLast(1000).ToArray(),
        YoloParameters = snapshot.YoloParameters.ToDictionary(pair => pair.Key, pair => pair.Value),
        Output = snapshot.Output is not { } output ? null : output with
        {
            Anomalib = AnomalibValidationState.CopyOutput(output.Anomalib),
            Matches = new JointMatchResult(
                output.Matches.Regions.Select(region => region with { Detections = region.Detections.ToArray() }).ToArray(),
                output.Matches.UnmatchedDetections.ToArray()),
        },
    };
}
