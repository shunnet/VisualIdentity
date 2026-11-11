using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>YOLO 训练页实时折线图与最佳轮次的数据：每个轮次一个点，来自该轮的验证指标与训练损失。</summary>
public sealed class TrainingStatusCurveTests
{
    /// <summary>构造一个已经收到某轮验证指标的状态。</summary>
    private static TrainingStatus Status(int epoch, double? map5095, double? box = 1.0, double? cls = 2.0, double? dfl = 0.5)
        => new()
        {
            Epoch = epoch,
            Metrics = new TrainingMetrics { Map50 = 0.8, Map5095 = map5095, BoxLoss = box, ClsLoss = cls, DflLoss = dfl },
        };

    /// <summary>每个轮次记录一个点：分数为 mAP50-95，损失为三项训练损失之和。</summary>
    [Fact]
    public void RecordCurvePoint_AddsOnePointPerEpoch()
    {
        var status = Status(1, 0.30);
        status.RecordCurvePoint();
        status.Epoch = 2;
        status.Metrics.Map5095 = 0.45;
        status.RecordCurvePoint();

        Assert.Equal([1, 2], status.Curve.Select(point => point.Epoch));
        Assert.Equal([0.30, 0.45], status.Curve.Select(point => point.Score));
        Assert.Equal(3.5, status.Curve[0].Loss);
    }

    /// <summary>同一轮次再次出现（训练结束后的最终验证）时覆盖该点，不重复。</summary>
    [Fact]
    public void RecordCurvePoint_ReplacesThePointOfTheSameEpoch()
    {
        var status = Status(3, 0.40);
        status.RecordCurvePoint();
        status.Metrics.Map5095 = 0.42;
        status.RecordCurvePoint();

        var point = Assert.Single(status.Curve);
        Assert.Equal(0.42, point.Score);
    }

    /// <summary>没有 mAP50-95 时退而取 mAP50；两者都没有，或还没有轮次时不记录。</summary>
    [Fact]
    public void RecordCurvePoint_FallsBackToMap50AndSkipsIncompleteData()
    {
        var withoutMap5095 = Status(1, null);
        withoutMap5095.RecordCurvePoint();
        Assert.Equal(0.8, Assert.Single(withoutMap5095.Curve).Score);

        var withoutAnyMap = new TrainingStatus { Epoch = 1, Metrics = new TrainingMetrics { BoxLoss = 1.0 } };
        withoutAnyMap.RecordCurvePoint();
        Assert.Empty(withoutAnyMap.Curve);

        var beforeFirstEpoch = Status(0, 0.5);
        beforeFirstEpoch.RecordCurvePoint();
        Assert.Empty(beforeFirstEpoch.Curve);
    }

    /// <summary>超过上限后丢弃最早的点，保留最近的轮次。</summary>
    [Fact]
    public void RecordCurvePoint_KeepsOnlyTheMostRecentPoints()
    {
        var status = Status(1, 0.1);
        for (var epoch = 1; epoch <= TrainingStatus.MaximumCurvePoints + 25; epoch++)
        {
            status.Epoch = epoch;
            status.RecordCurvePoint();
        }

        Assert.Equal(TrainingStatus.MaximumCurvePoints, status.Curve.Count);
        Assert.Equal(26, status.Curve[0].Epoch);
        Assert.Equal(TrainingStatus.MaximumCurvePoints + 25, status.Curve[^1].Epoch);
    }

    /// <summary>快照里的曲线是独立副本：训练线程继续追加点不会改动已经推送给页面的数据。</summary>
    [Fact]
    public void Clone_CopiesTheCurveIndependently()
    {
        var status = Status(1, 0.3);
        status.RecordCurvePoint();

        var snapshot = status.Clone(includeLogs: false);
        status.Epoch = 2;
        status.RecordCurvePoint();

        Assert.Single(snapshot.Curve);
        Assert.Equal(2, status.Curve.Count);
    }

