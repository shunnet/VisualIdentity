using SkiaSharp;
using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>部署清单声明的抗锯齿浮点缩放须与训练输入一致。</summary>
public sealed class AnomalibAntialiasTests
{
    /// <summary>5K 输入必须保留 float32 坐标约定，防止小图回归通过但大图采样出现偏差。</summary>
    [Fact]
    public void CreateInput_LargeCoordinates_KeepFloat32SamplingPrecision()
    {
        using var source = new SKBitmap(5120, 1);
        for (var x = 0; x < source.Width; x++) { source.SetPixel(x, 0, new SKColor((byte)(x * 37 % 256), 0, 0)); }
        var tensor = AnomalibOnnxInference.CreateInput(source, new()
        {
            Width = 960, Height = 1, Interpolation = AnomalibInterpolation.BilinearAntialias,
            NormalizationEmbedded = true, ValueRange = AnomalibValueRange.ZeroToOne,
        });
        int[] positions = [256, 512, 767, 900, 958, 959];
        float[] expected = [.483667821f, .418776095f, .490330279f, .446276098f, .579630971f, .550278246f];
        for (var i = 0; i < positions.Length; i++) { Assert.InRange(tensor[0, 0, 0, positions[i]], expected[i] - .000002f, expected[i] + .000002f); }
    }

    /// <summary>以 torchvision 生成的固定结果比较完整 JSON 契约及 RGB NCHW 输入，不只检查图像外观。</summary>
    [Fact]
    public void CreateInput_FloatAntialias_MatchesTorchvisionReference()
    {
        using var source = new SKBitmap(9, 11);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var i = (y * source.Width + x) * 3;
                source.SetPixel(x, y, new SKColor((byte)(i * 37 % 256), (byte)((i + 1) * 37 % 256), (byte)((i + 2) * 37 % 256)));
            }
        }
        var manifest = AnomalibManifestSerializer.Deserialize("""
            {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"patchCore","modelSha256":"DIGEST",
             "input":{"name":"input","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":32,"height":32,"resizeMode":"stretch","interpolation":"bilinearAntialias","valueRange":"zeroToOne","normalizationEmbedded":true},
             "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},
             "postProcessing":{"threshold":0.5,"thresholdSource":"manual"}}
            """.Replace("DIGEST", new string('a', 64), StringComparison.Ordinal));
        manifest.Input.Width = manifest.Input.Height = 4;
        var tensor = AnomalibOnnxInference.CreateInput(source, manifest.Input);
        float[] expected = [
            .517414629f, .452174157f, .482972860f, .508874297f, .476106495f, .468893677f, .542134523f, .470115811f,
            .523094296f, .485775530f, .453318954f, .562997282f, .429837078f, .582482159f, .519418240f, .446793199f,
            .461728424f, .514292181f, .460750610f, .486652106f, .536110282f, .458098203f, .497875065f, .521036446f,
            .585966408f, .555375397f, .484149545f, .464763820f, .441078961f, .409263611f, .500823557f, .573300123f,
            .438974977f, .493430346f, .520148039f, .488332778f, .600416481f, .490153074f, .473612309f, .479691923f,
            .472913206f, .447044462f, .551300824f, .470747024f, .546338797f, .486345321f, .415119410f, .557452023f];
        Assert.Equal(expected.Length, tensor.Length);
        for (var i = 0; i < expected.Length; i++) { Assert.InRange(tensor.GetValue(i), expected[i] - .000002f, expected[i] + .000002f); }
    }
}
