using System.Text.RegularExpressions;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>按数据集与显卡算出的训练推荐值。</summary>
/// <param name="ImageSize">推荐输入尺寸（imgsz，32 的倍数）。</param>
/// <param name="ModelScale">推荐模型规格：<c>n</c>、<c>s</c> 或 <c>m</c>（l / x 只作为用户自己的选择）。</param>
/// <param name="Epochs">推荐训练轮数（100 的倍数，与弹窗里的滑块步长一致）。</param>
/// <param name="UseValidation">是否推荐使用验证集（自动划分 10%）。</param>
/// <param name="Reasons">各项推荐值的依据。</param>
public sealed record YoloRecommendation(
    int ImageSize,
    string ModelScale,
    int Epochs,
    bool UseValidation,
    IReadOnlyList<YoloAdviceItem> Reasons)
{
    private static readonly Regex ModelNamePattern = new(@"^(yolo\d+)([nsmlx])(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>把推荐的规格套到当前模型名上：保留系列与任务后缀，例如 <c>yolo26n-seg.pt</c> + <c>s</c> → <c>yolo26s-seg.pt</c>。</summary>
    /// <param name="currentModel">当前选中的模型文件名。</param>
    /// <returns>推荐规格对应的模型文件名；无法识别当前名称时原样返回。</returns>
    public string ModelFor(string currentModel)
    {
        var match = ModelNamePattern.Match(currentModel);
        return match.Success ? match.Groups[1].Value + ModelScale + match.Groups[3].Value : currentModel;
    }

    /// <summary>当前选项是否已经等于推荐值。</summary>
    /// <param name="options">弹窗里的训练选项。</param>
    public bool Matches(TrainingOptions options)
        => options.ImgSize == ImageSize
           && options.Epochs == Epochs
           && options.UseVal == UseValidation
           && string.Equals(ModelFor(options.Model), options.Model, StringComparison.OrdinalIgnoreCase);
}
