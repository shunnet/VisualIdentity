using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练后自检：在训练集图片上按界面默认置信度复验，判断模型是不是"根本识别不到"。</summary>
public sealed class TrainSetSelfCheckTests
{
    [Fact]
    public void BuildValArguments_SelfCheckPinsSplitConfidenceAndOutputDirectory()
    {
        var options = new TrainingOptions { Task = "detect", Model = "yolo26n.pt", Epochs = 300, ImgSize = 640, Device = "0" };

        var args = YoloCommandBuilder.BuildValArguments("data.yaml", "best.pt", options, split: "train", conf: 0.25,
            projectDirectory: "/tmp/runs", name: "selfcheck");

        Assert.Contains("detect", args);
        Assert.Contains("val", args);
        Assert.Contains("data=data.yaml", args);
        Assert.Contains("model=best.pt", args);
        Assert.Contains("split=train", args);
        Assert.Contains("conf=0.25", args);
        Assert.Contains("project=/tmp/runs", args);
        Assert.Contains("name=selfcheck", args);
        Assert.Contains("exist_ok=True", args);
    }

    [Fact]
    public void BuildValArguments_WithoutSelfCheckOptions_KeepsPlainCommand()
    {
        var options = new TrainingOptions { Task = "detect", Model = "yolo26n.pt", Epochs = 300, ImgSize = 640, Device = "cpu" };

        var args = YoloCommandBuilder.BuildValArguments("data.yaml", "best.pt", options);

        Assert.DoesNotContain(args, a => a.StartsWith("split=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("conf=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("project=", StringComparison.Ordinal));
    }

    [Theory]
    // 实测（23 张图 / 9 类）：50 轮时训练集自检全是 0；300 轮时同一份数据 mAP50 才非 0。行首空格为 Ultralytics 原样输出
    [InlineData("                   all         21         21          0          0          0          0", 0d)]
    [InlineData("                   all         21         21      0.298      0.101     0.0985      0.052", 0.0985d)]
    public void ParseMetrics_ReadsTrainSetSelfCheckLine(string line, double expectedMap50)
    {
        var metrics = YoloOutputParser.ParseMetrics(line);

        Assert.NotNull(metrics);
        Assert.Equal(expectedMap50, metrics!.Map50 ?? 0d, 4);
    }

    [Fact]
    public void ParseMetrics_IgnoresPerClassRows_SoOnlyAllRowCounts()
    {
        // 自检输出里每个类别一行，只有 "all" 汇总行才是整体结论
        Assert.Null(YoloOutputParser.ParseMetrics("纸          2          2      0.298      0.101     0.0985      0.052"));
        Assert.NotNull(YoloOutputParser.ParseMetrics("all          2          2      0.298      0.101     0.0985      0.052"));
    }

    [Fact]
    public void DefaultEpochs_AreEnoughForSmallDatasets()
    {
        // 图片少的工程里 50 轮只有几十次参数更新，学不到东西（默认值必须够大，Ultralytics 会按 patience 早停）
        Assert.True(new TrainingOptions().Epochs >= DatasetHealthCheck.MinEpochsForSmallDataset,
            "默认训练轮数不应低于小数据集建议轮数：" + DatasetHealthCheck.MinEpochsForSmallDataset);
    }

    [Fact]
    public void DefaultUseVal_IsOff()
    {
        // 训练配置弹窗里的"使用验证集（自动划分 10%）"默认不勾选：
        // 小数据集再切掉 10% 会明显影响训练；需要客观指标时由用户主动勾选。
        Assert.False(new TrainingOptions().UseVal);
    }

    [Theory]
    // 验证集够大且 mAP 达标：跳过自检（大工程上这次复验很贵）
    [InlineData(0.9d, 40, false)]
    [InlineData(0.5d, 40, false)]
    // 指标偏低：即使验证集很大也要复验
    [InlineData(0.4d, 40, true)]
    // 验证集太小：mAP 不可信，无论多高都要复验
    [InlineData(0.99d, 4, true)]
    [InlineData(0.99d, 0, true)]
    public void NeedsTrainSetSelfCheck_DependsOnMapAndValidationSize(double map50, int valImages, bool expected)
    {
        var summary = new TrainingResultSummary(100, map50, map50 / 2, 0.8, 0.7);

        Assert.Equal(expected, TrainingResultsReader.NeedsTrainSetSelfCheck(summary, valImages));
    }

    [Fact]
    public void NeedsTrainSetSelfCheck_IsTrueWhenResultsAreMissing()
    {
        Assert.True(TrainingResultsReader.NeedsTrainSetSelfCheck(null, 40));
    }
}
