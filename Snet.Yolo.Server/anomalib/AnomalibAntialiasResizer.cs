using System.Buffers;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Snet.Yolo.Server.anomalib;

/// <summary>以可分离三角核完成浮点抗锯齿缩放，不将大原图展开为整幅浮点数组。</summary>
internal static class AnomalibAntialiasResizer
{
    /// <summary>按训练的半像素中心、缩小时扩展滤波核及边界归一化生成输入。</summary>
    /// <param name="source">已摆正方向的原图。</param>
    /// <param name="input">声明抗锯齿拉伸的输入契约。</param>
    /// <returns>归一化并按契约排列的张量。</returns>
    internal static DenseTensor<float> CreateInput(SKBitmap source, AnomalibInputContract input)
    {
        var horizontal = CreateWeights(source.Width, input.Width);
        var vertical = CreateWeights(source.Height, input.Height);
        var rows = new float[]?[source.Height];
        var firstCached = 0;
        var tensor = input.Layout == AnomalibTensorLayout.Nchw
            ? new DenseTensor<float>([1, 3, input.Height, input.Width])
            : new DenseTensor<float>([1, input.Height, input.Width, 3]);
        var divisor = input.ValueRange == AnomalibValueRange.ZeroToOne ? 255f : 1f;
        try
        {
            for (var y = 0; y < input.Height; y++)
            {
                var (start, weights) = vertical[y];
                while (firstCached < start)
                {
                    if (rows[firstCached] is { } expired) { ArrayPool<float>.Shared.Return(expired); rows[firstCached] = null; }
                    firstCached++;
                }
                for (var k = 0; k < weights.Length; k++)
                {
                    if (rows[start + k] is not null) { continue; }
                    var row = ArrayPool<float>.Shared.Rent(input.Width * 3);
                    rows[start + k] = row;
                    ResizeRow(source, start + k, horizontal, row);
                }
                for (var x = 0; x < input.Width; x++)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var rgbChannel = input.ColorSpace == AnomalibColorSpace.Bgr ? 2 - channel : channel;
                        var value = 0f;
                        for (var k = 0; k < weights.Length; k++) { value += rows[start + k]![x * 3 + rgbChannel] * weights[k]; }
                        value /= divisor;
                        if (!input.NormalizationEmbedded && input.Mean is not null && input.Std is not null)
                        {
                            value = (value - input.Mean[rgbChannel]) / input.Std[rgbChannel];
                        }
                        if (input.Layout == AnomalibTensorLayout.Nchw) { tensor[0, channel, y, x] = value; }
                        else { tensor[0, y, x, channel] = value; }
                    }
                }
            }
            return tensor;
        }
        finally
        {
            foreach (var row in rows) { if (row is not null) { ArrayPool<float>.Shared.Return(row); } }
        }
    }

    /// <summary>预计算一维抗锯齿双线性权重，边缘只使用图内像素并重新归一化。</summary>
    /// <param name="sourceSize">原始长度。</param>
    /// <param name="targetSize">目标长度。</param>
    /// <returns>每个输出位置对应的首个源像素和连续权重。</returns>
    private static (int Start, float[] Weights)[] CreateWeights(int sourceSize, int targetSize)
    {
        var result = new (int, float[])[targetSize];
        // 系数使用 float32，与训练张量的缩放系数精度一致；大图采用 double 会产生可测的采样坐标偏差。
        var scale = sourceSize / (float)targetSize;
        var support = Math.Max(1f, scale);
        var inverseScale = scale >= 1 ? (float)(1d / scale) : 1f;
        for (var i = 0; i < targetSize; i++)
        {
            var center = (float)((i + .5) * scale);
            var start = Math.Max(0, (int)Math.Floor(center - support + .5));
            var end = Math.Min(sourceSize, (int)Math.Floor(center + support + .5));
            var weights = new float[end - start];
            var total = 0f;
            for (var k = 0; k < weights.Length; k++)
            {
                var distance = (float)((start + k - center + .5) * inverseScale);
                var weight = Math.Max(0f, 1f - Math.Abs(distance));
                weights[k] = weight;
                total += weight;
            }
            for (var k = 0; k < weights.Length; k++) { weights[k] /= total; }
            result[i] = (start, weights);
        }
        return result;
    }

    /// <summary>计算一条横向浮点扫描线；常用 32 位像素直接读取，其他格式沿用 Skia 的颜色转换。</summary>
    /// <param name="source">原始位图。</param>
    /// <param name="y">源扫描线。</param>
    /// <param name="horizontal">预计算的横向权重。</param>
    /// <param name="destination">由数组池提供的 RGB 扫描线。</param>
    private static unsafe void ResizeRow(SKBitmap source, int y, (int Start, float[] Weights)[] horizontal, float[] destination)
    {
        var packed = source.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        var premultiplied = source.AlphaType == SKAlphaType.Premul;
        var address = (byte*)source.GetPixels() + (long)y * source.RowBytes;
        var redOffset = source.ColorType == SKColorType.Bgra8888 ? 2 : 0;
        for (var x = 0; x < horizontal.Length; x++)
        {
            var (start, weights) = horizontal[x];
            float red = 0, green = 0, blue = 0;
            for (var k = 0; k < weights.Length; k++)
            {
                float r, g, b;
                if (packed)
                {
                    var pixel = address + (start + k) * 4;
                    var alpha = premultiplied && pixel[3] is > 0 and < 255 ? 255f / pixel[3] : 1f;
                    r = Math.Min(255, pixel[redOffset] * alpha);
                    g = Math.Min(255, pixel[1] * alpha);
                    b = Math.Min(255, pixel[2 - redOffset] * alpha);
                }
                else
                {
                    var pixel = source.GetPixel(start + k, y);
                    r = pixel.Red; g = pixel.Green; b = pixel.Blue;
                }
                red += r * weights[k]; green += g * weights[k]; blue += b * weights[k];
            }
            destination[x * 3] = red; destination[x * 3 + 1] = green; destination[x * 3 + 2] = blue;
        }
        GC.KeepAlive(source);
    }
}
