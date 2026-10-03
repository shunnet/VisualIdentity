using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Snet.Yolo.Server.sam;

public sealed partial class SamOnnxRuntime
{
    /// <summary>固定 Tracker 导出的正方形输入尺寸，不使用 SAM 1/2 的 1024 像素契约。</summary>
    internal const int Sam3InputSize = 1008;
    private static readonly string[] Sam3Features = ["image_embeddings.0", "image_embeddings.1", "image_embeddings.2"];
    private static readonly int[][] Sam3FeatureShapes = [[1, 32, 288, 288], [1, 64, 144, 144], [1, 256, 72, 72]];

    /// <summary>RGB 按 CHW 排列并归一化到 [-1,1]；直接拉伸到正方形，不补边。</summary>
    internal static float[] Sam3Pixels(SKBitmap original, CancellationToken token)
    {
        using var resized = new SKBitmap(Sam3InputSize, Sam3InputSize);
        using (var canvas = new SKCanvas(resized))
        { canvas.DrawBitmap(original, new SKRect(0, 0, Sam3InputSize, Sam3InputSize), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)); }
        var plane = Sam3InputSize * Sam3InputSize; var pixels = new float[plane * 3];
        for (var y = 0; y < Sam3InputSize; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < Sam3InputSize; x++)
            {
                var color = resized.GetPixel(x, y); var i = y * Sam3InputSize + x;
                pixels[i] = color.Red / 127.5f - 1;
                pixels[plane + i] = color.Green / 127.5f - 1;
                pixels[2 * plane + i] = color.Blue / 127.5f - 1;
            }
        }
        return pixels;
    }

    /// <summary>在共享会话锁内编码；复制特征后释放原生输出，图片缓存与模型版本绑定。</summary>
    private SamImageContext EncodeSam3(SKBitmap original, SamInstalledModel installed, int? gpuId, CancellationToken token)
    {
        using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
        using var output = _encoder!.Run([NamedOnnxValue.CreateFromTensor("pixel_values",
            new DenseTensor<float>(Sam3Pixels(original, token), [1, 3, Sam3InputSize, Sam3InputSize]))], Sam3Features, run);
        token.ThrowIfCancellationRequested();
        for (var i = 0; i < Sam3Features.Length; i++)
        {
            var tensor = output.First(o => o.Name == Sam3Features[i]).AsTensor<float>();
            if (!tensor.Dimensions.SequenceEqual(Sam3FeatureShapes[i]) || tensor.Any(v => !float.IsFinite(v)))
            { throw new InvalidDataException("SAM 3 图片特征尺寸或数值无效。"); }
        }
        return new SamImageContext
        {
            Installed = installed, ModelKind = SamModelKind.Sam3, GpuId = gpuId,
            Width = original.Width, Height = original.Height, WorkWidth = Sam3InputSize, WorkHeight = Sam3InputSize,
            HighRes0 = output.First(o => o.Name == Sam3Features[0]).AsTensor<float>().ToArray(),
            HighRes1 = output.First(o => o.Name == Sam3Features[1]).AsTensor<float>().ToArray(),
            Embedding = output.First(o => o.Name == Sam3Features[2]).AsTensor<float>().ToArray()
        };
    }

    /// <summary>将原图点坐标分别缩放；标签是 int64，不能复用旧模型的浮点标签。</summary>
    internal static (float[] Coordinates, long[] Labels) Sam3Points(int width, int height, IReadOnlyList<SamPrompt> prompts)
    {
        var coordinates = new float[checked(prompts.Count * 2)]; var labels = new long[prompts.Count];
        for (var i = 0; i < prompts.Count; i++)
        {
            coordinates[2 * i] = (float)(prompts[i].X * Sam3InputSize / width);
            coordinates[2 * i + 1] = (float)(prompts[i].Y * Sam3InputSize / height);
            labels[i] = prompts[i].Include ? 1 : 0;
        }
        return (coordinates, labels);
    }

    /// <summary>只传点提示，框张量必须为空；虚构一个零矩形会改变模型的分割语义。</summary>
    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> DecodeSam3(SamImageContext image, IReadOnlyList<SamPrompt> prompts, RunOptions run)
    {
        var points = Sam3Points(image.Width, image.Height, prompts);
        return _decoder!.Run([
            NamedOnnxValue.CreateFromTensor("input_points", new DenseTensor<float>(points.Coordinates, [1, 1, prompts.Count, 2])),
            NamedOnnxValue.CreateFromTensor("input_labels", new DenseTensor<long>(points.Labels, [1, 1, prompts.Count])),
            NamedOnnxValue.CreateFromTensor("input_boxes", new DenseTensor<float>(Array.Empty<float>(), [1, 0, 4])),
            NamedOnnxValue.CreateFromTensor(Sam3Features[0], new DenseTensor<float>(image.HighRes0, Sam3FeatureShapes[0])),
            NamedOnnxValue.CreateFromTensor(Sam3Features[1], new DenseTensor<float>(image.HighRes1, Sam3FeatureShapes[1])),
            NamedOnnxValue.CreateFromTensor(Sam3Features[2], new DenseTensor<float>(image.Embedding, Sam3FeatureShapes[2]))],
            ["pred_masks", "iou_scores"], run);
    }

    /// <summary>从满足全部提示点的候选中选择最高预测 IoU，保留首点对应的连通物体。</summary>
    private SamResult SegmentSam3(SamImageContext image, IReadOnlyList<SamPrompt> prompts, CancellationToken token)
    {
        using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
        using var output = DecodeSam3(image, prompts, run); token.ThrowIfCancellationRequested();
        var masks = output.First(o => o.Name == "pred_masks").AsTensor<float>();
        var scores = output.First(o => o.Name == "iou_scores").AsTensor<float>();
        if (masks.Rank != 5 || masks.Dimensions[0] != 1 || masks.Dimensions[1] != 1 || masks.Dimensions[2] != 3 ||
            masks.Dimensions[3] <= 0 || masks.Dimensions[4] <= 0 || !scores.Dimensions.SequenceEqual(new[] { 1, 1, 3 }) ||
            masks.Any(v => !float.IsFinite(v)) || scores.Any(v => !float.IsFinite(v)))
        { throw new InvalidDataException("SAM 3 解码结果尺寸或数值无效。"); }
        var data = ResizeMasks(masks.ToArray(), masks.Dimensions[4], masks.Dimensions[3], Sam3InputSize, Sam3InputSize, 3, token);
        var plane = Sam3InputSize * Sam3InputSize; var best = -1; var quality = float.NegativeInfinity;
        for (var m = 0; m < 3; m++)
        {
            var matches = prompts.All(p => (data[m * plane + Math.Min(Sam3InputSize - 1, (int)(p.Y * Sam3InputSize / image.Height)) * Sam3InputSize
                + Math.Min(Sam3InputSize - 1, (int)(p.X * Sam3InputSize / image.Width))] > 0) == p.Include);
            if (matches && scores[0, 0, m] > quality) { best = m; quality = scores[0, 0, m]; }
        }
        if (best < 0) { throw new InvalidOperationException("SAM 3 未找到满足提示点的物体，请调整前景或背景点。"); }
        var binary = new byte[plane];
        for (var i = 0; i < plane; i++) { binary[i] = data[best * plane + i] > 0 ? (byte)255 : (byte)0; }
        var result = SamMaskGeometry.Create(binary, Sam3InputSize, Sam3InputSize, image.Width, image.Height,
            (int)(prompts[0].X * Sam3InputSize / image.Width), (int)(prompts[0].Y * Sam3InputSize / image.Height), quality, token);
        if (prompts.Any(p => (result.Mask[(int)p.Y * image.Width + (int)p.X] != 0) != p.Include))
        { throw new InvalidOperationException("SAM 3 提示点不属于同一个连通物体，请分开标注。"); }
        return result;
    }

    /// <summary>创建会话时检查精确输入类型、秩与固定维度，不接收 PCS 文本/概念模型。</summary>
    private static void ValidateSam3Contract(InferenceSession encoder, InferenceSession decoder)
    {
        static bool Has(IReadOnlyDictionary<string, NodeMetadata> nodes, string name, Type type, params int[] shape)
            => nodes.TryGetValue(name, out var node) && node.IsTensor && node.ElementType == type && node.Dimensions.Length == shape.Length
                && node.Dimensions.Zip(shape).All(d => d.Second < 0 || d.First == d.Second);
        if (!Has(encoder.InputMetadata, "pixel_values", typeof(float), -1, 3, Sam3InputSize, Sam3InputSize) ||
            !Has(decoder.InputMetadata, "input_points", typeof(float), -1, 1, -1, 2) ||
            !Has(decoder.InputMetadata, "input_labels", typeof(long), -1, 1, -1) ||
            !Has(decoder.InputMetadata, "input_boxes", typeof(float), -1, -1, 4) ||
            !Has(decoder.OutputMetadata, "pred_masks", typeof(float), -1, -1, -1, -1, -1) ||
            !Has(decoder.OutputMetadata, "iou_scores", typeof(float), -1, -1, 3) ||
            Sam3Features.Where((name, i) => !Has(encoder.OutputMetadata, name, typeof(float), -1, -1, -1, -1)
                || !Has(decoder.InputMetadata, name, typeof(float), [-1, .. Sam3FeatureShapes[i].Skip(1)])).Any())
        { throw new InvalidDataException("SAM 3 ONNX 点选模型接口不匹配，版本未切换。"); }
    }

    /// <summary>升级时在目标设备实际执行编码和解码；合成图片只验证算子，不评价语义准确率。</summary>
    private void ValidateSam3Execution(CancellationToken token)
    {
        using var bitmap = new SKBitmap(Sam3InputSize, Sam3InputSize); bitmap.Erase(SKColors.Black);
        var image = EncodeSam3(bitmap, _loadedModel!, _loadedGpu, token);
        using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
        using var output = DecodeSam3(image, [new SamPrompt(504, 504)], run); token.ThrowIfCancellationRequested();
        if (output.Any(o => o.AsTensor<float>().Length == 0 || o.AsTensor<float>().Any(v => !float.IsFinite(v))))
        { throw new InvalidDataException("SAM 3 版本试运行输出无效，版本未切换。"); }
    }
}
