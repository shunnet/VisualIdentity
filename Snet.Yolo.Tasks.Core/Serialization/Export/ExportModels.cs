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
    public const string Json = "JSON";
    public const string JsonMin = "JSON_MIN";
    public const string Csv = "CSV";
    public const string Tsv = "TSV";
    public const string Coco = "COCO";
    public const string Voc = "Pascal VOC XML";
    public const string Yolo = "YOLO";
    public const string YoloWithImages = "YOLO (with images)";
    public const string Conll = "CoNLL2003";
    public const string AsrManifest = "ASR_MANIFEST";
    public const string BrushPng = "Brush labels to NumPy / PNG";

    /// <summary>全部支持格式（展示顺序）。</summary>
    /// <summary>导出下拉仅保留 YOLO 与 YOLO 带图片。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Yolo, YoloWithImages,
    };
}
