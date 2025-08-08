using Snet.Identify.@interface;

namespace Snet.Identify.models.data
{
    /// <summary>
    /// 分割
    /// </summary>
    public class SegmentationData : IData
    {
        public SegmentationData() { }
        public SegmentationData(byte[] file, double confidence = 0.2, double pixelConfedence = 0.65, double iou = 0.7)
        {
            this.File = file;
            this.Confidence = confidence;
            this.Iou = iou;
            this.PixelConfedence = pixelConfedence;
        }
        /// <summary>
        /// 传进来的图片
        /// </summary>
        public byte[] File { get; set; }
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
        public double PixelConfedence { get; set; } = 0.65;
    }
}
