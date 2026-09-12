using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 分类结果
    /// </summary>
    public class ClassificationResultData : IClassification
    {
        /// <summary>预测类别名称。</summary>
        public string Label { get; set; } = string.Empty;
        /// <summary>预测置信度。</summary>
        public double Confidence { get; set; }
    }
}
