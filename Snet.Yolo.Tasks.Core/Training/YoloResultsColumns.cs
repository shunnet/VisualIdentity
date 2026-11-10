namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>results.csv 中各指标所在的列下标；列缺失为 -1。</summary>
internal readonly struct YoloResultsColumns
{
    /// <summary>按表头名称定位列。</summary>
    /// <param name="header">已去除空白的表头。</param>
    public YoloResultsColumns(string[] header)
    {
        Epoch = IndexOf(header, "epoch");
        Precision = IndexOf(header, "metrics/precision(B)");
        Recall = IndexOf(header, "metrics/recall(B)");
        Map50 = IndexOf(header, "metrics/mAP50(B)");
        Map5095 = IndexOf(header, "metrics/mAP50-95(B)");
        Accuracy = IndexOf(header, "metrics/accuracy_top1");
        BoxLoss = IndexOf(header, "train/box_loss");
        ClsLoss = IndexOf(header, "train/cls_loss");
        DflLoss = IndexOf(header, "train/dfl_loss");
        Loss = IndexOf(header, "train/loss");
        MaskMap50 = IndexOf(header, "metrics/mAP50(M)");
        MaskMap5095 = IndexOf(header, "metrics/mAP50-95(M)");
        PoseMap50 = IndexOf(header, "metrics/mAP50(P)");
        PoseMap5095 = IndexOf(header, "metrics/mAP50-95(P)");
        AccuracyTop5 = IndexOf(header, "metrics/accuracy_top5");
    }

    /// <summary>轮次列。</summary>
    public int Epoch { get; }

    /// <summary>精确率列。</summary>
    public int Precision { get; }

    /// <summary>召回率列。</summary>
    public int Recall { get; }

    /// <summary>mAP50 列。</summary>
    public int Map50 { get; }

    /// <summary>mAP50-95 列。</summary>
    public int Map5095 { get; }

    /// <summary>分类 top1 准确率列。</summary>
    public int Accuracy { get; }

    /// <summary>框损失列。</summary>
    public int BoxLoss { get; }

    /// <summary>分类损失列。</summary>
    public int ClsLoss { get; }

    /// <summary>分布焦点损失列。</summary>
    public int DflLoss { get; }

    /// <summary>分类任务的总损失列。</summary>
    public int Loss { get; }

    /// <summary>实例分割的掩码 mAP50 列。</summary>
    public int MaskMap50 { get; }

    /// <summary>实例分割的掩码 mAP50-95 列。</summary>
    public int MaskMap5095 { get; }

    /// <summary>姿态估计的关键点 mAP50 列。</summary>
    public int PoseMap50 { get; }

    /// <summary>姿态估计的关键点 mAP50-95 列。</summary>
    public int PoseMap5095 { get; }

    /// <summary>分类 top5 准确率列。</summary>
    public int AccuracyTop5 { get; }

    /// <summary>在表头中查找列名，找不到返回 -1。</summary>
    private static int IndexOf(string[] header, string name)
        => Array.FindIndex(header, column => string.Equals(column, name, StringComparison.Ordinal));
}
