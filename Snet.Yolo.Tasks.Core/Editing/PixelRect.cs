namespace Snet.Yolo.Tasks.Core.Editing;

/// <summary>图像像素空间矩形（编辑几何单位，与视口变换同构；序列化转百分数）。</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height);
