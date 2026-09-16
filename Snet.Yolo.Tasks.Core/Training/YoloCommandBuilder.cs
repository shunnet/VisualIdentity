namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;
using System.Globalization;

/// <summary>构建 Ultralytics YOLO 训练与验证命令（以显式参数列表为准，字符串形式仅用于日志展示）。</summary>
public static class YoloCommandBuilder
{
    /// <summary>
    /// 构建训练参数列表（不含可执行文件）。每个 key=value 都是独立参数，路径含空格也不会被拆开。
    ///
    /// 显式传 project=/name=：Ultralytics 默认输出目录来自它的全局设置（runs_dir），
    /// 在不同部署里可能落到工程目录之外，导致训练完成后找不到 best.pt。
    /// </summary>
    public static IReadOnlyList<string> BuildTrainArguments(string dataYaml, TrainingOptions options, string? projectDirectory = null)
    {
        var arguments = new List<string>
        {
            NormalizeTask(options.Task),
            "train",
            "data=" + dataYaml,
            "model=" + options.Model,
            "epochs=" + options.Epochs.ToString(CultureInfo.InvariantCulture),
            "imgsz=" + options.ImgSize.ToString(CultureInfo.InvariantCulture),
            "device=" + options.Device,
            "verbose=True",
        };
        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            arguments.Add("project=" + projectDirectory);
            arguments.Add("name=train");
        }
        return arguments;
    }

    /// <summary>构建验证参数列表（不含可执行文件）。</summary>
    public static IReadOnlyList<string> BuildValArguments(string dataYaml, string modelPath, TrainingOptions options) => new[]
    {
        NormalizeTask(options.Task),
        "val",
        "data=" + dataYaml,
        "model=" + modelPath,
        "imgsz=" + options.ImgSize.ToString(CultureInfo.InvariantCulture),
        "device=" + options.Device,
        "verbose=True",
    };

    /// <summary>构建 ONNX 导出参数列表（不含可执行文件）。</summary>
    public static IReadOnlyList<string> BuildExportArguments(string modelPath, int opset) => new[]
    {
        "export",
        "model=" + modelPath,
        "format=onnx",
        "imgsz=640",
        "opset=" + opset.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>构建显式包含任务类型的训练命令（仅用于日志展示）。</summary>
    public static string BuildTrain(string yoloExe, string dataYaml, TrainingOptions options, string? projectDirectory = null)
        => CommandLine.Join(yoloExe, BuildTrainArguments(dataYaml, options, projectDirectory));

    /// <summary>构建显式包含任务类型的验证命令（仅用于日志展示）。</summary>
    public static string BuildVal(string yoloExe, string dataYaml, string modelPath, TrainingOptions options)
        => CommandLine.Join(yoloExe, BuildValArguments(dataYaml, modelPath, options));

    /// <summary>将未知任务安全降级为目标检测任务。</summary>
    private static string NormalizeTask(string? task) => task?.ToLowerInvariant() switch
    {
        "segment" => "segment",
        "classify" => "classify",
        "pose" => "pose",
        "obb" => "obb",
        _ => "detect",
    };
}
