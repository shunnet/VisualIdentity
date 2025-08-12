using System.ComponentModel;

namespace Snet.Server.models.@enum
{
    /// <summary>
    /// 识别类型<br/>
    /// https://docs.ultralytics.com/zh/tasks/#detection
    /// </summary>
    public enum OnnxType
    {
        /// <summary>
        /// 对象检测<br/>
        /// 检测是YOLO支持的主要任务。它包括识别图像或视频帧中的对象，并在其周围绘制边界框。检测到的对象根据其特征被分类为不同的类别。YOLO可以以高精度和速度检测单个图像或视频帧中的多个对象，使其成为监控系统和自动驾驶汽车等实时应用的理想选择。
        /// </summary>
        [Description("对象检测")]
        ObjectDetection,
        /// <summary>
        /// 图像分割<br/>
        /// 分割通过将图像分割成基于内容的不同区域，进一步推进了对象检测。每个区域都被分配一个标签，为医学成像、农业分析和制造业质量控制等应用提供像素级精度。YOLO 实现了 U-Net 架构的变体，以执行高效而准确的分割。
        /// </summary>
        [Description("图像分割")]
        Segmentation,
        /// <summary>
        /// 分类<br/>
        /// 分类涉及根据图像的内容对整个图像进行分类。YOLO 的分类功能利用 EfficientNet 架构的变体来提供高性能的图像分类。此任务对于电子商务中的 产品分类 、内容审核 和 野生动物监测 等应用至关重要。
        /// </summary>
        [Description("分类")]
        Classification,
        /// <summary>
        /// 姿态估计<br/>
        /// 姿态估计检测图像或视频帧中的特定关键点，以跟踪运动或估计姿势。这些关键点可以代表人体的关节、面部特征或其他重要的兴趣点。YOLO 在关键点检测方面表现出色，具有高精度和高速度，使其在 健身应用、运动分析 和 人机交互 领域具有重要价值。
        /// </summary>
        [Description("姿态估计")]
        PoseEstimation,
        /// <summary>
        /// 定向检测<br/>
        /// 定向边界框（OBB）检测通过增加一个方向角来更好地定位旋转对象，从而增强了传统的对象检测。这种能力对于航空图像分析、文档处理和工业应用尤其有价值，在这些应用中，对象以各种角度出现。YOLO 在各种场景中为检测旋转对象提供高精度和速度。
        /// </summary>
        [Description("定向检测")]
        ObbDetection,
    }
}
