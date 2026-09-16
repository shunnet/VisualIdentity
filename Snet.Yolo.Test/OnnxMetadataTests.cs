using Snet.Yolo.Tasks.Core.Training;
using System.Text;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// ONNX description 元数据规范化契约。
///
/// 现场事故（已在本地用真实模型复现并逐字段二分确认）：YoloDotNet 从 description 解析模型型号，
/// “Ultralytics YOLO26n model trained on ...” 会让它在 RunObjectDetection 内抛
/// IndexOutOfRangeException（验证页只显示“识别返回: Index was outside the bounds of the array”），
/// 而 YOLO11n / YOLO11s / YOLOv8n 都正常。NuGet 上最新的 YoloDotNet 4.2.0 仍未支持 YOLO26，
/// 因此登记模型时把型号名等长改写成 YOLO11。
/// </summary>
public sealed class OnnxMetadataTests
{
    [Fact]
    public void NormalizeDescription_RewritesYolo26ToYolo11AndKeepsLength()
    {
        const string original = "Ultralytics YOLO26n model trained on /home/ys/data.yaml";
        var normalized = OnnxMetadata.NormalizeDescription(original);

        Assert.Equal("Ultralytics YOLO11n model trained on /home/ys/data.yaml", normalized);
        // 等长是“就地覆盖字节”方案的前提
        Assert.Equal(original.Length, normalized.Length);
    }

    [Theory]
    [InlineData("Ultralytics YOLO26n model trained on x", true)]
    [InlineData("Ultralytics yolo26s model trained on x", true)]
    [InlineData("Ultralytics YOLO11n model trained on x", false)]
    [InlineData("Ultralytics YOLOv8n model trained on x", false)]
    [InlineData("", false)]
    public void NeedsNormalization_OnlyMatchesYolo26(string description, bool expected)
    {
        Assert.Equal(expected, OnnxMetadata.NeedsNormalization(description));
    }

    [Fact]
    public void TryNormalizeDescriptionFile_RewritesInPlaceAndKeepsFileBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-onnx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "model.onnx");
        try
        {
            var tail = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            var original = BuildOnnxLikeFile("Ultralytics YOLO26n model trained on /home/ys/data.yaml", tail);
            File.WriteAllBytes(path, original);

            var replaced = OnnxMetadata.TryNormalizeDescriptionFile(path);

            Assert.Equal("Ultralytics YOLO11n model trained on /home/ys/data.yaml", replaced);
            var after = File.ReadAllBytes(path);
            Assert.Equal(original.Length, after.Length);                       // 文件大小不变
            Assert.Equal(tail, after[^tail.Length..]);                          // 描述之后的字节原样保留
            Assert.Equal(original[..8], after[..8]);                            // 头部（protobuf 标签）保持
            Assert.Contains("YOLO11n", Encoding.UTF8.GetString(after));
            Assert.DoesNotContain("YOLO26", Encoding.UTF8.GetString(after));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void TryNormalizeDescriptionFile_DoesNothingWhenDescriptionIsFine()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-onnx-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "model.onnx");
        try
        {
            var original = BuildOnnxLikeFile("Ultralytics YOLO11n model trained on config.yaml", Array.Empty<byte>());
            File.WriteAllBytes(path, original);

            Assert.Null(OnnxMetadata.TryNormalizeDescriptionFile(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>构造一个含 metadata_props(description) 的最小 protobuf 片段，用于验证就地改写。</summary>
    private static byte[] BuildOnnxLikeFile(string description, byte[] tail)
    {
        var key = Encoding.ASCII.GetBytes("description");
        var value = Encoding.UTF8.GetBytes(description);
        var entry = new List<byte> { 0x0A, (byte)key.Length };
        entry.AddRange(key);
        entry.Add(0x12);
        entry.Add((byte)value.Length);
        entry.AddRange(value);

        var bytes = new List<byte> { 0x72, (byte)entry.Count };   // ModelProto.metadata_props = 字段 14
        bytes.AddRange(entry);
        bytes.AddRange(tail);
        return bytes.ToArray();
    }
}
