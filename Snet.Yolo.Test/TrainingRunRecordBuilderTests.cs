using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.History;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练记录构建：把状态、校准报告和 Python 档案整理成可留档的记录。</summary>
public sealed class TrainingRunRecordBuilderTests
{
    private static readonly DateTime Started = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Finished = Started.AddMinutes(12);

    /// <summary>YOLO：成功训练带出 best.pt 路径、results.csv 指标和数据集统计。</summary>
    [Fact]
    public void Yolo_CompleteRun_CapturesMetricsAndPaths()
    {
        var options = new TrainingOptions { Model = "yolo26n.pt", Task = "detect", Epochs = 100, ImgSize = 640, Device = "0", UseVal = true };
        var context = new YoloRunContext
        {
            RunId = "yolo-20261110080000-abc123", ProjectId = "p1", StartedAt = Started, Options = options,
            Stats = new DatasetStats(100, 250, ["a", "b"], [150, 100], [30, 40], 10, true),
        };
        var status = new TrainingStatus { Phase = TrainingPhase.Complete, GpuName = "RTX 4080", YoloVersion = "8.4.0", Epoch = 100, Message = "训练完成" };
        var series = new YoloResultsSeries { Epochs = 100, BestEpoch = 88, Map50 = 0.9, Map5095 = 0.7, Precision = 0.8, Recall = 0.85, Curve = [new TrainingCurvePoint { Epoch = 1, Score = 0.1 }] };

        var record = YoloRunRecordBuilder.Build(context, status, series, "runs/train", "runs/train/weights/best.pt", 5_000_000, Finished);

        Assert.Equal(TrainingRunStatus.Complete, record.Status);
        Assert.Equal("yolo26n.pt", record.Algorithm);
        Assert.Equal(90, record.TrainImages);
        Assert.Equal(10, record.ValidationImages);
        Assert.Equal(2, record.ClassCount);
        Assert.Equal(0.7, record.PrimaryScore);
        Assert.Equal("mAP50-95", record.PrimaryMetric);
        Assert.Equal(100, record.CompletedEpochs);
        Assert.Equal("RTX 4080", record.GpuName);
        Assert.Equal(88, record.Yolo!.BestEpoch);
        Assert.Equal(250, record.Yolo.Instances);
        Assert.True(record.HasModel);
        Assert.Equal(720, record.DurationSeconds);
    }

    /// <summary>YOLO：失败训练没有模型也能留档，原因进入 Message。</summary>
    [Fact]
    public void Yolo_FailedRun_KeepsErrorWithoutModel()
    {
        var context = new YoloRunContext { RunId = "yolo-x", ProjectId = "p1", StartedAt = Started, Options = new TrainingOptions() };
        var status = new TrainingStatus { Phase = TrainingPhase.Failed, LastError = "CUDA out of memory", Message = "训练失败" };

        var record = YoloRunRecordBuilder.Build(context, status, null, string.Empty, string.Empty, 0, Finished);

        Assert.Equal(TrainingRunStatus.Failed, record.Status);
        Assert.Equal("CUDA out of memory", record.Message);
        Assert.False(record.HasModel);
        Assert.Null(record.PrimaryScore);
    }

    /// <summary>运行标识按时间排序且互不相同。</summary>
    [Fact]
    public void NewRunId_IsSortableAndUnique()
    {
        var first = YoloRunRecordBuilder.NewRunId(Started);
        var second = YoloRunRecordBuilder.NewRunId(Started);
        Assert.StartsWith("yolo-20261110080000-", first);
        Assert.NotEqual(first, second);
        Assert.True(string.CompareOrdinal(first, YoloRunRecordBuilder.NewRunId(Started.AddSeconds(1))) < 0);
    }

    /// <summary>构造 Anomalib 请求。</summary>
    private static AnomalibTrainingRequest Request(AnomalibModelKind model)
        => new()
        {
            Owner = "snet", ProjectId = "p1", WorkingDirectory = "w", PythonExecutable = "py", Images = ["a"],
            Options = new AnomalibTrainingOptions { Model = model, ImageSize = 640, MaxEpochs = 300, Device = "cuda:0" },
        };

