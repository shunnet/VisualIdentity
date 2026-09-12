using System.Text.Json;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证页面阈值从 JSON 到推理数据的完整传递。</summary>
public sealed class ValidationParameterTests
{
    /// <summary>确保新页面会把置信度写成 JSON 数字而非字符串。</summary>
    [Fact]
    public void NumericDictionary_SerializesConfidenceAsNumber()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, double> { ["Confidence"] = 0.95, ["Iou"] = 0.7 });
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("Confidence").ValueKind);
        Assert.Equal(0.95, document.RootElement.GetProperty("Confidence").GetDouble());
    }

    /// <summary>确保当前数字格式能够完整进入模型推理参数。</summary>
    [Fact]
    public void NumericJson_PreservesConfidenceThreshold()
    {
        var data = ValidationService.FromJson<ObjectDetectionData>("{\"Confidence\":0.95,\"Iou\":0.7}");

        Assert.Equal(0.95, data.Confidence);
        Assert.Equal(0.7, data.Iou);
    }

    /// <summary>确保刷新前已产生的字符串数值也不会回退到默认置信度。</summary>
    [Fact]
    public void LegacyStringJson_PreservesConfidenceThreshold()
    {
        var data = ValidationService.FromJson<ObjectDetectionData>("{\"Confidence\":\"0.95\",\"Iou\":\"0.7\"}");

        Assert.Equal(0.95, data.Confidence);
        Assert.Equal(0.7, data.Iou);
    }

    /// <summary>确保分割、姿态、OBB 和分类模型各自的动态参数都能传入强类型数据。</summary>
    [Fact]
    public void EveryModelType_PreservesItsDynamicParameters()
    {
        var segmentation = ValidationService.FromJson<SegmentationData>("{\"Confidence\":0.91,\"Iou\":0.72,\"PixelConfidence\":0.83}");
        var pose = ValidationService.FromJson<PoseEstimationData>("{\"Confidence\":0.92,\"Iou\":0.73}");
        var obb = ValidationService.FromJson<ObbDetectionData>("{\"Confidence\":0.93,\"Iou\":0.74}");
        var classification = ValidationService.FromJson<ClassificationData>("{\"Classes\":3}");

        Assert.Equal(0.91, segmentation.Confidence);
        Assert.Equal(0.72, segmentation.Iou);
        Assert.Equal(0.83, segmentation.PixelConfidence);
        Assert.Equal(0.92, pose.Confidence);
        Assert.Equal(0.73, pose.Iou);
        Assert.Equal(0.93, obb.Confidence);
        Assert.Equal(0.74, obb.Iou);
        Assert.Equal(3, classification.Classes);
    }

    /// <summary>确保无效参数会显式报错，不会静默使用默认阈值。</summary>
    [Fact]
    public void InvalidJson_DoesNotSilentlyUseDefaults()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ValidationService.FromJson<ObjectDetectionData>("{\"Confidence\":\"invalid\"}"));

        Assert.Contains("参数格式无效", exception.Message);
    }
}
