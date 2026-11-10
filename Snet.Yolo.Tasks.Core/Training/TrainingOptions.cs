namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练选项（弹窗填写）。</summary>
public sealed class TrainingOptions
{
    /// <summary>训练轮数。默认 500：图片少时 50 轮只有几十次参数更新，模型学不到东西（识别不到任何目标）。</summary>
    public int Epochs { get; set; } = 500;
    /// <summary>Square training image size.</summary>
    public int ImgSize { get; set; } = 640;
    /// <summary>auto / cpu / 0 / 0,1</summary>
    public string Device { get; set; } = "auto";
    /// <summary>Base Ultralytics model name or path.</summary>
    public string Model { get; set; } = "yolo26n.pt";
    /// <summary>Ultralytics task name.</summary>
    public string Task { get; set; } = "detect";
    /// <summary>是否使用验证集（自动划分训练集的 10%）。默认关闭：由用户按需勾选，避免小数据集白白少掉 10% 训练图。</summary>
    public bool UseVal { get; set; }
}
