using SkiaSharp;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 定向检测
    /// </summary>
    public class ObbDetectionResultData : TrackingInfo, IDetection
    {
        public LabelModel Label { get; init; } = new LabelModel();
        public double Confidence { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public SKRectI BoundingBox { get; init; }
        public string Position => BoundingBox.ToString();
        public float OrientationAngle { get; set; }
    }
}
