using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>与 YOLO 一样按用户保存进程内验证状态；刷新保留，应用重启后清空。</summary>
public sealed class AnomalibValidationState
{
    public const int MaximumFiles = 500;

    /// <summary>默认判定阈值：模型训练时标定的阈值在归一化分数上恰为 0.5，整图判定与区域框都以它为准。</summary>
    public const float DefaultThreshold = 0.5f;
    private readonly object _gate = new();
    private readonly Dictionary<string, AnomalibValidationSnapshot> _users = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>返回独立快照，页面修改不能污染其他页面正在使用的对象。</summary>
    public AnomalibValidationSnapshot Get(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            return _users.TryGetValue(owner, out var snapshot)
                ? Copy(snapshot) : new(string.Empty, null, [], [], DefaultThreshold, 4);
        }
    }

    /// <summary>保存当前用户状态，不持有文件流或模型会话。</summary>
    public void Save(string owner, AnomalibValidationSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        new AnomalibInferenceOptions { PixelThreshold = snapshot.PixelThreshold, MinimumArea = snapshot.MinimumArea }.Validate();
        if (snapshot.Files.Count > MaximumFiles) { throw new InvalidOperationException("验证文件数量超过 500 个限制。"); }
        lock (_gate) { _users[owner] = Copy(snapshot); }
    }

    private static AnomalibValidationSnapshot Copy(AnomalibValidationSnapshot snapshot) => snapshot with
    {
        Files = snapshot.Files.Select(file => file with
        {
            ResultMessageArguments = file.ResultMessageArguments.ToArray(),
            Output = CopyOutput(file.Output),
        }).ToArray(),
        Logs = snapshot.Logs.TakeLast(1000).Select(log => log with { Arguments = log.Arguments.ToArray() }).ToArray(),
    };

    internal static AnomalibInferenceOutput? CopyOutput(AnomalibInferenceOutput? output)
    {
        if (output is null) { return null; }
        var result = output.Result;
        return new AnomalibInferenceOutput
        {
            HeatmapDataUrl = output.HeatmapDataUrl,
            Result = new AnomalibImageResult
            {
                ImageScore = result.ImageScore,
                IsAnomalous = result.IsAnomalous,
                OriginalWidth = result.OriginalWidth,
                OriginalHeight = result.OriginalHeight,
                MapWidth = result.MapWidth,
                MapHeight = result.MapHeight,
                InferenceMilliseconds = result.InferenceMilliseconds,
                DecodeMilliseconds = result.DecodeMilliseconds,
                PreprocessMilliseconds = result.PreprocessMilliseconds,
                PostprocessMilliseconds = result.PostprocessMilliseconds,
                Regions = result.Regions.ToArray(),
            },
        };
    }
}
