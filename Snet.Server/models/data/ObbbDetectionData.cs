using Snet.Server.@interface;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 定向检测
    /// </summary>
    public class ObbDetectionData : IData
    {
        public ObbDetectionData() { }
        public ObbDetectionData(byte[] file, double confidence = 0.2, double iou = 0.7)
        {
            this.File = file;
            this.Confidence = confidence;
            this.Iou = iou;
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
    }
}
