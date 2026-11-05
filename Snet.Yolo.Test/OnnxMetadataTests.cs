using System.Reflection;
using Xunit;
using Snet.Yolo.Server.yolo.Enums;

namespace Snet.Yolo.Test;

/// <summary>模型版本必须保持真实，YOLO26 不能伪装为 YOLO11。</summary>
public sealed class OnnxMetadataTests
{
    [Theory]
    [InlineData("Ultralytics YOLO26n model trained on x", ModelVersion.V26)]
    [InlineData("Ultralytics YOLO11n model trained on x", ModelVersion.V11)]
    public void OriginalDescription_SelectsMatchingParser(string description, ModelVersion expected)
    {
        var parser = typeof(Snet.Yolo.Server.yolo.Yolo).Assembly.GetType("Snet.Yolo.Server.yolo.Extensions.ParseOnnxData")!;
        var method = parser.GetMethod("GetModelVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, method.Invoke(null, [description]));
    }
}
