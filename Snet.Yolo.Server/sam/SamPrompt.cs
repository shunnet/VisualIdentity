namespace Snet.Yolo.Server.sam;

/// <summary>原图像素坐标的交互点；Include=false 表示排除该位置。</summary>
/// <param name="X">横坐标。</param><param name="Y">纵坐标。</param><param name="Include">是否包含。</param>
public sealed record SamPrompt(double X, double Y, bool Include = true);
