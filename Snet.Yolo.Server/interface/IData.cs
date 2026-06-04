namespace Snet.Yolo.Server.@interface
{
    /// <summary>
    /// 统一数据接口 — marker interface for type-safe dispatch in inference pipeline.
    /// All inference data types (ClassificationData, ObjectDetectionData, etc.) implement this.
    /// </summary>
    public interface IData
    {
        /// <summary>
        /// Raw image bytes for inference.
        /// </summary>
        byte[] File { get; set; }
    }
}
