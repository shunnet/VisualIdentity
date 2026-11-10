using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>YOLO ONNX 导出结果的复用判定：被中断或截断的导出不能被当成成功结果。</summary>
public sealed class YoloOnnxExportCacheTests : IDisposable
{
    /// <summary>每个测试独立的临时目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "onnx-cache-" + Guid.NewGuid().ToString("N"));

    /// <summary>权重文件路径（比 ONNX 旧一分钟）。</summary>
    private readonly string _best;

    /// <summary>ONNX 文件路径。</summary>
    private readonly string _onnx;

    /// <summary>创建目录与权重文件。</summary>
    public YoloOnnxExportCacheTests()
    {
        Directory.CreateDirectory(_directory);
        _best = Path.Combine(_directory, "best.pt");
        _onnx = Path.Combine(_directory, "best.onnx");
        File.WriteAllBytes(_best, [1]);
        File.SetLastWriteTimeUtc(_best, DateTime.UtcNow.AddMinutes(-1));
    }

    /// <summary>删除临时目录。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>没有完成标记的 ONNX（导出被中断后遗留的文件）不能被复用。</summary>
    [Fact]
    public void IsExportComplete_WithoutMarker_IsFalse()
    {
        File.WriteAllBytes(_onnx, new byte[100]);
        Assert.False(TrainingService.IsExportComplete(_best, _onnx));
    }

    /// <summary>标记记录的大小与文件一致、文件不旧于权重时才复用；之后文件被截断则不再复用。</summary>
    [Fact]
    public void IsExportComplete_RequiresMatchingSizeAndFreshness()
    {
        File.WriteAllBytes(_onnx, new byte[100]);
        File.WriteAllText(TrainingService.CompletionMarker(_onnx), "100");
        Assert.True(TrainingService.IsExportComplete(_best, _onnx));

        File.WriteAllBytes(_onnx, new byte[40]);
        Assert.False(TrainingService.IsExportComplete(_best, _onnx));

        File.WriteAllBytes(_onnx, new byte[100]);
        File.SetLastWriteTimeUtc(_best, DateTime.UtcNow.AddMinutes(1));
        Assert.False(TrainingService.IsExportComplete(_best, _onnx));
    }

    /// <summary>清理会同时删除 ONNX 与完成标记，文件不存在时不抛异常。</summary>
    [Fact]
    public void DeleteExport_RemovesBothFiles()
    {
        File.WriteAllBytes(_onnx, new byte[10]);
        File.WriteAllText(TrainingService.CompletionMarker(_onnx), "10");

        TrainingService.DeleteExport(_onnx);
        TrainingService.DeleteExport(_onnx);

        Assert.False(File.Exists(_onnx));
        Assert.False(File.Exists(TrainingService.CompletionMarker(_onnx)));
    }
}
