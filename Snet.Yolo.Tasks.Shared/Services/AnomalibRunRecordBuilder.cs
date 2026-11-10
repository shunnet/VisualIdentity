namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;

/// <summary>把一次 Anomalib 训练的状态、校准报告和 Python 档案整理成训练记录；纯函数，不访问磁盘。</summary>
public static class AnomalibRunRecordBuilder
{
    /// <summary>训练记录里 PaDiM 与 PatchCore 的轮数：它们是单轮拟合。</summary>
    private const int SingleFitEpochs = 1;

    /// <summary>整理一次已进入训练流水线的运行；成功、校准未通过、失败和取消都会产生记录。</summary>
    /// <param name="request">训练请求，提供所有者、工程与选项。</param>
    /// <param name="runId">运行标识，即运行目录名。</param>
    /// <param name="status">训练结束时的状态快照。</param>
    /// <param name="parity">一致性与校准结果；流水线未写出时为空。</param>
    /// <param name="metrics">Python 写出的档案；缺失时为空。</param>
    /// <param name="finishedAt">结束时间（UTC）。</param>
    /// <param name="modelBytes">保留下来的 ONNX 文件大小；没有模型时为 0。</param>
    /// <returns>可保存的训练记录。</returns>
    public static TrainingRunRecord Build(AnomalibTrainingRequest request, string runId, AnomalibTrainingStatus status,
        AnomalibParityResult? parity, AnomalibRunMetricsFile? metrics, DateTime finishedAt, long modelBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(status);
        var options = request.Options;
        var runStatus = MapStatus(status.Phase);
        var details = BuildDetails(status, parity, metrics, registered: status.Phase == AnomalibTrainingPhase.Complete);
        var iterative = options.Model == AnomalibModelKind.EfficientAdSmall;
        var completedEpochs = iterative ? metrics?.CompletedEpochs ?? status.Live.Epoch : modelBytes > 0 ? SingleFitEpochs : 0;
        return new TrainingRunRecord
        {
            Id = runId,
            ProjectId = request.ProjectId,
            Kind = ProjectKind.Anomalib,
            Status = runStatus,
            StartedAt = status.StartedAt ?? finishedAt,
            FinishedAt = finishedAt,
            Algorithm = AnomalibModelCatalog.Get(options.Model).DisplayName,
            Task = "anomaly",
            ImageSize = options.ImageSize,
            Epochs = iterative ? options.MaxEpochs : SingleFitEpochs,
            CompletedEpochs = completedEpochs,
            Device = options.Device,
            GpuName = metrics?.GpuName ?? string.Empty,
            Message = runStatus == TrainingRunStatus.Complete ? status.Message : status.LastError ?? status.Message,
            TrainImages = status.TrainingImageCount,
            ValidationImages = status.CalibrationImageCount,
            PrimaryMetric = "passRate",
            PrimaryScore = details.PassRate,
            Anomalib = details,
            RunDirectory = runId,
            ModelPath = modelBytes > 0 ? runId + "/artifacts/model.onnx" : string.Empty,
            ModelBytes = modelBytes,
        };
    }

    /// <summary>整理在进入训练流水线之前就失败或被取消的运行（环境准备失败等），不含模型与校准数据。</summary>
    /// <param name="owner">工程所有者，仅用于校验调用方传入的非空。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="options">用户提交的训练选项。</param>
    /// <param name="status">结束时的状态快照。</param>
    /// <param name="startedAt">开始时间（UTC）。</param>
    /// <param name="finishedAt">结束时间（UTC）。</param>
    /// <returns>可保存的训练记录；标识使用新的 <c>anomalib-run-</c> GUID。</returns>
    public static TrainingRunRecord BuildEarlyStop(string owner, string projectId, AnomalibTrainingOptions options,
        AnomalibTrainingStatus status, DateTime startedAt, DateTime finishedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(status);
        var runStatus = status.Phase == AnomalibTrainingPhase.Cancelled ? TrainingRunStatus.Cancelled : TrainingRunStatus.Failed;
        var iterative = options.Model == AnomalibModelKind.EfficientAdSmall;
        return new TrainingRunRecord
        {
            Id = "anomalib-run-" + Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            Kind = ProjectKind.Anomalib,
            Status = runStatus,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Algorithm = AnomalibModelCatalog.Get(options.Model).DisplayName,
            Task = "anomaly",
            ImageSize = options.ImageSize,
            Epochs = iterative ? options.MaxEpochs : SingleFitEpochs,
            Device = options.Device,
            Message = status.LastError ?? status.Message,
            TrainImages = status.TrainingImageCount,
            ValidationImages = status.CalibrationImageCount,
            PrimaryMetric = "passRate",
        };
    }

