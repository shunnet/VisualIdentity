using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Snet.Yolo.Server.sam;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class SamTests
{
    [Fact]
    public void Geometry_RetainsConcavityHoleAndSeededComponent()
    {
        var mask = new byte[30 * 30];
        for (var y = 3; y < 24; y++) { for (var x = 3; x < 24; x++) { if (x < 10 || y > 16) { mask[y * 30 + x] = 255; } } }
        mask[20 * 30 + 6] = 0; mask[1] = 255;
        var result = SamMaskGeometry.Create(mask, 30, 30, 60, 60, 5, 5, .9f);
        Assert.Equal(6, result.X); Assert.Equal(6, result.Y); Assert.Equal(42, result.BoxWidth);
        Assert.Equal(0, result.Mask[40 * 60 + 12]); Assert.Equal(0, result.Mask[2]);
        Assert.Equal(255, result.Mask[10 * 60 + 10]);
        Assert.True(result.PointsX.Length >= 6); Assert.Contains(result.PointsX, x => x == 20);
        Assert.StartsWith("data:image/png;base64,", result.PreviewDataUrl);
    }

    [Fact]
    public void Geometry_RejectsInvalidPromptAndCancellation()
    {
        Assert.Throws<ArgumentException>(() => SamMaskGeometry.Create(new byte[9], 3, 3, 3, 3, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => SamMaskGeometry.Create([255], 1, 1, 3, 3, 1, 0, 0));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SamMaskGeometry.Create([255], 1, 1, 3, 3, 0, 0, 0, cts.Token));
    }

    [Fact]
    public void FilledBrush_PersistsRlePreviewOutlineAndUndoRedo()
    {
        const string config = "<View><Image name='image' value='$image'/><BrushLabels name='brush' toName='image'><Label value='object'/></BrushLabels></View>";
        var task = new AnnotationTask(); var session = new LabelingSession(config, task); session.SetImageOriginalSize(30, 30);
        var mask = new byte[900]; for (var y = 2; y < 20; y++) { for (var x = 2; x < 20; x++) { mask[y * 30 + x] = 255; } } mask[10 * 30 + 10] = 0;
        var result = SamMaskGeometry.Create(mask, 30, 30, 30, 30, 3, 3, .8f);
        var row = session.AddFilledBrushMask(result.Mask, result.PointsX, result.PointsY, result.PreviewDataUrl, "object");
        var rle = row.Value!["rle"]!.AsArray().Select(n => (byte)n!.GetValue<int>()).ToArray();
        Assert.Equal(mask, RleCodec.Decode(rle, 900));
        var saved = TaskJson.SerializeTask(task); session.MoveShape(row.Id!, 3, 3); Assert.Equal(saved, TaskJson.SerializeTask(task));
        var restored = new LabelingSession(config, TaskJson.DeserializeTask(saved)!); restored.SetImageOriginalSize(30, 30);
        Assert.Equal(result.PreviewDataUrl, restored.BuildRegionViews()[0].MaskDataUrl);
        Assert.NotEmpty(YoloLabelExporter.Build(task, YoloTaskType.Segment, ["object"]));
        session.Undo(); Assert.Empty(session.BuildRegionViews()); session.Redo(); Assert.Single(session.BuildRegionViews());
    }

    [Fact]
    public async Task ModelStore_InvalidExistingFileIsNeverOverwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "sam-test-" + Guid.NewGuid().ToString("N"));
        var store = new SamModelStore(root); Directory.CreateDirectory(store.DirectoryPath);
        try
        {
            await File.WriteAllTextAsync(store.EncoderPath, "invalid model");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureReadyAsync());
            Assert.Equal("invalid model", await File.ReadAllTextAsync(store.EncoderPath));
            Assert.Single(Directory.GetFiles(store.DirectoryPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ModelStore_CancelledBeforeStartDoesNotCreateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "sam-test-" + Guid.NewGuid().ToString("N"));
        var store = new SamModelStore(root); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "sam"), new SamModelStore().DirectoryPath);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsureReadyAsync(cancellationToken: cancelled.Token));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(SamModelKind.MobileSam)]
    [InlineData(SamModelKind.Sam21Tiny)]
    [InlineData(SamModelKind.SamVitB)]
    [InlineData(SamModelKind.SamVitL)]
    [InlineData(SamModelKind.SamVitH)]
    public async Task Runtime_RealPinnedModelsCpuSmoke(SamModelKind kind)
    {
        // 显式设置此环境变量才使用离线权重，日常测试绝不联网下载。
        var root = Environment.GetEnvironmentVariable("SAM_TEST_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { return; }
        using var runtime = new SamOnnxRuntime(new SamModelStore(root), () => new SessionOptions());
        await runtime.PrepareAsync(kind);
        var path = Path.Combine(root, "smoke.png");
        using (var bitmap = new SKBitmap(320, 240))
        {
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true }; canvas.DrawCircle(160, 120, 60, paint);
            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); png.SaveTo(file);
        }
        var context = await runtime.EncodeAsync(path, kind);
        Assert.Equal(kind, context.ModelKind);
        var result = await runtime.SegmentAsync(context, [new(160, 120)]);
        Assert.Equal(320, result.Width); Assert.Equal(240, result.Height); Assert.Equal(255, result.Mask[120 * 320 + 160]);
        Assert.Equal(0, result.Mask[0]); Assert.True(result.PointsX.Length >= 3); Assert.InRange(result.BoxWidth, 50, 200);
        var refined = await runtime.SegmentAsync(context, [new(160, 120), new(0, 0, false)]);
        Assert.Equal(0, refined.Mask[0]);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.SegmentAsync(context, [new(160, 120)], cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.EncodeAsync(path, cancelled.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.SegmentAsync(context, [new(-1, 0)]));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.SegmentAsync(context, [new(5, 5, false)]));
    }

    [Fact]
    public void ModelCatalog_PathsAreIsolatedAndUnsupportedKindsRejected()
    {
        var store = new SamModelStore();
        Assert.Equal(5, SamModels.All.Count);
        Assert.Equal(5, SamModels.All.Select(m => store.GetEncoderPath(m.Kind)).Distinct().Count());
        Assert.Equal(45, SamModels.Get(SamModelKind.MobileSam).ModelMegabytes);
        Assert.Equal(126, SamModels.Get(SamModelKind.Sam21Tiny).ModelMegabytes);
        Assert.Equal(376, SamModels.Get(SamModelKind.SamVitB).ModelMegabytes);
        Assert.Equal(1251, SamModels.Get(SamModelKind.SamVitL).ModelMegabytes);
        Assert.Equal(2567, SamModels.Get(SamModelKind.SamVitH).ModelMegabytes);
        Assert.Equal("sam_vit_h_4b8939.encoder_data.bin", SamModels.Get(SamModelKind.SamVitH).EncoderData);
        Assert.Equal(0, (int)SamModelKind.MobileSam); Assert.Equal(2, (int)SamModelKind.SamVitB); // 已保存的浏览器偏好编号不变。
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetEncoderPath((SamModelKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => SamModels.Get((SamModelKind)999));
    }

    [Theory]
    [InlineData(SamModelKind.Sam21Tiny, "sam2-tiny.zip")]
    [InlineData(SamModelKind.SamVitB, "sam-vit-b.zip")]
    [InlineData(SamModelKind.SamVitL, "sam-vit-l.zip")]
    [InlineData(SamModelKind.SamVitH, "sam-vit-h.zip")]
    public async Task ModelStore_OfflineArchiveIsVerifiedExtractedAndPreserved(SamModelKind kind, string archiveFile)
    {
        var source = Environment.GetEnvironmentVariable("SAM_TEST_ARCHIVE_ROOT");
        if (string.IsNullOrWhiteSpace(source)) { return; }
        var root = Path.Combine(Path.GetTempPath(), "sam-archive-test-" + Guid.NewGuid().ToString("N"));
        var store = new SamModelStore(root); var definition = SamModels.Get(kind);
        var archive = Path.Combine(store.DirectoryPath, definition.Folder, definition.Archive!);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            File.Copy(Path.Combine(source, archiveFile), archive);
            await store.EnsureReadyAsync(kind);
            Assert.Equal(definition.EncoderBytes, new FileInfo(store.GetEncoderPath(kind)).Length);
            Assert.Equal(definition.DecoderBytes, new FileInfo(store.GetDecoderPath(kind)).Length);
            Assert.True(File.Exists(archive)); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(archive)!, "*.partial"));
            await new SamModelStore(root).EnsureReadyAsync(kind); // 重启后仍能从完整 ONNX 验证，不重复解压。
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(archive)!, "config.yaml")));
            if (definition.EncoderData is not null)
            {
                var data = Path.Combine(Path.GetDirectoryName(archive)!, definition.EncoderData);
                Assert.Equal(definition.EncoderDataBytes, new FileInfo(data).Length);
                File.Delete(data); // 仅删除本测试创建的临时目录内文件，验证缺失外部权重的修复。
                await new SamModelStore(root).EnsureReadyAsync(kind);
                Assert.Equal(definition.EncoderDataBytes, new FileInfo(data).Length);
                await File.WriteAllTextAsync(data, "corrupt");
                await Assert.ThrowsAsync<InvalidDataException>(() => new SamModelStore(root).EnsureReadyAsync(kind));
                Assert.Equal("corrupt", await File.ReadAllTextAsync(data));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ModelStore_InvalidArchiveDoesNotPublishOnnx()
    {
        var root = Path.Combine(Path.GetTempPath(), "sam-archive-test-" + Guid.NewGuid().ToString("N"));
        var store = new SamModelStore(root); var definition = SamModels.Get(SamModelKind.Sam21Tiny);
        var archive = Path.Combine(store.DirectoryPath, definition.Folder, definition.Archive!);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            await File.WriteAllTextAsync(archive, "not a valid archive");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureReadyAsync(definition.Kind));
            Assert.False(File.Exists(store.GetEncoderPath(definition.Kind))); Assert.False(File.Exists(store.GetDecoderPath(definition.Kind)));
            Assert.Equal("not a valid archive", await File.ReadAllTextAsync(archive));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Runtime_SwitchModelsAndReuseMatchingCachedFeatures()
    {
        var root = Environment.GetEnvironmentVariable("SAM_TEST_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { return; }
        using var runtime = new SamOnnxRuntime(new SamModelStore(root), () => new SessionOptions());
        var path = Path.Combine(root, "smoke.png");
        // 基础真实模型测试生成的图片不能成为本测试的顺序依赖。
        using (var bitmap = new SKBitmap(320, 240))
        {
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White); using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawCircle(160, 120, 60, paint); using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(path); png.SaveTo(output);
        }
        var mobile = await runtime.EncodeAsync(path, SamModelKind.MobileSam);
        var tiny = await runtime.EncodeAsync(path, SamModelKind.Sam21Tiny);
        var a = await runtime.SegmentAsync(mobile, [new(160, 120)]); // 新会话已切换，旧的 MobileSAM 特征仍必须走匹配模型。
        var b = await runtime.SegmentAsync(tiny, [new(160, 120)]);
        Assert.Equal(255, a.Mask[120 * 320 + 160]); Assert.Equal(255, b.Mask[120 * 320 + 160]);
        Assert.Equal(0, a.Mask[0]); Assert.Equal(0, b.Mask[0]);
    }

    [Fact]
    public async Task Runtime_RejectsUnsupportedOrInvalidGpuBeforeDownloading()
    {
        using var runtime = new SamOnnxRuntime(new SamModelStore(), () => throw new InvalidOperationException("Factory must not run"));
        Assert.False(runtime.SupportsCuda);
        await Assert.ThrowsAsync<NotSupportedException>(() => runtime.EncodeAsync("missing.png", SamModelKind.MobileSam, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runtime.EncodeAsync("missing.png", SamModelKind.MobileSam, -1));
    }

    [Fact]
    public async Task Runtime_RoutesDeviceAndReloadsSessionsWithoutMixingRequests()
    {
        var root = Environment.GetEnvironmentVariable("SAM_TEST_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { return; }
        var devices = new List<int?>();
        // 用真实 CPU 模型验证设备路由，不将测试工厂伪装成真实 CUDA 验证。
        using var runtime = new SamOnnxRuntime(new SamModelStore(root), id =>
        {
            devices.Add(id);
            if (id == 7) { throw new InvalidOperationException("CUDA initialization failed"); }
            return new SessionOptions();
        }, true);
        var path = Path.Combine(root, "device-smoke.png");
        using (var bitmap = new SKBitmap(320, 240))
        {
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White); using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawCircle(160, 120, 60, paint); using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(path); png.SaveTo(output);
        }
        var gpu3 = await runtime.EncodeAsync(path, SamModelKind.MobileSam, 3);
        Assert.Equal(3, gpu3.GpuId); Assert.Equal(new int?[] { 3, 3 }, devices);
        await runtime.SegmentAsync(gpu3, [new(160, 120)]);
        Assert.Equal(2, devices.Count);
        var cpu = await runtime.EncodeAsync(path, SamModelKind.MobileSam);
        Assert.Null(cpu.GpuId);
        await runtime.SegmentAsync(gpu3, [new(160, 120)]);
        Assert.Equal(new int?[] { 3, 3, null, null, 3, 3 }, devices);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.EncodeAsync(path, SamModelKind.MobileSam, 7));
        Assert.Equal(new int?[] { 3, 3, null, null, 3, 3, 7 }, devices); // 失败不能再创建 CPU 会话。
        await runtime.SegmentAsync(gpu3, [new(160, 120)]);
        Assert.Equal(new int?[] { 3, 3, null, null, 3, 3, 7, 3, 3 }, devices);
    }
}
