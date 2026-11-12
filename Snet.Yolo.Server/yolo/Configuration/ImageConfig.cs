// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2024-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Models;
namespace Snet.Yolo.Server.yolo.Configuration
{
    internal static class ImageConfig
    {
        public const float FONT_SIZE = 18;
        public const float KEYPOINT_SIZE = 5;
        public const float SHADOW_OFFSET = 1;
        public const float BORDER_THICKNESS = 2;

        public const float CLASSIFICATION_TRANSPARENT_BOX_X = 50;
        public const float CLASSIFICATION_TRANSPARENT_BOX_Y = 50;
        public const int CLASSIFICATION_BOX_ALPHA = 60;

        public const int DEFAULT_OPACITY = 128;

        public const int SEGMENTATION_MASK_OPACITY = 80;
        public const float SEGMENTATION_PIXEL_THRESHOLD = .68f;

        public const float POSE_KEYPOINT_THRESHOLD = 0.65f;

        // Image size used for GPU memory allocation.
        public const int GPU_IMG_ALLOC_SIZE = 1080;

        public const int SCALING_DENOMINATOR = 1280;

        public static readonly SKColor FontColor = SKColors.White;

        public static readonly SKColor ClassificationBackground
            = new(0, 0, 0, CLASSIFICATION_BOX_ALPHA);

        public static readonly float TailThickness = 4f;
        public static readonly SKColor TailPaintColorStart = new(255, 105, 180);  // #FF69B4 - Blazing Bubblegum Bomber Pink
        public static readonly SKColor TailPaintColorEnd = TailPaintColorStart.WithAlpha(0);

        public static readonly SKColor PoseMarkerColor = new(255, 246, 51, DEFAULT_OPACITY); // #FFF633 - Goblin Torchlight Yellow

        // 推理前把原图缩到模型输入尺寸时的采样方式，必须与 Ultralytics 训练/预测一致：cv2.INTER_LINEAR，即双线性、不做 mipmap、不抗锯齿。
        // 以前这里是 Nearest（最近邻）：在 3024×3024 的真实照片缩到 640（4.7 倍）上，同一个模型的 mAP50-95 0.583 → 0.556、
        // mAP50 0.900 → 0.849、召回率 0.797 → 0.743（应用自己的 C# 推理路径实测，见 docs/plans/2026-11-12-yolo-accuracy.md）。
        // 也不能用 Mipmap/抗锯齿：它比训练时更“平滑”，同样会掉点（mAP50-95 0.565）。
        public static readonly SKSamplingOptions DefaultSamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.None);
        public static readonly SKSamplingOptions SegmentationResamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.None);

        public static readonly SKPaint PaintFill = new()
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        public static readonly SKPaint FontColorPaint = new()
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Color = SKColors.White
        };

        public static readonly SKPaint ClassificationBackgroundPaint = new()
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Color = ClassificationBackground
        };

        public static readonly SKPaint TextShadowPaint = new()
        {
            Color = new SKColor(0, 0, 0, DEFAULT_OPACITY),
            IsAntialias = true
        };

        public static readonly SKPaint EdgeBlur = new()
        {
            ImageFilter = SKImageFilter.CreateBlur(0.8f, 0.8f)
        };

        public static readonly ClassificationDrawingOptions DefaultClassificationDrawingOptions = new();
        public static readonly DetectionDrawingOptions DefaultDetectionDrawingOptions = new();
        public static readonly PoseDrawingOptions DefaultPoseDrawingOptions = new();
        public static readonly SegmentationDrawingOptions DefaultSegmentationDrawingOptions = new();
    }
}
