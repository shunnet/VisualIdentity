using SkiaSharp;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 姿态结果
    /// </summary>
    public class PoseEstimationResultData : TrackingInfo, IDetection
    {
        public LabelModel Label { get; init; } = new LabelModel();
        public double Confidence { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public SKRectI BoundingBox { get; init; }
        public string Position { get; init; } = string.Empty;
        public KeyPoint[] KeyPoints { get; set; } = Array.Empty<KeyPoint>();

    }
}
