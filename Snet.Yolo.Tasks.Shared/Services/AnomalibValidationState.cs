using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>与 YOLO 一样按用户和模型保存进程内验证状态；刷新保留，应用重启后清空。</summary>
public sealed class AnomalibValidationState
{
    /// <summary>每个模型最多保留的验证文件数量。</summary>
    public const int MaximumFiles = 500;

    /// <summary>默认判定阈值：模型训练时标定的阈值在归一化分数上恰为 0.5，整图判定与区域框都以它为准。</summary>
    public const float DefaultThreshold = 0.5f;
    private readonly object _gate = new();
    /// <summary>按用户、模型选择键分别保存文件和识别状态。</summary>
    private readonly Dictionary<string, Dictionary<string, AnomalibValidationSnapshot>> _users = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>各用户最后选中的模型，供页面刷新恢复。</summary>
    private readonly Dictionary<string, string> _selectedModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>返回独立快照，页面修改不能污染其他页面正在使用的对象。</summary>
    public AnomalibValidationSnapshot Get(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            return Copy(ReadModel(owner, _selectedModels.GetValueOrDefault(owner, string.Empty)));
        }
    }

    /// <summary>获取指定用户、指定模型的独立快照；新模型使用空文件队列。</summary>
    /// <param name="owner">经过认证的用户名称。</param>
    /// <param name="modelKey">由工程和训练运行组成的模型选择键。</param>
    public AnomalibValidationSnapshot GetModel(string owner, string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(modelKey);
        lock (_gate) { return Copy(ReadModel(owner, modelKey)); }
    }

    /// <summary>保存当前模型状态并记录模型选择，不覆盖其他模型的文件与结果。</summary>
    public void Save(string owner, AnomalibValidationSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        new AnomalibInferenceOptions { PixelThreshold = snapshot.PixelThreshold, MinimumArea = snapshot.MinimumArea }.Validate();
        if (snapshot.Files.Count > MaximumFiles) { throw new InvalidOperationException("每个模型的验证文件数量不能超过 500 个。"); }
        lock (_gate)
        {
            if (!_users.TryGetValue(owner, out var models)) { _users[owner] = models = new(StringComparer.Ordinal); }
            models[snapshot.SelectedModelKey] = Copy(snapshot);
            _selectedModels[owner] = snapshot.SelectedModelKey;
        }
    }

    /// <summary>移除已删除模型的页面状态，保留同一用户其他模型的状态。</summary>
    /// <param name="owner">经过认证的用户名称。</param>
    /// <param name="modelKey">已删除的模型选择键。</param>
    public void RemoveModel(string owner, string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(modelKey);
        lock (_gate)
        {
            if (_users.TryGetValue(owner, out var models)) { models.Remove(modelKey); }
            if (_selectedModels.GetValueOrDefault(owner) == modelKey) { _selectedModels.Remove(owner); }
        }
    }

    /// <summary>仅在持有状态锁时读取模型；未知模型不继承当前模型的图片。</summary>
    private AnomalibValidationSnapshot ReadModel(string owner, string modelKey)
        => _users.TryGetValue(owner, out var models) && models.TryGetValue(modelKey, out var snapshot)
            ? snapshot : new(modelKey, null, [], [], DefaultThreshold, 4);

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
