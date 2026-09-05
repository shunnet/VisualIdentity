using System.Globalization;
using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class TrainingCoreTests
{
    [Fact]
    public void ParseProgress_IsCultureIndependentAndStripsAnsi()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var update = YoloOutputParser.Parse("\u001b[2K  3/10  1.2G  0.125  0.250  0.375  2  640: 100%");

            Assert.NotNull(update);
            Assert.Equal(3, update.Epoch);
            Assert.Equal(10, update.TotalEpochs);
            Assert.Equal(30, update.Percent);
            Assert.Equal(0.125, update.BoxLoss);
            Assert.Equal(0.250, update.ClsLoss);
            Assert.Equal(0.375, update.DflLoss);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ParseMetrics_ReturnsAllValidationMetrics()
    {
        var update = YoloOutputParser.ParseMetrics("all  12  18  0.91  0.82  0.73  0.64");

        Assert.NotNull(update);
        Assert.Equal(0.91, update.Precision);
        Assert.Equal(0.82, update.Recall);
        Assert.Equal(0.73, update.Map50);
        Assert.Equal(0.64, update.Map5095);
    }

    [Fact]
    public void DataYaml_EscapesNamesAndUsesAvailableValidationFolder()
    {
        var yaml = DataYamlBuilder.Build(new[] { "cat", "a\"b" }, @"C:\dataset", useVal: true, hasValDir: true, keypointCount: 4);

        Assert.Contains("path: C:/dataset", yaml);
        Assert.Contains("val: val/images", yaml);
        Assert.Contains("names: [\"cat\", \"a\\\"b\"]", yaml);
        Assert.Contains("kpt_shape: [4, 3]", yaml);
    }

    [Fact]
    public void StatusClone_CanExcludeLargeLogTailAndRemainsIndependent()
    {
        var source = new TrainingStatus { ProjectId = "p", LogTail = new List<string> { "one" } };

        var clone = source.Clone(includeLogs: false);
        clone.Metrics.Map50 = 0.5;

        Assert.Empty(clone.LogTail);
        Assert.Null(source.Metrics.Map50);
    }
}
