using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Snet.Yolo.Tasks.Core.Anomalib;

/// <summary>
/// Anomalib 模型清单的严格序列化与校验入口。
/// </summary>
public static partial class AnomalibManifestSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>
    /// 严格反序列化并校验 Anomalib 模型清单。
    /// </summary>
    /// <param name="json">清单 JSON 文本。</param>
    /// <returns>通过结构和语义校验的模型清单。</returns>
    /// <exception cref="AnomalibManifestException">JSON 或清单语义不符合约定。</exception>
    public static AnomalibModelManifest Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new AnomalibManifestException("Anomalib 模型清单不能为空。");
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<AnomalibModelManifest>(json, SerializerOptions)
                ?? throw new AnomalibManifestException("Anomalib 模型清单不能为 null。");
            Validate(manifest);
            return manifest;
        }
        catch (AnomalibManifestException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new AnomalibManifestException("Anomalib 模型清单 JSON 无效或包含未知字段。", exception);
        }
    }

    /// <summary>
    /// 序列化已经通过语义校验的 Anomalib 模型清单。
    /// </summary>
    /// <param name="manifest">待序列化的模型清单。</param>
    /// <returns>使用稳定字段命名的 JSON 文本。</returns>
    public static string Serialize(AnomalibModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Validate(manifest);
        return JsonSerializer.Serialize(manifest, SerializerOptions);
    }

    /// <summary>
    /// 校验 Anomalib 模型清单的必填字段和交叉字段约束。
    /// </summary>
    /// <param name="manifest">待校验的模型清单。</param>
    /// <exception cref="AnomalibManifestException">清单不满足部署约定。</exception>
    public static void Validate(AnomalibModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw new AnomalibManifestException($"不支持的 Anomalib 清单版本：{manifest.SchemaVersion}。");
        }
        if (!string.Equals(manifest.Family, "anomalib", StringComparison.Ordinal))
        {
            throw new AnomalibManifestException("Anomalib 模型清单 family 必须为 anomalib。");
        }
        if (string.IsNullOrWhiteSpace(manifest.AnomalibVersion))
        {
            throw new AnomalibManifestException("Anomalib 版本不能为空。");
        }
        if (string.IsNullOrWhiteSpace(manifest.ModelSha256) || !Sha256Pattern().IsMatch(manifest.ModelSha256))
        {
            throw new AnomalibManifestException("ONNX 模型 SHA-256 必须是 64 位十六进制字符串。");
        }
        if (!Enum.IsDefined(manifest.Algorithm))
        {
            throw new AnomalibManifestException("Anomalib 模型算法无效。");
        }

        ValidateInput(manifest.Input);
        ValidateOutputs(manifest.Outputs);
        if (manifest.PostProcessing is null)
        {
            throw new AnomalibManifestException("Anomalib 后处理契约不能为空。");
        }
        if (!Enum.IsDefined(manifest.PostProcessing.ThresholdSource))
        {
            throw new AnomalibManifestException("异常阈值来源无效。");
        }
        if (!float.IsFinite(manifest.PostProcessing.Threshold)
            || manifest.PostProcessing.Threshold is < 0 or > 1)
        {
            throw new AnomalibManifestException("异常阈值必须是 0 到 1 之间的有限数值。");
        }
    }

    /// <summary>
    /// 校验模型输入约定，尤其防止内嵌归一化被重复执行。
    /// </summary>
    /// <param name="input">模型输入约定。</param>
    private static void ValidateInput(AnomalibInputContract input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
        {
            throw new AnomalibManifestException("ONNX 输入节点名称不能为空。");
        }
        if (input.ElementType != OnnxTensorElementType.Float32)
        {
            throw new AnomalibManifestException("第一阶段仅支持 Float32 图像输入。");
        }
        if (input.Layout != AnomalibTensorLayout.Nchw)
        {
            throw new AnomalibManifestException("第一阶段仅支持 NCHW 图像输入布局。");
        }
        if (!Enum.IsDefined(input.ColorSpace)
            || !Enum.IsDefined(input.ResizeMode)
            || !Enum.IsDefined(input.ValueRange))
        {
            throw new AnomalibManifestException("Anomalib 输入颜色空间、缩放方式或像素值范围无效。");
        }
        if (input.Width is < 32 or > 8192 || input.Height is < 32 or > 8192)
        {
            throw new AnomalibManifestException("模型输入宽高必须在 32 到 8192 像素之间。");
        }
        if (input.NormalizationEmbedded && (input.Mean is not null || input.Std is not null))
        {
            throw new AnomalibManifestException("模型已经内嵌归一化，不能再次声明 mean/std，以免发生双重归一化。");
        }
        if ((input.Mean is null) != (input.Std is null))
        {
            throw new AnomalibManifestException("输入 mean 和 std 必须同时提供或同时省略。");
        }
        if (!input.NormalizationEmbedded && input.Mean is null)
        {
            throw new AnomalibManifestException("模型未内嵌归一化时必须提供 mean 和 std。");
        }
        if (input.Mean is not null)
        {
            if (input.Mean.Length != 3 || input.Std!.Length != 3
                || input.Mean.Any(static value => !float.IsFinite(value))
                || input.Std.Any(static value => !float.IsFinite(value) || value <= 0))
            {
                throw new AnomalibManifestException("输入 mean/std 必须各包含三个有限通道值，且 std 必须大于零。");
            }
        }
    }

    /// <summary>
    /// 校验输出名称完整且互不重复。
    /// </summary>
    /// <param name="outputs">模型输出名称。</param>
    private static void ValidateOutputs(AnomalibOutputNames outputs)
    {
        if (outputs is null)
        {
            throw new AnomalibManifestException("ONNX 输出契约不能为空。");
        }

        var names = new[]
        {
            outputs.PredictionScore,
            outputs.PredictionLabel,
            outputs.AnomalyMap,
            outputs.PredictionMask
        };
        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new AnomalibManifestException("四个 Anomalib 输出节点名称均不能为空。");
        }
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new AnomalibManifestException("Anomalib 输出节点名称不能重复。");
        }
    }

    /// <summary>
    /// 创建拒绝未知成员且使用驼峰枚举文本的 JSON 选项。
    /// </summary>
    /// <returns>清单专用 JSON 选项。</returns>
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    /// <summary>
    /// 获取 SHA-256 十六进制文本校验表达式。
    /// </summary>
    /// <returns>编译期生成的正则表达式。</returns>
    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