    /// <summary>最佳轮次是验证分数最高的那一轮；分数相同时保留最早达到的轮次，还没有记录时为 0 与空。</summary>
    [Fact]
    public void RecordCurvePoint_TracksTheBestEpoch()
    {
        var status = Status(1, 0.30);
        Assert.Equal(0, status.BestEpoch);
        Assert.Null(status.BestScore);

        foreach (var (epoch, score) in new[] { (1, 0.30), (2, 0.50), (3, 0.45), (4, 0.50) })
        {
            status.Epoch = epoch;
            status.Metrics.Map5095 = score;
            status.RecordCurvePoint();
        }

        Assert.Equal(2, status.BestEpoch);
        Assert.Equal(0.50, status.BestScore);
    }

    /// <summary>训练结束后对 best.pt 的最终验证与最后一个轮次同号：只覆盖曲线上的点，不能把最佳轮次挪到最后一个轮次。</summary>
    [Fact]
    public void RecordCurvePoint_FinalValidationDoesNotMoveTheBestEpoch()
    {
        var status = Status(1, 0.60);
        status.RecordCurvePoint();
        status.Epoch = 2;
        status.Metrics.Map5095 = 0.55;
        status.RecordCurvePoint();
        // 训练结束后的最终验证：轮次号仍是 2，数字属于 best.pt，比第 1 轮训练时记下的略高。
        status.Metrics.Map5095 = 0.61;
        status.RecordCurvePoint();

        Assert.Equal(1, status.BestEpoch);
        Assert.Equal(0.60, status.BestScore);
        Assert.Equal(0.61, status.Curve[^1].Score);
    }

    /// <summary>最佳轮次独立于曲线的点数上限：最早的点被丢弃之后，最佳轮次与分数不变。</summary>
    [Fact]
    public void RecordCurvePoint_KeepsTheBestEpochAfterTheCurveIsTrimmed()
    {
        var status = Status(1, 0.9);
        status.RecordCurvePoint();
        for (var epoch = 2; epoch <= TrainingStatus.MaximumCurvePoints + 25; epoch++)
        {
            status.Epoch = epoch;
            status.Metrics.Map5095 = 0.1;
            status.RecordCurvePoint();
        }

        Assert.DoesNotContain(status.Curve, point => point.Epoch == 1);
        Assert.Equal(1, status.BestEpoch);
        Assert.Equal(0.9, status.BestScore);
    }

    /// <summary>没有 mAP 或还没有轮次时不产生最佳轮次；只有 mAP50 时用它比较。</summary>
    [Fact]
    public void RecordCurvePoint_WithoutScoreOrEpoch_LeavesTheBestEmpty()
    {
        var withoutAnyMap = new TrainingStatus { Epoch = 1, Metrics = new TrainingMetrics { BoxLoss = 1.0 } };
        withoutAnyMap.RecordCurvePoint();
        Assert.Equal(0, withoutAnyMap.BestEpoch);
        Assert.Null(withoutAnyMap.BestScore);

        var beforeFirstEpoch = Status(0, 0.5);
        beforeFirstEpoch.RecordCurvePoint();
        Assert.Equal(0, beforeFirstEpoch.BestEpoch);
        Assert.Null(beforeFirstEpoch.BestScore);

        var onlyMap50 = Status(7, null);
        onlyMap50.RecordCurvePoint();
        Assert.Equal(7, onlyMap50.BestEpoch);
        Assert.Equal(0.8, onlyMap50.BestScore);
    }

    /// <summary>页面渲染的是状态快照，快照必须带着最佳轮次，并且不随训练线程之后的更新而变化。</summary>
    [Fact]
    public void Clone_CopiesTheBestEpoch()
    {
        var status = Status(5, 0.7);
        status.RecordCurvePoint();

        var snapshot = status.Clone(includeLogs: false);
        status.Epoch = 6;
        status.Metrics.Map5095 = 0.9;
        status.RecordCurvePoint();

        Assert.Equal(5, snapshot.BestEpoch);
        Assert.Equal(0.7, snapshot.BestScore);
        Assert.Equal(6, status.BestEpoch);
    }
}
