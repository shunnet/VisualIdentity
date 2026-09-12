using Snet.Yolo.Server.@interface;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 分割
    /// </summary>
    public class SegmentationData : IData
    {
        /// <summary>创建使用默认阈值的实例分割输入。</summary>
        public SegmentationData() { }
        /// <summary>创建指定图片与阈值的实例分割输入。</summary>
        /// <param name="file">待识别图片字节。</param>
        /// <param name="confidence">最低目标置信度。</param>
        /// <param name="pixelConfedence">最低像素置信度。</param>
        /// <param name="iou">交并比阈值。</param>
        public SegmentationData(byte[] file, double confidence = 0.2, double pixelConfedence = 0.65, double iou = 0.7)
        {
            this.File = file;
            this.Confidence = confidence;
            this.Iou = iou;
            this.PixelConfidence = pixelConfedence;
        }
        /// <summary>
        /// 传进来的图片
        /// </summary>
        public byte[] File { get; set; } = Array.Empty<byte>();
        /// <summary>
        /// 置信度
        /// </summary>
        public double Confidence { get; set; } = 0.2;
        /// <summary>
        /// 交并比
        /// </summary>
        public double Iou { get; set; } = 0.7;
        /// <summary>
        /// 像素置信度
        /// </summary>
        public double PixelConfidence { get; set; } = 0.65;
    }
}
