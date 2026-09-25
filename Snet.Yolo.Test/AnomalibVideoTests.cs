using SkiaSharp;
using Snet.Yolo.Server.Anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Anomalib 视频抽帧参数和原尺寸标注帧的专项回归测试。</summary>
public sealed class AnomalibVideoTests
{
    /// <summary>FFprobe 的整数与分数帧率必须正确解析，非法值不可伪装成有效帧率。</summary>
    [Theory]
    [InlineData("30/1", 30)]
    [InlineData("30000/1001", 29.97002997002997)]
    [InlineData("25", 25)]
    [InlineData("0/0", 0)]
    [InlineData("invalid", 0)]
    public void ParseFrameRate_HandlesProbeValues(string value, double expected)
        => Assert.Equal(expected, AnomalibVideoService.ParseFrameRate(value), precision: 10);

    /// <summary>变帧率视频必须按原始时间戳计算每帧时长，而非统一使用平均帧率。</summary>
    [Fact]
    public void FrameDurations_PreservesVariableFrameIntervals()
    {
        var durations = AnomalibVideoService.FrameDurations([1.0, 1.04, 1.16], 25);

        Assert.Equal(0.04, durations[0], precision: 10);
        Assert.Equal(0.12, durations[1], precision: 10);
        Assert.Equal(0.04, durations[2], precision: 10);
    }

    /// <summary>乱序时间戳不能被当作有效的视频播放顺序。</summary>
    [Fact]
    public void FrameDurations_RejectsNonIncreasingTimestamp()
        => Assert.Throws<InvalidDataException>(() => AnomalibVideoService.FrameDurations([1.0, 1.0], 25));

    /// <summary>视频帧标注应保持原有像素尺寸，并在对应区域绘制红色边框。</summary>
    [Fact]
    public void DrawFrame_PreservesDimensionsAndMarksOriginalCoordinates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anomalib-video-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.png");
            var destination = Path.Combine(directory, "result.jpg");
            using (var bitmap = new SKBitmap(320, 180))
            {
                bitmap.Erase(SKColors.Black);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(source);
                encoded.SaveTo(stream);
            }
            var result = new AnomalibImageResult
            {
                ImageScore = 0.9f, IsAnomalous = true,
                OriginalWidth = 320, OriginalHeight = 180,
                MapWidth = 32, MapHeight = 18,
                InferenceMilliseconds = 1,
                Regions = [new AnomalibRegionResult
                {
                    RegionId = 1, Bounds = new PixelRectangle(40, 30, 80, 50),
                    PixelArea = 100, MaximumScore = 0.9f, MeanScore = 0.8f,
                }],
            };
            AnomalibVideoService.DrawFrame(source, destination, result);
            using var annotated = SKBitmap.Decode(destination);
            Assert.NotNull(annotated);
            Assert.Equal(320, annotated.Width);
            Assert.Equal(180, annotated.Height);
            Assert.True(annotated.GetPixel(40, 50).Red > annotated.GetPixel(40, 50).Blue);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
