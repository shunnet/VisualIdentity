namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>把一次 YOLO 训练的上下文、最终状态与 results.csv 整理成训练记录；纯函数，不访问磁盘。</summary>
public static class YoloRunRecordBuilder
{
    /// <summary>生成 YOLO 训练运行标识：<c>yolo-&lt;UTC 时间戳&gt;-&lt;6 位随机串&gt;</c>，按字典序即按时间排序。</summary>
    /// <param name="startedAt">训练开始时间（UTC）。</param>
    /// <returns>可直接作为文件名的标识。</returns>
    public static string NewRunId(DateTime startedAt)
        => "yolo-" + startedAt.ToUniversalTime().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..6];

    /// <summary>整理训练记录。</summary>
    /// <param name="context">流水线积累的上下文。</param>
    /// <param name="status">训练结束时的状态快照。</param>
    /// <param name="series">解析后的 results.csv；没有时为空。</param>
    /// <param name="runDirectory">Ultralytics 输出目录相对于工程训练目录的路径；不在工程目录内时为空。</param>
    /// <param name="modelPath">模型文件相对于工程训练目录的路径；没有模型时为空。</param>
    /// <param name="modelBytes">模型文件大小。</param>
    /// <param name="finishedAt">结束时间（UTC）。</param>
    /// <returns>可保存的训练记录。</returns>
    public static TrainingRunRecord Build(YoloRunContext context, TrainingStatus status, YoloResultsSeries? series,
        string runDirectory, string modelPath, long modelBytes, DateTime finishedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(status);
        var options = context.Options;
        var stats = context.Stats;
        var runStatus = status.Phase switch
        {
            TrainingPhase.Complete => TrainingRunStatus.Complete,
            TrainingPhase.Cancelled => TrainingRunStatus.Cancelled,
            _ => TrainingRunStatus.Failed,
        };
        return new TrainingRunRecord
        {
            Id = context.RunId,
            ProjectId = context.ProjectId,
            Kind = ProjectKind.Yolo,
            Status = runStatus,
            StartedAt = context.StartedAt,
            FinishedAt = finishedAt,
            Algorithm = options.Model,
            Task = options.Task,
            ImageSize = options.ImgSize,
            Epochs = options.Epochs,
            CompletedEpochs = series?.Epochs ?? status.Epoch,
            Device = options.Device,
            GpuName = status.GpuName,
            Message = runStatus == TrainingRunStatus.Complete ? status.Message : status.LastError ?? status.Message,
            TrainImages = stats is null ? 0 : Math.Max(0, stats.ImageCount - stats.ValImageCount),
            ValidationImages = stats?.ValImageCount ?? 0,
            ClassCount = stats?.ClassNames.Count ?? 0,
            PrimaryScore = series?.PrimaryScore,
            PrimaryMetric = series?.PrimaryMetric ?? string.Empty,
            RunDirectory = runDirectory,
            ModelPath = modelPath,
            ModelBytes = modelBytes,
            Yolo = new YoloRunDetails
            {
                ModelName = options.Model,
                YoloVersion = status.YoloVersion,
                UsedValidationSet = options.UseVal,
                Instances = stats?.BoxCount ?? 0,
                BestEpoch = series?.BestEpoch ?? 0,
                Map50 = series?.Map50,
                Map5095 = series?.Map5095,
                Precision = series?.Precision,
                Recall = series?.Recall,
                Accuracy = series?.Accuracy,
                BoxLoss = series?.BoxLoss,
                ClsLoss = series?.ClsLoss,
                DflLoss = series?.DflLoss,
                Curve = series is null ? [] : [.. series.Curve],
            },
        };
    }
}
