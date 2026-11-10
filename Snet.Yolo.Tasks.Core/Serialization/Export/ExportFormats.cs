namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using System.Collections.Generic;

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
