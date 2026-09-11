namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>构建 Ultralytics YOLO 训练与验证命令。</summary>
public static class YoloCommandBuilder
{
    /// <summary>构建显式包含任务类型的训练命令。</summary>
    public static string BuildTrain(string yoloExe, string dataYaml, TrainingOptions options)
        => Quote(yoloExe) + " " + NormalizeTask(options.Task) + " train data=" + Quote(dataYaml)
            + " model=" + Quote(options.Model) + " epochs=" + options.Epochs
            + " imgsz=" + options.ImgSize + " device=" + options.Device
            + " verbose=True";

    /// <summary>构建显式包含任务类型的验证命令。</summary>
    public static string BuildVal(string yoloExe, string dataYaml, string modelPath, TrainingOptions options)
        => Quote(yoloExe) + " " + NormalizeTask(options.Task) + " val data=" + Quote(dataYaml) + " model=" + Quote(modelPath)
            + " imgsz=" + options.ImgSize + " device=" + options.Device
            + " verbose=True";

    /// <summary>将未知任务安全降级为目标检测任务。</summary>
    private static string NormalizeTask(string? task) => task?.ToLowerInvariant() switch
    {
        "segment" => "segment",
        "classify" => "classify",
        "pose" => "pose",
        "obb" => "obb",
        _ => "detect",
    };

    /// <summary>仅在参数包含空格时添加命令行引号。</summary>
    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
