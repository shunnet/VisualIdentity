namespace Snet.Yolo.Tasks.Services;

/// <summary>等待用户确认的文件选择（分类导入、ONNX 模型导入）。</summary>
public sealed record UploadSelection(UploadKind Kind, string ScopeKey, IReadOnlyList<UploadFileInfo> Files);
