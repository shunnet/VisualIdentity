namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练数据集统计结果。</summary>
/// <param name="ImageCount">图片总数（含验证集）。</param>
/// <param name="BoxCount">标注框总数（分类任务为图片数）。</param>
/// <param name="ClassNames">类别名称（顺序即类别下标）。</param>
/// <param name="InstancesPerClass">与 ClassNames 同序的每类实例数。</param>
/// <param name="BoxSizesPixels">各标注框换算到训练输入尺寸后的短边像素（仅带框任务）。</param>
/// <param name="ValImageCount">验证集图片数。</param>
/// <param name="HasBoxes">是否为带框任务（图像分类为 false）。</param>
public sealed record DatasetStats(
    int ImageCount,
    int BoxCount,
    IReadOnlyList<string> ClassNames,
    IReadOnlyList<int> InstancesPerClass,
    IReadOnlyList<double> BoxSizesPixels,
    int ValImageCount,
    bool HasBoxes)
{
    /// <summary>没有任何标注的类别数。</summary>
    public int EmptyClasses => InstancesPerClass.Count(count => count == 0);

    /// <summary>实例数偏少（&lt; MinInstancesPerClass）的类别数。</summary>
    public int SparseClasses => InstancesPerClass.Count(count => count is > 0 and < DatasetHealthCheck.MinInstancesPerClass);

    /// <summary>框短边像素中位数（无框时返回 0）。</summary>
    public double MedianBoxPixels => Percentile(BoxSizesPixels, 0.5);

    /// <summary>框短边像素最小值（无框时返回 0）。</summary>
    public double MinBoxPixels => BoxSizesPixels.Count == 0 ? 0 : BoxSizesPixels.Min();

    private static double Percentile(IReadOnlyList<double> values, double q)
    {
        if (values.Count == 0) { return 0; }
        var sorted = values.OrderBy(v => v).ToArray();
        var index = Math.Clamp((int)Math.Round((sorted.Length - 1) * q), 0, sorted.Length - 1);
        return sorted[index];
    }
}
