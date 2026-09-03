namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>构建 yolo 训练/验证命令行（最新版 Ultralytics CLI，缺省 detect 任务）。</summary>
public static class YoloCommandBuilder
{
    /// <summary>训练命令。</summary>
    public static string BuildTrain(string yoloExe, string dataYaml, TrainingOptions options)
        => Quote(yoloExe) + " train data=" + Quote(dataYaml)
            + " model=" + Quote(options.Model) + " epochs=" + options.Epochs
            + " imgsz=" + options.ImgSize + " device=" + options.Device + " verbose=True";

    /// <summary>验证命令（用指定权重，如 best.pt）。</summary>
    public static string BuildVal(string yoloExe, string dataYaml, string modelPath, TrainingOptions options)
        => Quote(yoloExe) + " val data=" + Quote(dataYaml) + " model=" + Quote(modelPath)
            + " imgsz=" + options.ImgSize + " device=" + options.Device + " verbose=True";

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
