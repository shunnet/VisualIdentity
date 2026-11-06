namespace Snet.Yolo.Server.anomalib;

using System.Text.Json.Serialization;

/// <summary>单张正常校准图片的推理、一致性与误报诊断，失败项仍保留在完整报告中。</summary>
public sealed class AnomalibCalibrationSampleResult
{
    /// <summary>从 1 开始的校准序号。</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>对应工程源图片的文件名，不包含服务器目录。</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    /// <summary>该图片是否同时满足正常判定和各项一致性规则。</summary>
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    /// <summary>训练模型输出的整图异常分数，未完成时为空。</summary>
    [JsonPropertyName("torchScore")]
    public double? TorchScore { get; init; }

    /// <summary>ONNX 输出的整图异常分数，未完成时为空。</summary>
    [JsonPropertyName("onnxScore")]
    public double? OnnxScore { get; init; }

    /// <summary>训练模型是否判为异常，未完成时为空。</summary>
    [JsonPropertyName("torchLabel")]
    public bool? TorchLabel { get; init; }

    /// <summary>ONNX 是否判为异常，未完成时为空。</summary>
    [JsonPropertyName("onnxLabel")]
    public bool? OnnxLabel { get; init; }

    /// <summary>两路整图分数的绝对差。</summary>
    [JsonPropertyName("scoreDifference")]
    public double? ScoreDifference { get; init; }

    /// <summary>两路二值异常掩码的交并比。</summary>
    [JsonPropertyName("maskIou")]
    public double? MaskIou { get; init; }

    /// <summary>模型内嵌后处理使用的归一化整图阈值，无法读取时为空。</summary>
    [JsonPropertyName("imageThreshold")]
    public double? ImageThreshold { get; init; }

    /// <summary>误报、一致性差异或推理异常的具体说明。</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
