namespace Snet.Yolo.Tasks.Core.Serialization.Export;

/// <summary>单个导出文件：相对路径、小型标注字节，或不占用整图内存的惰性读取入口。</summary>
public sealed record ExportFile(string Path, byte[] Content)
{
    /// <summary>原图的惰性读取入口；非空时不使用 Content，由打包器逐个打开并释放文件流。</summary>
    public Func<Stream>? OpenRead { get; init; }
}
