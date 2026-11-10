namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using System;
using System.Collections.Generic;

/// <summary>一次导出结果。</summary>
public sealed class ExportResult
{
    /// <summary>文件名（zip 时为压缩包名）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>是否打包为 zip。</summary>
    public bool IsZip { get; init; }

    /// <summary>文件清单（单文件时默认不 zip）。</summary>
    public IReadOnlyList<ExportFile> Files { get; init; } = Array.Empty<ExportFile>();
}
