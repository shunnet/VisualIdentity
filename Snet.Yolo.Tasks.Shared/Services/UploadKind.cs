namespace Snet.Yolo.Tasks.Services;

/// <summary>上传任务的业务类型。</summary>
public enum UploadKind
{
    /// <summary>向普通工程批量导入图片。</summary>
    ProjectImages,
    /// <summary>向 Anomalib 工程批量导入正常图片。</summary>
    AnomalibProjectImages,
    /// <summary>向分类工程按类别导入图片。</summary>
    ProjectClassImages,
    /// <summary>导入“YOLO (with images)”ZIP 数据集。</summary>
    ProjectYoloArchive,
    /// <summary>向验证页导入待识别图片或视频。</summary>
    ValidationImages,
    /// <summary>按模型导入 Anomalib 验证图片或视频。</summary>
    AnomalibValidationImages,
    /// <summary>导入联合验证图片。</summary>
    JointValidationImages,
    /// <summary>向验证页导入 ONNX 模型。</summary>
    ValidationModel,
}