    /// <summary>构造通过门禁的校准报告：15 张校准图全部逐图通过。</summary>
    private static AnomalibParityResult Parity()
    {
        var rows = string.Join(",", Enumerable.Range(1, 15).Select(index =>
            $$"""{"index":{{index}},"fileName":"n{{index}}.bmp","passed":true,"torchScore":0.1,"onnxScore":0.1,"torchLabel":false,"onnxLabel":false,"scoreDifference":0.0,"maskIou":1.0,"imageThreshold":0.5,"message":""}"""));
        return AnomalibParityResult.Parse($$$"""
            {"status":"passed","exportSucceeded":true,"totalSampleCount":15,"sampleCount":15,"normalSampleCount":15,"normalFalsePositiveCount":0,
             "maxScoreDifference":0.0001,"minimumMaskIou":1.0,"labelMismatches":0,"errors":[],
             "sampleResults":[{{{rows}}}],
             "normalCalibration":{"completed":true,"sampleCount":2,"quantile":1.0,"thresholdPolicy":"max x 1.05 / mean + 3.5 sigma","previousImageThreshold":1.0,"previousPixelThreshold":1.0,
               "imageThreshold":57.7,"pixelThreshold":57.1,"imageScoreMax":50.0,"imageScoreMean":45.0,"imageScoreStd":7.07,
               "samples":[{"fileName":"a","rawImageScore":40.0,"rawPixelMaximum":39.0},{"fileName":"b","rawImageScore":50.0,"rawPixelMaximum":49.0}],"errors":[]}}
            """);
    }

    /// <summary>Anomalib：成功训练合并校准报告、阈值、显存与 Python 档案。</summary>
    [Fact]
    public void Anomalib_CompleteRun_MergesParityAndMetrics()
    {
        var status = new AnomalibTrainingStatus { Phase = AnomalibTrainingPhase.Complete, StartedAt = Started, TrainingImageCount = 108, CalibrationImageCount = 15, Message = "完成" };
        var metrics = new AnomalibRunMetricsFile
        {
            GpuName = "RTX 4080", PeakVramMiB = 8195, CompletedEpochs = 120, BestEpoch = 87, BestPixelAuroc = 0.91,
            TimingsSeconds = new() { ["train"] = 60.5 },
            Epochs = [new AnomalibRunMetricsEpoch { Epoch = 1, TrainLoss = 3, PixelAuroc = 0.6 }, new AnomalibRunMetricsEpoch { Epoch = 2, TrainLoss = 2, PixelAuroc = 0.7 }],
            PatchcorePlan = new AnomalibRunMetricsPlan { MemoryBank = 6912, KeepFraction = 1.0 },
        };

        var record = AnomalibRunRecordBuilder.Build(Request(AnomalibModelKind.EfficientAdSmall), "anomalib-run-1", status, Parity(), metrics, Finished, 12345);

        Assert.Equal(TrainingRunStatus.Complete, record.Status);
        Assert.Equal("EfficientAD Small", record.Algorithm);
        Assert.Equal(300, record.Epochs);
        Assert.Equal(120, record.CompletedEpochs);
        Assert.Equal("anomalib-run-1/artifacts/model.onnx", record.ModelPath);
        Assert.Equal(1.0, record.PrimaryScore);
        var details = record.Anomalib!;
        Assert.True(details.GatePassed);
        Assert.True(details.ExportSucceeded);
        Assert.Equal(15, details.TotalSamples);
        Assert.Equal(0, details.FalsePositives);
        Assert.True(details.GateEvaluated);
        Assert.Equal(0.0001, details.MaxScoreDifference);
        Assert.Equal(1.0, details.MinimumMaskIou);
        Assert.Equal(57.7, details.ImageThreshold);
        Assert.Equal(50.0, details.NormalScoreMax);
        Assert.Equal(45.0, details.NormalScoreMean);
        Assert.Equal(8195, details.PeakVramMiB);
        Assert.Equal(87, details.BestEpoch);
        Assert.Equal([1, 2], details.Curve.Select(point => point.Epoch));
        Assert.Equal(60.5, details.StageSeconds["train"]);
    }

    /// <summary>Anomalib：PaDiM 与 PatchCore 是单轮拟合；没有模型时不带模型路径。</summary>
    [Fact]
    public void Anomalib_SingleFitModel_UsesOneEpoch()
    {
        var status = new AnomalibTrainingStatus { Phase = AnomalibTrainingPhase.Failed, LastError = "显存不足" };
        var record = AnomalibRunRecordBuilder.Build(Request(AnomalibModelKind.PatchCore), "anomalib-run-2", status, null, null, Finished, 0);

        Assert.Equal(TrainingRunStatus.Failed, record.Status);
        Assert.Equal(1, record.Epochs);
        Assert.Equal(0, record.CompletedEpochs);
        Assert.Equal("显存不足", record.Message);
        Assert.False(record.HasModel);
    }

