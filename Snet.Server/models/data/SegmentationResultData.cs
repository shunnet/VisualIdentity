using SkiaSharp;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 分割结果
    /// </summary>
    public class SegmentationResultData : TrackingInfo, IDetection
    {
        public LabelModel Label { get; init; } = new LabelModel();
        public double Confidence { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public SKRectI BoundingBox { get; init; }
        public string Position => BoundingBox.ToString();
        public byte[] BitPackedPixelMask { get; set; } = Array.Empty<byte>();

    }
}
