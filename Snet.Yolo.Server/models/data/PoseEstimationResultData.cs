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
        /// <summary>姿态类别。</summary>
        public LabelModel Label { get; init; } = new LabelModel();
        /// <summary>姿态检测置信度。</summary>
        public double Confidence { get; init; }
        /// <summary>像素坐标边界框，仅用于进程内绘制。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public SKRectI BoundingBox { get; init; }
        /// <summary>可序列化的边界框位置。</summary>
        public string Position { get; init; } = string.Empty;
        /// <summary>按模型顺序排列的姿态关键点。</summary>
        public KeyPoint[] KeyPoints { get; set; } = Array.Empty<KeyPoint>();

    }
}
