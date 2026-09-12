using SkiaSharp;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 检测
    /// </summary>
    public class ObjectDetectionResultData : TrackingInfo, IDetection
    {
        /// <summary>检测类别。</summary>
        public LabelModel Label { get; init; } = new LabelModel();
        /// <summary>检测置信度。</summary>
        public double Confidence { get; init; }
        /// <summary>像素坐标边界框，仅用于进程内绘制。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public SKRectI BoundingBox { get; init; }
        /// <summary>可序列化的边界框位置。</summary>
        public string Position { get; init; } = string.Empty;
    }

}
