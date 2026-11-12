using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Core.Training;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练推荐值：输入尺寸看小目标与原图分辨率，模型规格看数据量与设备，轮数看参数更新次数，验证集看图片数。</summary>
public sealed class YoloTrainingAdvisorTests
{
    /// <summary>每张图一个框的画像：框短边 <paramref name="shortSide"/>、图片长边 <paramref name="longEdge"/>（原图像素）。</summary>
    private static YoloDatasetProfile Profile(int images, double shortSide = 300, int longEdge = 3000)
    {
        var boxes = Enumerable.Repeat(new YoloBoxSample(shortSide, longEdge), images).ToArray();
        return new YoloDatasetProfile(images, images, boxes.Length, 1, boxes, longEdge);
    }

    private static YoloRecommendation Recommend(YoloDatasetProfile profile, YoloTaskType taskType = YoloTaskType.Detect, YoloHardware hardware = default)
        => YoloTrainingAdvisor.Recommend(profile, taskType, hardware);

    // ---------- 输入尺寸 ----------

    [Fact]
    public void ImageSize_BigObjects_KeepsTheDefaultFloor()
    {
        var advice = Recommend(Profile(500, shortSide: 300, longEdge: 3000));

        Assert.Equal(YoloTrainingAdvisor.MinImageSize, advice.ImageSize);
        Assert.Contains(advice.Reasons, item => item is { Field: YoloAdviceField.ImageSize, ResourceKey: "YoloAdviceSizeObjects" });
    }

    [Fact]
    public void ImageSize_SmallObjects_GrowsUntilTheSmallestFivePercentAreVisible()
    {
        // 4000 px 的图里 60 px 的框：640 输入上只有 9.6 px；要到 16 px 需要 16/60×4000 ≈ 1067，按 32 对齐 → 1088
        var advice = Recommend(Profile(500, shortSide: 60, longEdge: 4000));

        Assert.Equal(1088, advice.ImageSize);
        Assert.True(advice.ImageSize % 32 == 0);
        Assert.Contains(advice.Reasons, item => item is { Field: YoloAdviceField.ImageSize, ResourceKey: "YoloAdviceSizeObjects" });
    }

    [Fact]
    public void ImageSize_TinyObjects_StopsAtTheUpperLimitAndAdvisesTiling()
    {
        var advice = Recommend(Profile(500, shortSide: 20, longEdge: 4000));

        Assert.Equal(YoloTrainingAdvisor.MaxImageSize, advice.ImageSize);
        Assert.Contains(advice.Reasons, item => item is { Field: YoloAdviceField.ImageSize, ResourceKey: "YoloAdviceSizeStillSmall" });
    }

    [Fact]
    public void ImageSize_NeverExceedsTheSourceResolution()
    {
        // 800 px 的图里 10 px 的框要 1280 才够 16 px，但把 800 的图放大到 1280 不会多出细节
        var advice = Recommend(Profile(500, shortSide: 10, longEdge: 800));

        Assert.Equal(800, advice.ImageSize);
        Assert.Contains(advice.Reasons, item => item is { Field: YoloAdviceField.ImageSize, ResourceKey: "YoloAdviceSizeSource" });
    }

    [Fact]
    public void ImageSize_TinySourceImages_StillUseTheDefaultFloor()
    {
        var advice = Recommend(Profile(500, shortSide: 100, longEdge: 320));

        Assert.Equal(YoloTrainingAdvisor.MinImageSize, advice.ImageSize);
    }

    [Fact]
    public void ImageSize_Classification_UsesTheStandardInput()
    {
        var advice = Recommend(new YoloDatasetProfile(500, 500, 0, 3, [], 0), YoloTaskType.Classify);

        Assert.Equal(YoloTrainingAdvisor.ClassifyImageSize, advice.ImageSize);
    }

    [Fact]
    public void ImageSize_WithoutBoxes_UsesTheDefault()
    {
        var advice = Recommend(new YoloDatasetProfile(500, 500, 0, 3, [], 0));

        Assert.Equal(YoloTrainingAdvisor.MinImageSize, advice.ImageSize);
        Assert.Contains(advice.Reasons, item => item.ResourceKey == "YoloAdviceSizeNoBoxes");
    }

