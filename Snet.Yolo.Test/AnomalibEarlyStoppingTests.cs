using System.Text.Json;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>EfficientAD 早停配置和状态快照的回归。</summary>
public sealed class AnomalibEarlyStoppingTests
{
    /// <summary>默认启用早停，耐心 50 轮，最小改善幅度为 0.001。</summary>
    [Fact]
    public void Options_EnableEarlyStoppingByDefault()
    {
        var options = new AnomalibTrainingOptions { Model = AnomalibModelKind.EfficientAdSmall };
        var json = JsonSerializer.SerializeToElement(options);
        Assert.True(json.TryGetProperty("EarlyStoppingEnabled", out var enabled));
        Assert.True(enabled.GetBoolean());
        Assert.Equal(50, json.GetProperty("EarlyStoppingPatience").GetInt32());
        Assert.Equal(0.001, json.GetProperty("EarlyStoppingMinDelta").GetDouble());
    }

    /// <summary>状态快照应保留用户选择的早停配置。</summary>
    [Fact]
    public void Status_ClonePreservesEarlyStoppingConfiguration()
    {
        var status = JsonSerializer.Deserialize<AnomalibTrainingStatus>("""{"EarlyStoppingEnabled":false,"EarlyStoppingPatience":20,"EarlyStoppingMinDelta":0.005}""")!;
        var json = JsonSerializer.SerializeToElement(status.Clone());
        Assert.True(json.TryGetProperty("EarlyStoppingEnabled", out var enabled));
        Assert.False(enabled.GetBoolean());
        Assert.Equal(20, json.GetProperty("EarlyStoppingPatience").GetInt32());
        Assert.Equal(0.005, json.GetProperty("EarlyStoppingMinDelta").GetDouble());
    }

    /// <summary>像素 AUROC 的耐心和幅度不得接受无效值。</summary>
    [Theory]
    [InlineData(0, 0.001)]
    [InlineData(1001, 0.001)]
    [InlineData(50, -0.01)]
    [InlineData(50, 1.01)]
    public void Options_RejectInvalidEarlyStoppingValues(int patience, double delta)
    {
        var options = JsonSerializer.Deserialize<AnomalibTrainingOptions>(JsonSerializer.Serialize(new
        {
            Model = AnomalibModelKind.EfficientAdSmall,
            EarlyStoppingPatience = patience,
            EarlyStoppingMinDelta = delta,
        }))!;
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    /// <summary>关闭早停后不使用隐藏的耐心和幅度参数。</summary>
    [Fact]
    public void DisabledEarlyStopping_DoesNotValidateUnusedRanges()
    {
        new AnomalibTrainingOptions
        {
            Model = AnomalibModelKind.EfficientAdSmall,
            EarlyStoppingEnabled = false, EarlyStoppingPatience = 0, EarlyStoppingMinDelta = 2,
        }.Validate();
    }

    /// <summary>非有限幅度不能进入 JSON、状态持久化和 Python 配置。</summary>
    [Theory]
    [InlineData(true, double.NaN)]
    [InlineData(true, double.PositiveInfinity)]
    [InlineData(true, double.NegativeInfinity)]
    [InlineData(false, double.NaN)]
    [InlineData(false, double.PositiveInfinity)]
    [InlineData(false, double.NegativeInfinity)]
    public void NonFiniteDelta_IsAlwaysRejected(bool enabled, double delta)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AnomalibTrainingOptions
        {
            Model = AnomalibModelKind.EfficientAdSmall, EarlyStoppingEnabled = enabled, EarlyStoppingMinDelta = delta,
        }.Validate());

    /// <summary>从训练服务到 Python 配置和状态回调的完整传递不能丢失早停参数。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrainingService_PassesEarlyStoppingConfiguration(bool enabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "earlystop-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var images = Enumerable.Range(0, 20).Select(i => Path.Combine(root, $"{i}.png")).ToArray();
            for (var i = 0; i < images.Length; i++) { await File.WriteAllBytesAsync(images[i], [(byte)i, 1, 2], TestContext.Current.CancellationToken); }
            var runner = new AnomalibConfigurationCaptureRunner();
            AnomalibTrainingStatus? status = null;
            await new AnomalibTrainingService(runner, runner).TrainAsync(new()
            {
                Owner = "test", ProjectId = "earlystop", WorkingDirectory = root, PythonExecutable = "python", Images = images,
                Options = new() { Model = AnomalibModelKind.EfficientAdSmall, Device = "cpu", MaxEpochs = 10_000,
                    EarlyStoppingEnabled = enabled, EarlyStoppingPatience = 25, EarlyStoppingMinDelta = 0.005 },
            }, snapshot => status = snapshot, TestContext.Current.CancellationToken);
            Assert.Equal(enabled, runner.Configuration.GetProperty("earlyStoppingEnabled").GetBoolean());
            Assert.Equal(25, runner.Configuration.GetProperty("earlyStoppingPatience").GetInt32());
            Assert.Equal(0.005, runner.Configuration.GetProperty("earlyStoppingMinDelta").GetDouble());
            Assert.Equal(10_000, runner.Configuration.GetProperty("maxEpochs").GetInt32());
            Assert.NotNull(status);
            Assert.Equal(enabled, status.EarlyStoppingEnabled);
            Assert.Equal(25, status.EarlyStoppingPatience);
            Assert.Equal(0.005, status.EarlyStoppingMinDelta);
            Assert.Equal(0, runner.RegistrationCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
