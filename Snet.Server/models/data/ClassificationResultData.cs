using YoloDotNet.Models.Interfaces;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 分类结果
    /// </summary>
    public class ClassificationResultData : IClassification
    {
        public string Label { get; set; }
        public double Confidence { get; set; }
    }
}