    /// <summary>训练阶段映射为记录的最终状态；仍在进行中的阶段按失败处理（异常退出）。</summary>
    internal static TrainingRunStatus MapStatus(AnomalibTrainingPhase phase) => phase switch
    {
        AnomalibTrainingPhase.Complete => TrainingRunStatus.Complete,
        AnomalibTrainingPhase.CalibrationFailed => TrainingRunStatus.CalibrationFailed,
        AnomalibTrainingPhase.Cancelled => TrainingRunStatus.Cancelled,
        _ => TrainingRunStatus.Failed,
    };

    /// <summary>合并校准报告、阈值标定和 Python 档案中的指标。</summary>
    private static AnomalibRunDetails BuildDetails(AnomalibTrainingStatus status, AnomalibParityResult? parity,
        AnomalibRunMetricsFile? metrics, bool registered)
    {
        var normal = parity?.NormalCalibration;
        var scores = normal?.Samples.Select(static sample => sample.RawImageScore).ToArray() ?? [];
        var plan = metrics?.PatchcorePlan;
        return new AnomalibRunDetails
        {
            TrainingImages = status.TrainingImageCount,
            CalibrationImages = status.CalibrationImageCount,
            GatePassed = parity?.Passed == true,
            ExportSucceeded = parity?.ExportSucceeded == true,
            Registered = registered,
            TotalSamples = parity?.TotalSampleCount ?? 0,
            PassedSamples = parity?.PassedSampleCount ?? 0,
            NormalSamples = parity?.NormalSampleCount ?? 0,
            FalsePositives = parity?.NormalFalsePositiveCount ?? 0,
            MaxScoreDifference = parity is { MaxScoreDifference: var difference } && double.IsFinite(difference) ? difference : null,
            MinimumMaskIou = parity is { SampleCount: > 0, MinimumMaskIou: var iou } ? iou : null,
            LabelMismatches = parity?.LabelMismatches ?? 0,
            ImageThreshold = normal?.ImageThreshold,
            NormalScoreMax = scores.Length > 0 ? scores.Max() : status.Live.NormalScoreMax,
            NormalScoreMean = scores.Length > 0 ? scores.Average() : null,
            ThresholdSamples = normal?.SampleCount ?? 0,
            CompletedEpochs = metrics?.CompletedEpochs ?? status.Live.Epoch,
            BestEpoch = metrics?.BestEpoch ?? status.Live.BestEpoch,
            BestPixelAuroc = metrics?.BestPixelAuroc ?? status.Live.BestPixelAuroc,
            PeakVramMiB = metrics?.PeakVramMiB,
            MemoryBank = plan?.MemoryBank ?? status.Live.MemoryBank,
            KeepFraction = plan?.KeepFraction ?? status.Live.KeepFraction,
            StageSeconds = metrics?.TimingsSeconds ?? [],
            Curve = BuildCurve(status, metrics),
        };
    }

    /// <summary>优先使用 Python 档案里的完整曲线，缺失时退回状态里的实时曲线。</summary>
    private static List<TrainingCurvePoint> BuildCurve(AnomalibTrainingStatus status, AnomalibRunMetricsFile? metrics)
        => metrics is { Epochs.Count: > 0 }
            ? metrics.Epochs.Select(static point => new TrainingCurvePoint { Epoch = point.Epoch, Score = point.PixelAuroc, Loss = point.TrainLoss }).ToList()
            : status.Live.History.Select(static point => new TrainingCurvePoint { Epoch = point.Epoch, Score = point.PixelAuroc, Loss = point.TrainLoss }).ToList();
}
