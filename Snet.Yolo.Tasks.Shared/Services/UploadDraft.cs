namespace Snet.Yolo.Tasks.Services;

/// <summary>确认前可编辑的草稿参数（分类名称、模型信息）。</summary>
public sealed class UploadDraft
{
    /// <summary>分类导入使用的类别名称。</summary>
    public string ClassName { get; set; } = string.Empty;
    /// <summary>ONNX 模型显示名称。</summary>
    public string ModelName { get; set; } = string.Empty;
    /// <summary>ONNX 模型描述。</summary>
    public string ModelDescribe { get; set; } = string.Empty;
    /// <summary>ONNX 模型类型（OnnxType 名称）。</summary>
    public string ModelType { get; set; } = "ObjectDetection";
}