    // ---------- 模型规格 ----------

    [Theory]
    [InlineData(50, "n")]
    [InlineData(2999, "n")]
    [InlineData(3000, "s")]
    [InlineData(14999, "s")]
    [InlineData(15000, "m")]
    [InlineData(50000, "m")]
    public void Scale_FollowsTheNumberOfAnnotatedImages(int images, string expected)
        => Assert.Equal(expected, Recommend(Profile(images)).ModelScale);

    [Theory]
    [InlineData(4096, "s")]
    [InlineData(7999, "s")]
    [InlineData(8000, "m")]
    [InlineData(24576, "m")]
    public void Scale_IsCappedByGpuMemory(int vramMb, string expected)
    {
        var advice = Recommend(Profile(20000), hardware: YoloHardware.Gpu(vramMb));

        Assert.Equal(expected, advice.ModelScale);
        if (expected != "m") { Assert.Contains(advice.Reasons, item => item.ResourceKey == "YoloAdviceModelVram"); }
    }

    [Fact]
    public void Scale_OnTheCpu_IsNano()
    {
        var advice = Recommend(Profile(20000), hardware: YoloHardware.Cpu);

        Assert.Equal("n", advice.ModelScale);
        Assert.Contains(advice.Reasons, item => item.ResourceKey == "YoloAdviceModelCpu");
    }

    [Fact]
    public void Scale_WithUnknownHardware_IsNotLimited()
        => Assert.Equal("m", Recommend(Profile(20000), hardware: YoloHardware.Unknown).ModelScale);

    [Fact]
    public void Scale_SmallDataset_IsNotRaisedByAnAbundantGpu()
        => Assert.Equal("n", Recommend(Profile(100), hardware: YoloHardware.Gpu(24576)).ModelScale);

    // ---------- 训练轮数 ----------

    [Theory]
    [InlineData(10)]
    [InlineData(300)]
    [InlineData(2762)]
    [InlineData(20000)]
    public void Epochs_AreWithinBoundsAndOnTheSliderStep(int images)
    {
        var epochs = Recommend(Profile(images)).Epochs;

        Assert.InRange(epochs, YoloTrainingAdvisor.MinEpochs, YoloTrainingAdvisor.MaxEpochs);
        Assert.Equal(0, epochs % YoloTrainingAdvisor.EpochStep);
    }

    [Fact]
    public void Epochs_AreNeverFewerForABiggerDataset()
    {
        var previous = int.MaxValue;
        foreach (var images in new[] { 50, 150, 400, 1000, 3000, 10000 })
        {
            var epochs = Recommend(Profile(images)).Epochs;
            Assert.True(epochs <= previous, $"{images} 张图推荐 {epochs} 轮，比更小的数据集（{previous} 轮）还多。");
            previous = epochs;
        }
    }

    // ---------- 验证集 ----------

