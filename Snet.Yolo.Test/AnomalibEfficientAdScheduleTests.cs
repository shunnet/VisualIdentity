using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using System.Diagnostics;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// EfficientAD 的训练方式与建议值：固定轮数训练、末段学习率衰减、采用最终权重，输入尺寸与轮数按模型给出建议。
/// 背景：用户把 768 px 的 EfficientAD 训练了 89 轮就被“合成异常验证 AUROC”早停，采用的是第 39 轮（约 4200 步，官方是 70000 步）的权重；
/// 同一批真实异常图上它只检出 54%，而 256 px 的同款模型能检出 66%–71%，大块异物（网状薄片）在 768 px 下整张漏检。
/// </summary>
public sealed class AnomalibEfficientAdScheduleTests
{
    // ---------- 训练轮数建议 ----------

    [Theory]
    [InlineData(150, 300, 108)]     // 15 张校准图 → 135 张训练图 → 留出 27 张验证 → 108 张拟合；30000 / 108 ≈ 278 → 300 轮
    [InlineData(60, 700, 43)]       // 6 → 54 → 11 → 43；30000 / 43 ≈ 698 → 700 轮
    [InlineData(1000, 100, 720)]    // 100 → 900 → 180 → 720；30000 / 720 ≈ 42 → 100 轮（下限）
    [InlineData(10, 4300, 7)]       // 1 → 9 → 2 → 7；30000 / 7 ≈ 4286 → 4300 轮
    [InlineData(2, 10_000, 1)]      // 1 → 1 → 1；30000 轮超过上限 → 10000 轮
    public void EpochGuidance_TargetsTheRecommendedStepBudget(int normalImages, int expectedEpochs, int expectedFittingImages)
    {
        var guidance = AnomalibModelCatalog.GetEpochGuidance(AnomalibModelKind.EfficientAdSmall, normalImages);

        Assert.NotNull(guidance);
        Assert.Equal(expectedEpochs, guidance.Recommended);
        Assert.Equal(expectedFittingImages, guidance.TrainingImages);
        Assert.Equal(expectedEpochs * expectedFittingImages, guidance.Steps);
        Assert.Equal(0, guidance.Recommended % 100);   // 与页面滑块的步长一致，不会被滑块吸附到别的数
        Assert.InRange(guidance.Recommended, 100, 10_000);
    }

    [Theory]
    [InlineData(AnomalibModelKind.Padim)]
    [InlineData(AnomalibModelKind.PatchCore)]
    public void EpochGuidance_OneShotModelsHaveNone(AnomalibModelKind kind)
        => Assert.Null(AnomalibModelCatalog.GetEpochGuidance(kind, 150));

    [Fact]
    public void EpochGuidance_NeedsAtLeastTwoImages()
    {
        Assert.Null(AnomalibModelCatalog.GetEpochGuidance(AnomalibModelKind.EfficientAdSmall, 0));
        Assert.Null(AnomalibModelCatalog.GetEpochGuidance(AnomalibModelKind.EfficientAdSmall, 1));
        Assert.NotNull(AnomalibModelCatalog.GetEpochGuidance(AnomalibModelKind.EfficientAdSmall, 2));
    }

    [Fact]
    public void EpochGuidance_FlagsFarTooFewEpochs()
    {
        var guidance = AnomalibModelCatalog.GetEpochGuidance(AnomalibModelKind.EfficientAdSmall, 150)!;   // 推荐 300 轮

        Assert.True(guidance.IsFarBelowRecommended(100));
        Assert.True(guidance.IsFarBelowRecommended(149));
        Assert.False(guidance.IsFarBelowRecommended(150));
        Assert.False(guidance.IsFarBelowRecommended(300));
        Assert.Equal(10_800, guidance.StepsFor(100));
    }

    // ---------- 输入尺寸 ----------

    [Theory]
    [InlineData(AnomalibModelKind.EfficientAdSmall, AnomalibModelKind.EfficientAdSmall, 384, 384)]   // 同一个模型、没超过建议上限：沿用
    [InlineData(AnomalibModelKind.EfficientAdSmall, AnomalibModelKind.EfficientAdSmall, 512, 512)]   // 恰好等于建议上限：沿用
    [InlineData(AnomalibModelKind.EfficientAdSmall, AnomalibModelKind.EfficientAdSmall, 768, 256)]   // 超过建议上限：用推荐值，不再沿用
    [InlineData(AnomalibModelKind.EfficientAdSmall, AnomalibModelKind.PatchCore, 768, 256)]          // 上一次是别的模型：用推荐值（用户的实际情形）
    [InlineData(AnomalibModelKind.PatchCore, AnomalibModelKind.PatchCore, 768, 768)]
    [InlineData(AnomalibModelKind.PatchCore, AnomalibModelKind.EfficientAdSmall, 256, 512)]
    [InlineData(AnomalibModelKind.Padim, null, 0, 256)]                                              // 没训练过
    [InlineData(AnomalibModelKind.Padim, AnomalibModelKind.Padim, 96, 256)]                          // 非法的旧值
    public void ResolveImageSize_ReusesOnlyReasonableSizesOfTheSameModel(AnomalibModelKind model, AnomalibModelKind? previous, int previousSize, int expected)
        => Assert.Equal(expected, AnomalibModelCatalog.ResolveImageSize(model, previous, previousSize));

