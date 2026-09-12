using Snet.Yolo.Server.@interface;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 定向检测
    /// </summary>
    public class ObbDetectionData : IData
    {
        /// <summary>创建使用默认阈值的定向检测输入。</summary>
        public ObbDetectionData() { }
        /// <summary>创建指定图片与阈值的定向检测输入。</summary>
        /// <param name="file">待识别图片字节。</param>
        /// <param name="confidence">最低置信度。</param>
        /// <param name="iou">交并比阈值。</param>
        public ObbDetectionData(byte[] file, double confidence = 0.2, double iou = 0.7)
        {
            this.File = file;
            this.Confidence = confidence;
            this.Iou = iou;
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
    }
}
