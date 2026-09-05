using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 分类结果
    /// </summary>
    public class ClassificationResultData : IClassification
    {
        public string Label { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