    /// <summary>
    /// Anomalib：流水线异常（如显存不足）时，Python 写出的失败报告用 1.0 占位分数差且没有任何样本；
    /// 记录里不能留下这个占位值，也不能把这次运行当成执行过校准门禁。
    /// </summary>
    [Fact]
    public void Anomalib_PipelineCrashReport_DoesNotLeakPlaceholderCalibration()
    {
        var crash = AnomalibParityResult.Parse("""{"status":"failed","sampleCount":0,"normalSampleCount":0,"normalFalsePositiveCount":0,"maxScoreDifference":1.0,"minimumMaskIou":0.0,"labelMismatches":0,"errors":["CUDA out of memory"]}""");
        var status = new AnomalibTrainingStatus { Phase = AnomalibTrainingPhase.Failed, StartedAt = Started, LastError = "CUDA out of memory" };

        var record = AnomalibRunRecordBuilder.Build(Request(AnomalibModelKind.EfficientAdSmall), "anomalib-run-3", status, crash, null, Finished, 0);

        var details = record.Anomalib!;
        Assert.Null(details.MaxScoreDifference);
        Assert.Null(details.MinimumMaskIou);
        Assert.False(details.GatePassed);
        Assert.False(details.GateEvaluated);
    }

    /// <summary>校准门禁算执行过：成功导出，或至少有一张校准图参与比较；旧记录里残留的占位分数差不影响判断。</summary>
    [Fact]
    public void AnomalibRunDetails_GateEvaluated_RequiresExportOrComparedSamples()
    {
        Assert.False(new AnomalibRunDetails().GateEvaluated);
        Assert.False(new AnomalibRunDetails { MaxScoreDifference = 1.0 }.GateEvaluated);
        Assert.True(new AnomalibRunDetails { ExportSucceeded = true }.GateEvaluated);
        Assert.True(new AnomalibRunDetails { TotalSamples = 15 }.GateEvaluated);
    }

    /// <summary>Anomalib：校准未通过映射为独立状态；进入流水线之前失败的运行也能留档。</summary>
    [Fact]
    public void Anomalib_StatusMappingAndEarlyStop()
    {
        Assert.Equal(TrainingRunStatus.CalibrationFailed, AnomalibRunRecordBuilder.MapStatus(AnomalibTrainingPhase.CalibrationFailed));
        Assert.Equal(TrainingRunStatus.Cancelled, AnomalibRunRecordBuilder.MapStatus(AnomalibTrainingPhase.Cancelled));
        Assert.Equal(TrainingRunStatus.Failed, AnomalibRunRecordBuilder.MapStatus(AnomalibTrainingPhase.Training));

        var early = AnomalibRunRecordBuilder.BuildEarlyStop("snet", "p1", Request(AnomalibModelKind.Padim).Options,
            new AnomalibTrainingStatus { Phase = AnomalibTrainingPhase.Failed, LastError = "未找到 Python" }, Started, Finished);
        Assert.StartsWith("anomalib-run-", early.Id);
        Assert.Equal(TrainingRunStatus.Failed, early.Status);
        Assert.Equal("未找到 Python", early.Message);
        Assert.Null(early.Anomalib);
    }

    /// <summary>训练记录能序列化后再读回（枚举为字符串），这是存储格式的保证。</summary>
    [Fact]
    public void Record_SerializesAsWebJson()
    {
        var context = new YoloRunContext { RunId = "yolo-x", ProjectId = "p1", StartedAt = Started, Options = new TrainingOptions() };
        var record = YoloRunRecordBuilder.Build(context, new TrainingStatus { Phase = TrainingPhase.Cancelled }, null, string.Empty, string.Empty, 0, Finished);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var json = JsonSerializer.Serialize(record, options);
        Assert.Contains("\"status\":\"Cancelled\"", json);
        Assert.Equal(TrainingRunStatus.Cancelled, JsonSerializer.Deserialize<TrainingRunRecord>(json, options)!.Status);
    }
}