    [Theory]
    [InlineData(99, false)]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(2762, true)]
    public void Validation_IsRecommendedOnlyWithEnoughImages(int images, bool expected)
    {
        var advice = Recommend(Profile(images));

        Assert.Equal(expected, advice.UseValidation);
        Assert.Contains(advice.Reasons, item => item.Field == YoloAdviceField.Validation
            && item.ResourceKey == (expected ? "YoloAdviceValidationOn" : "YoloAdviceValidationOff"));
    }

    // ---------- 推荐值的应用 ----------

    [Theory]
    [InlineData("yolo26n.pt", "s", "yolo26s.pt")]
    [InlineData("yolo11m-seg.pt", "l", "yolo11l-seg.pt")]
    [InlineData("yolo26x-cls.pt", "n", "yolo26n-cls.pt")]
    [InlineData("custom-weights.pt", "s", "custom-weights.pt")]
    public void ModelFor_KeepsTheFamilyAndTheTaskSuffix(string current, string scale, string expected)
    {
        var advice = new YoloRecommendation(640, scale, 100, true, []);

        Assert.Equal(expected, advice.ModelFor(current));
    }

    [Fact]
    public void Matches_ComparesEveryRecommendedOption()
    {
        var advice = new YoloRecommendation(800, "s", 300, true, []);
        var same = new TrainingOptions { ImgSize = 800, Epochs = 300, UseVal = true, Model = "yolo26s-seg.pt" };

        Assert.True(advice.Matches(same));
        Assert.False(advice.Matches(new TrainingOptions { ImgSize = 640, Epochs = 300, UseVal = true, Model = "yolo26s.pt" }));
        Assert.False(advice.Matches(new TrainingOptions { ImgSize = 800, Epochs = 500, UseVal = true, Model = "yolo26s.pt" }));
        Assert.False(advice.Matches(new TrainingOptions { ImgSize = 800, Epochs = 300, UseVal = false, Model = "yolo26s.pt" }));
        Assert.False(advice.Matches(new TrainingOptions { ImgSize = 800, Epochs = 300, UseVal = true, Model = "yolo26n.pt" }));
    }

    // ---------- 界面文案 ----------

    [Fact]
    public void EveryReasonKey_ExistsInBothLanguagesWithMatchingPlaceholders()
    {
        var manager = new ResourceManager("Snet.Yolo.Tasks.Core.Localization.AppResource", typeof(LanguageManager).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var chinese = manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)!;
        var reasons = new[]
        {
            Recommend(Profile(500)),                                                                   // 目标够大 + 数据量 + 轮数 + 验证开
            Recommend(Profile(50, shortSide: 60, longEdge: 4000)),                                      // 目标偏小要放大 + 验证关
            Recommend(Profile(500, shortSide: 20, longEdge: 4000)),                                     // 仍然偏小
            Recommend(Profile(500, shortSide: 10, longEdge: 800)),                                      // 受原图分辨率限制
            Recommend(new YoloDatasetProfile(500, 500, 0, 3, [], 0), YoloTaskType.Classify),            // 分类
            Recommend(new YoloDatasetProfile(500, 500, 0, 3, [], 0)),                                   // 没有框
            Recommend(Profile(20000), hardware: YoloHardware.Gpu(6000)),                                // 显存限制
            Recommend(Profile(20000), hardware: YoloHardware.Cpu),                                      // CPU
        }.SelectMany(advice => advice.Reasons).DistinctBy(item => item.ResourceKey).ToList();

        // 新增推荐依据时，这里必须同步补上覆盖它的画像，否则它的中英文文案不会被检查。
        string[] expectedKeys =
        [
            "YoloAdviceSizeClassify", "YoloAdviceSizeNoBoxes", "YoloAdviceSizeStillSmall", "YoloAdviceSizeSource", "YoloAdviceSizeObjects",
            "YoloAdviceModelData", "YoloAdviceModelVram", "YoloAdviceModelCpu", "YoloAdviceEpochs", "YoloAdviceValidationOn", "YoloAdviceValidationOff",
        ];
        Assert.Equal(expectedKeys.Order(StringComparer.Ordinal), reasons.Select(item => item.ResourceKey).Order(StringComparer.Ordinal));
        foreach (var item in reasons)
        {
            var englishText = Assert.IsType<string>(english.GetString(item.ResourceKey));
            var chineseText = Assert.IsType<string>(chinese.GetString(item.ResourceKey));
            var placeholders = Placeholders(englishText);
            Assert.Equal(placeholders, Placeholders(chineseText));
            Assert.All(placeholders, index => Assert.True(index < item.Arguments.Count, $"{item.ResourceKey} 用到了第 {index} 个参数，但只提供了 {item.Arguments.Count} 个。"));
            _ = string.Format(CultureInfo.InvariantCulture, englishText, item.Arguments.ToArray());
            _ = string.Format(CultureInfo.InvariantCulture, chineseText, item.Arguments.ToArray());
        }
    }

    /// <summary>提取 string.Format 序号，发现翻译中漏写或错写的参数。</summary>
    private static int[] Placeholders(string value)
        => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[^}]*)\}(?!\})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();
}
