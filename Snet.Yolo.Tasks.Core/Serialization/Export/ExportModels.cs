namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using System;
using System.Collections.Generic;

/// <summary>单个导出文件（相对路径 + 字节内容）。</summary>
public sealed record ExportFile(string Path, byte[] Content);

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

/// <summary>支持的导出格式（与 Label Studio 一致）。</summary>
public static class ExportFormats
{
    /// <summary>Full JSON export.</summary>
    public const string Json = "JSON";
    /// <summary>Compact JSON export.</summary>
    public const string JsonMin = "JSON_MIN";
    /// <summary>Comma-separated export.</summary>
    public const string Csv = "CSV";
    /// <summary>Tab-separated export.</summary>
    public const string Tsv = "TSV";
    /// <summary>COCO export.</summary>
    public const string Coco = "COCO";
    /// <summary>Pascal VOC XML export.</summary>
    public const string Voc = "Pascal VOC XML";
    /// <summary>YOLO label export.</summary>
    public const string Yolo = "YOLO";
    /// <summary>YOLO labels plus source images.</summary>
    public const string YoloWithImages = "YOLO (with images)";
    /// <summary>CoNLL 2003 export.</summary>
    public const string Conll = "CoNLL2003";
    /// <summary>ASR manifest export.</summary>
    public const string AsrManifest = "ASR_MANIFEST";
    /// <summary>Brush-mask PNG export.</summary>
    public const string BrushPng = "Brush labels to NumPy / PNG";

    /// <summary>导出下拉仅保留 YOLO 与 YOLO 带图片。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Yolo, YoloWithImages,
    };
}