    // ---------- 流水线脚本 ----------

    [Fact]
    public void Pipeline_TrainsAFixedNumberOfEpochsAndUsesTheFinalWeightsByDefault()
    {
        var source = AnomalibPythonPipeline.Source;

        // 早停默认关闭；关闭时采用训练结束时自己保存的最终权重，不按合成异常上的验证指标挑检查点
        Assert.Contains("config.get(\"earlyStoppingEnabled\", False)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("config.get(\"earlyStoppingEnabled\", True)", source, StringComparison.Ordinal);
        Assert.Contains("FINAL_CHECKPOINT = \"final-weights.ckpt\"", source, StringComparison.Ordinal);
        Assert.Contains("trainer.save_checkpoint(self.path)", source, StringComparison.Ordinal);
        Assert.Contains("checkpoint = artifact_root / FINAL_CHECKPOINT", source, StringComparison.Ordinal);
        // ModelCheckpoint(save_last=True) 只在验证指标刷新最佳时才更新 last.ckpt，不是最终权重，不能再用它
        Assert.DoesNotContain("save_last=True,", source, StringComparison.Ordinal);   // 作为参数传给 ModelCheckpoint（说明文字里提到它不算）
        Assert.DoesNotContain("last_model_path", source, StringComparison.Ordinal);
        // 学习率衰减依赖 max_epochs 是真实的训练轮数
        Assert.Contains("max_epochs=config[\"maxEpochs\"]", source, StringComparison.Ordinal);
        Assert.Contains("check_val_every_n_epoch=validation_interval(config)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 固定轮数训练只在约 20 个时间点验证，但最后一轮必须验证（那一次验证会按最终权重重新估计归一化分位数，导出的模型要带着配套的分位数）。
    /// 直接把脚本里的函数取出来执行，不依赖 torch / anomalib；本机没有 Python 时跳过。
    /// </summary>
    [Fact]
    public async Task ValidationInterval_AlwaysValidatesTheFinalEpoch()
    {
        var python = FindPython();
        if (python is null) { return; }
        const string program = """
            import ast, sys
            tree = ast.parse(sys.stdin.buffer.read().decode('utf-8'))
            node = next(item for item in tree.body if isinstance(item, ast.FunctionDef) and item.name == 'validation_interval')
            scope = {}
            exec(compile(ast.Module([node], []), 'validation_interval', 'exec'), scope)
            interval = scope['validation_interval']
            efficient = lambda epochs, early=False: {'model': 'efficient_ad_small', 'maxEpochs': epochs, 'earlyStoppingEnabled': early}
            assert interval(efficient(600)) == 30
            assert interval(efficient(250)) == 10
            assert interval(efficient(100)) == 5
            assert interval(efficient(7)) == 1
            assert interval(efficient(10000)) == 500
            assert interval(efficient(600, early=True)) == 1          # 早停要逐轮比较验证指标
            assert interval({'model': 'patchcore', 'maxEpochs': 600}) == 1
            for epochs in range(1, 2001):
                step = interval(efficient(epochs))
                assert 1 <= step <= max(1, epochs // 20), (epochs, step)
                assert epochs % step == 0, (epochs, step)             # 最后一轮是验证轮
            """;
        // 校验程序写进独立文件再运行（-I 隔离模式不会从当前目录导入模块）；被检查的脚本正文从标准输入按 UTF-8 字节传入。
        var checker = Path.Combine(Path.GetTempPath(), "snet-validation-interval-" + Guid.NewGuid().ToString("N") + ".py");
        await File.WriteAllTextAsync(checker, program, new System.Text.UTF8Encoding(false), TestContext.Current.CancellationToken);
        try
        {
            var start = new ProcessStartInfo(python, $"-I \"{checker}\"") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var process = Process.Start(start)!;
            await process.StandardInput.BaseStream.WriteAsync(new System.Text.UTF8Encoding(false).GetBytes(AnomalibPythonPipeline.Source), TestContext.Current.CancellationToken);
            process.StandardInput.Close();
            var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.True(process.ExitCode == 0, error);
        }
        finally { File.Delete(checker); }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python", "python3" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(name, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                if (process is null) { continue; }
                process.WaitForExit(5000);
                if (process.ExitCode == 0) { return name; }
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }
}
