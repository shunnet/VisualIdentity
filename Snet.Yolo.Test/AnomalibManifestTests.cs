using Snet.Yolo.Tasks.Core.Anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// Anomalib 模型清单与 ONNX 输出契约测试。
/// </summary>
public sealed class AnomalibManifestTests
{
    /// <summary>
    /// 验证合法清单能够通过严格反序列化和语义校验。
    /// </summary>
    [Fact]
    public void Deserialize_ValidManifest_ReturnsValidatedManifest()
    {
        var manifest = AnomalibManifestSerializer.Deserialize(CreateValidJson());

        Assert.Equal(AnomalibAlgorithm.Padim, manifest.Algorithm);
        Assert.True(manifest.Input.NormalizationEmbedded);
        Assert.Equal("anomaly_map", manifest.Outputs.AnomalyMap);
    }

    /// <summary>
    /// 验证清单出现未知字段时立即拒绝，避免悄悄忽略拼写错误。
    /// </summary>
    [Fact]
    public void Deserialize_UnknownProperty_ThrowsManifestException()
    {
        var json = CreateValidJson().Replace("\"family\": \"anomalib\"", "\"family\": \"anomalib\", \"unknown\": true", StringComparison.Ordinal);

        var exception = Assert.Throws<AnomalibManifestException>(() => AnomalibManifestSerializer.Deserialize(json));

        Assert.Contains("未知", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证缺少必填字段时拒绝清单，避免默认枚举值掩盖导出错误。
    /// </summary>
    [Fact]
    public void Deserialize_MissingRequiredProperty_ThrowsManifestException()
    {
        var json = CreateValidJson().Replace("\"algorithm\": \"padim\",", string.Empty, StringComparison.Ordinal);

        Assert.Throws<AnomalibManifestException>(() => AnomalibManifestSerializer.Deserialize(json));
    }

    /// <summary>
    /// 验证已内嵌归一化的模型不能再声明均值和标准差，防止双重归一化。
    /// </summary>
    [Fact]
    public void Validate_EmbeddedNormalizationWithMeanStd_ThrowsManifestException()
    {
        var json = CreateValidJson().Replace(
            "\"normalizationEmbedded\": true",
            "\"normalizationEmbedded\": true, \"mean\": [0.485, 0.456, 0.406], \"std\": [0.229, 0.224, 0.225]",
            StringComparison.Ordinal);

        var exception = Assert.Throws<AnomalibManifestException>(() => AnomalibManifestSerializer.Deserialize(json));

        Assert.Contains("双重归一化", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>模型未内嵌归一化时不得缺失通道均值与标准差。</summary>
    [Fact]
    public void Validate_ExternalNormalizationWithoutMeanStd_ThrowsManifestException()
    {
        var json = CreateValidJson().Replace("\"normalizationEmbedded\": true", "\"normalizationEmbedded\": false", StringComparison.Ordinal);

        var exception = Assert.Throws<AnomalibManifestException>(() => AnomalibManifestSerializer.Deserialize(json));

        Assert.Contains("mean", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证输出发现不依赖模型输出顺序，而是同时核对名称、形状和数据类型。
    /// </summary>
    [Fact]
    public void Discover_ShuffledOutputs_BindsByNameShapeAndType()
    {
        var manifest = AnomalibManifestSerializer.Deserialize(CreateValidJson());
        var tensors = new[]
        {
            new OnnxTensorDescriptor("pred_mask", OnnxTensorElementType.Boolean, [1, 1, 256, 256]),
            new OnnxTensorDescriptor("anomaly_map", OnnxTensorElementType.Float32, [1, 1, 256, 256]),
            new OnnxTensorDescriptor("pred_label", OnnxTensorElementType.Int64, [1]),
            new OnnxTensorDescriptor("pred_score", OnnxTensorElementType.Float32, [1])
        };

        var contract = AnomalibOutputContractDiscovery.Discover(manifest, tensors);

        Assert.Equal("pred_score", contract.PredictionScore.Name);
        Assert.Equal("pred_label", contract.PredictionLabel.Name);
        Assert.Equal("anomaly_map", contract.AnomalyMap.Name);
        Assert.Equal("pred_mask", contract.PredictionMask.Name);
    }

    /// <summary>
    /// 验证即使存在形状相同的输出，名称不匹配也不能按位置误绑定。
    /// </summary>
    [Fact]
    public void Discover_WrongNamesWithMatchingShapes_ThrowsContractException()
    {
        var manifest = AnomalibManifestSerializer.Deserialize(CreateValidJson());
        var tensors = new[]
        {
            new OnnxTensorDescriptor("output_0", OnnxTensorElementType.Float32, [1]),
            new OnnxTensorDescriptor("output_1", OnnxTensorElementType.Int64, [1]),
            new OnnxTensorDescriptor("output_2", OnnxTensorElementType.Float32, [1, 1, 256, 256]),
            new OnnxTensorDescriptor("output_3", OnnxTensorElementType.Boolean, [1, 1, 256, 256])
        };

        var exception = Assert.Throws<AnomalibOnnxContractException>(() => AnomalibOutputContractDiscovery.Discover(manifest, tensors));

        Assert.Contains("pred_score", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证输出名称正确但数据类型错误时拒绝加载。
    /// </summary>
    [Fact]
    public void Discover_WrongAnomalyMapType_ThrowsContractException()
    {
        var manifest = AnomalibManifestSerializer.Deserialize(CreateValidJson());
        var tensors = new[]
        {
            new OnnxTensorDescriptor("pred_score", OnnxTensorElementType.Float32, [1]),
            new OnnxTensorDescriptor("pred_label", OnnxTensorElementType.Int64, [1]),
            new OnnxTensorDescriptor("anomaly_map", OnnxTensorElementType.UInt8, [1, 1, 256, 256]),
            new OnnxTensorDescriptor("pred_mask", OnnxTensorElementType.Boolean, [1, 1, 256, 256])
        };

        var exception = Assert.Throws<AnomalibOnnxContractException>(() => AnomalibOutputContractDiscovery.Discover(manifest, tensors));

        Assert.Contains("Float32", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证异常图与掩码空间尺寸不一致时拒绝加载。
    /// </summary>
    [Fact]
    public void Discover_MismatchedSpatialShapes_ThrowsContractException()
    {
        var manifest = AnomalibManifestSerializer.Deserialize(CreateValidJson());
        var tensors = new[]
        {
            new OnnxTensorDescriptor("pred_score", OnnxTensorElementType.Float32, [1]),
            new OnnxTensorDescriptor("pred_label", OnnxTensorElementType.Int64, [1]),
            new OnnxTensorDescriptor("anomaly_map", OnnxTensorElementType.Float32, [1, 1, 256, 256]),
            new OnnxTensorDescriptor("pred_mask", OnnxTensorElementType.Boolean, [1, 1, 128, 128])
        };

        var exception = Assert.Throws<AnomalibOnnxContractException>(() => AnomalibOutputContractDiscovery.Discover(manifest, tensors));

        Assert.Contains("空间尺寸", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 创建覆盖必填字段的合法清单 JSON。
    /// </summary>
    private static string CreateValidJson() => """
        {
          "schemaVersion": "1.0",
          "family": "anomalib",
          "anomalibVersion": "2.6.2",
          "algorithm": "padim",
          "modelSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "input": {
            "name": "input",
            "elementType": "float32",
            "layout": "nchw",
            "colorSpace": "rgb",
            "width": 256,
            "height": 256,
            "resizeMode": "letterbox",
            "valueRange": "zeroToOne",
            "normalizationEmbedded": true
          },
          "outputs": {
            "predictionScore": "pred_score",
            "predictionLabel": "pred_label",
            "anomalyMap": "anomaly_map",
            "predictionMask": "pred_mask"
          },
          "postProcessing": {
            "threshold": 0.5,
            "thresholdSource": "syntheticCalibration"
          }
        }
        """;
}
