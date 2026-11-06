namespace Snet.Yolo.Server.anomalib;

/// <summary>模型训练和推理共同约定的图像插值方式。</summary>
public enum AnomalibInterpolation
{
    /// <summary>历史模型使用的普通双线性采样。</summary>
    Bilinear,
    /// <summary>RGB 浮点双线性抗锯齿缩放，与 torchvision 张量训练输入一致。</summary>
    BilinearAntialias,
}
