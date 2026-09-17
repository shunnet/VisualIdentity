using System.Globalization;

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

/// <summary>
/// 训练前数据集"体检"：把注定学不到东西的数据问题在训练开始前就写进日志。
/// 阈值来自 YOLO 系列的实际经验：每类实例太少、目标在训练输入下只有几个像素、
/// 图片总量太少、验证集过小（mAP 无意义）都会表现为"训练成功但识别不到"。
/// </summary>
public static class DatasetHealthCheck
{
    /// <summary>建议的每类最少实例数（低于此值该类别基本学不会）。</summary>
    public const int MinInstancesPerClass = 10;
    /// <summary>建议的最少图片总数。</summary>
    public const int MinImages = 50;
    /// <summary>目标在训练输入尺寸下的短边像素下限：低于此值特征会被下采样抹平。</summary>
    public const double MinBoxPixels = 12d;
    /// <summary>建议的最少验证集图片数：太少时 mAP 基本是噪声。</summary>
    public const int MinValImages = 5;
    /// <summary>图片较少时建议的最少训练轮数。</summary>
    public const int MinEpochsForSmallDataset = 300;

    /// <summary>统计带标签文件的数据集（detect/segment/pose/obb 均按 5 列前缀解析）。</summary>
    /// <param name="labelsDir">训练集 labels 目录。</param>
    /// <param name="valLabelsDir">验证集 labels 目录（可为 null）。</param>
    /// <param name="classes">类别名称（顺序即类别下标）。</param>
    /// <param name="imageSize">训练输入尺寸（imgsz），用于把归一化框宽换算成像素。</param>
    /// <param name="imageCount">图片总数（含验证集）；≤0 时按标签文件数推断。</param>
    public static DatasetStats AnalyzeLabels(string labelsDir, string? valLabelsDir, IReadOnlyList<string> classes, int imageSize, int imageCount = 0)
    {
        var perClass = new int[classes.Count];
        var sizes = new List<double>();
        var labeledImages = 0;
        var valImageCount = 0;
        var boxCount = 0;
        void Read(string? directory, bool isVal)
        {
            if (directory is null || !Directory.Exists(directory)) { return; }
            foreach (var file in Directory.EnumerateFiles(directory, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
            {
                labeledImages++;
                if (isVal) { valImageCount++; }
                foreach (var raw in File.ReadAllLines(file))
                {
                    var fields = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    // 每行 = cls + 至少 4 个归一化数值（segment/pose 后面还有更多列）
                    if (fields.Length < 5) { continue; }
                    if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) { continue; }
                    if (index < 0 || index >= perClass.Length) { continue; }
                    if (!TryDouble(fields[3], out var width) || !TryDouble(fields[4], out var height)) { continue; }
                    perClass[index]++;
                    boxCount++;
                    // 归一化尺寸 × 输入尺寸 = 目标在网络输入上的像素尺寸（letterbox 等比缩放，无需原图分辨率）
                    sizes.Add(Math.Max(0d, Math.Min(width, height)) * imageSize);
                }
            }
        }
        Read(labelsDir, false);
        Read(valLabelsDir, true);
        return new DatasetStats(imageCount > 0 ? imageCount : labeledImages, boxCount, classes, perClass, sizes, valImageCount, HasBoxes: true);
    }

    /// <summary>统计图像分类数据集（每个类别一个目录）。</summary>
    public static DatasetStats AnalyzeClassify(string trainRoot, string? valRoot, IReadOnlyList<string> classes)
    {
        var perClass = new int[classes.Count];
        for (var i = 0; i < classes.Count; i++)
        {
            perClass[i] = CountFiles(Path.Combine(trainRoot, classes[i])) + (valRoot is null ? 0 : CountFiles(Path.Combine(valRoot, classes[i])));
        }
        var total = perClass.Sum();
        // 验证集图片在各类别子目录下，必须递归统计（顶层目录没有图片文件）
        var valImages = valRoot is null ? 0 : CountFilesRecursive(valRoot);
        return new DatasetStats(total, total, classes, perClass, Array.Empty<double>(), valImages, HasBoxes: false);
    }

    /// <summary>生成体检结论（空列表 = 没发现问题）。</summary>
    /// <param name="stats">统计数据。</param>
    /// <param name="imageSize">训练输入尺寸（imgsz）。</param>
    /// <param name="epochs">计划训练轮数。</param>
    /// <param name="useVal">是否使用验证集；用户主动关闭时不再就"验证集为空"告警（那是选择而不是问题）。</param>
    public static IReadOnlyList<string> Warnings(DatasetStats stats, int imageSize, int epochs, bool useVal = true)
    {
        var warnings = new List<string>();
        if (stats.EmptyClasses > 0)
        {
            var names = string.Join("、", stats.ClassNames.Where((_, i) => stats.InstancesPerClass[i] == 0));
            warnings.Add($"这些类别一个标注都没有：{names}。它们永远不可能被识别出来。");
        }
        if (stats.SparseClasses > 0)
        {
            var detail = string.Join("、", stats.ClassNames
                .Select((name, i) => (name, count: stats.InstancesPerClass[i]))
                .Where(item => item.count is > 0 and < MinInstancesPerClass)
                .Select(item => $"{item.name} {item.count} 个"));
            warnings.Add($"部分类别标注太少（建议每类至少 {MinInstancesPerClass} 个、{MinInstancesPerClass * 5} 个以上才比较稳）：{detail}。标注太少的类别训练后基本识别不到。");
        }
        if (stats.ImageCount < MinImages)
        {
            warnings.Add($"图片只有 {stats.ImageCount} 张（建议至少 {MinImages} 张，且每类 {MinInstancesPerClass} 个实例以上）。图片太少时模型只会记住这几张图，换一张就识别不到。");
        }
        if (stats.HasBoxes && stats.BoxSizesPixels.Count > 0 && stats.MinBoxPixels < MinBoxPixels)
        {
            var median = stats.MedianBoxPixels.ToString("0.#", CultureInfo.InvariantCulture);
            var smallest = stats.MinBoxPixels.ToString("0.#", CultureInfo.InvariantCulture);
            warnings.Add($"目标在训练输入尺寸（imgsz={imageSize}）下偏小：最小边只有 {smallest} 像素、中位数 {median} 像素（建议 ≥ {MinBoxPixels:0} 像素）。"
                + "请把图像尺寸（imgsz）调大，或先把大图切成小图再标注。");
        }
        if (useVal && stats.ValImageCount < MinValImages)
        {
            warnings.Add(stats.ValImageCount == 0
                ? "验证集为空，训练结束后无法评估效果。"
                : $"验证集只有 {stats.ValImageCount} 张图片（建议至少 {MinValImages} 张），日志里的 mAP 指标几乎没有参考价值。");
        }
        if (stats.HasBoxes && stats.BoxCount < MinInstancesPerClass * 2)
        {
            warnings.Add($"全部标注框一共只有 {stats.BoxCount} 个。样本量这么小时，即使日志显示训练成功，模型也学不到可用特征（典型现象：训练完识别不到任何目标）。");
        }
        if (stats.BoxCount > 0 && stats.ImageCount < MinImages && epochs < MinEpochsForSmallDataset)
        {
            warnings.Add($"图片少时建议把训练轮数调大：当前 {epochs} 轮，建议 {MinEpochsForSmallDataset} 轮以上。"
                + $"图片 {stats.ImageCount} 张、batch 16 时每轮只有约 {Math.Max(1, stats.ImageCount / 16)} 次参数更新，{epochs} 轮总共才约 {Math.Max(1, stats.ImageCount / 16) * epochs} 次，远远不够收敛。");
        }
        return warnings;
    }

    /// <summary>生成一行体检摘要（始终记录，便于事后核对训练用的是哪份数据）。</summary>
    public static string Summary(DatasetStats stats, int imageSize)
    {
        var counts = string.Join("、", stats.ClassNames.Select((name, i) => $"{name} {stats.InstancesPerClass[i]}"));
        var size = stats.HasBoxes && stats.BoxSizesPixels.Count > 0
            ? $"框最小边 中位 {stats.MedianBoxPixels:0.#}px/最小 {stats.MinBoxPixels:0.#}px（imgsz={imageSize}）"
            : "无框统计";
        return $"数据集体检：{stats.ImageCount} 张图片、{stats.BoxCount} 个标注、{stats.ValImageCount} 张验证图；{size}；每类实例：{counts}";
    }

    private static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int CountFiles(string directory)
        => Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Count() : 0;

    private static int CountFilesRecursive(string directory)
        => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count() : 0;
}
