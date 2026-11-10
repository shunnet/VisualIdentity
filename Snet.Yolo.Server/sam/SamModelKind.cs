namespace Snet.Yolo.Server.sam;

/// <summary>图片辅助标注支持的固定模型。</summary>
public enum SamModelKind
{
    /// <summary>轻量 MobileSAM。</summary>
    MobileSam,
    /// <summary>SAM 2.1 Hiera Tiny。</summary>
    Sam21Tiny,
    /// <summary>原版 SAM ViT-B。</summary>
    SamVitB,
    /// <summary>原版 SAM ViT-L，大型编码器。</summary>
    SamVitL,
    /// <summary>原版 SAM ViT-H，编码器使用独立外部权重文件。</summary>
    SamVitH,
    /// <summary>SAM 3 Tracker 图片点选分割；不包含文本概念分割与视频跟踪。</summary>
    Sam3
}
