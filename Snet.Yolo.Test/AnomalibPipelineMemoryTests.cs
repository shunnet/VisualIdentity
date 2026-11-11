using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using System.Diagnostics;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// PaDiM 的显存优化（嵌入放内存 + 分块拟合）与各模型的输入尺寸建议。
/// 分块拟合的数值与显存对照在服务器上完成（见 docs/plans/2026-11-11-anomalib-accuracy-and-speed.md），这里守住脚本结构与建议值。
/// </summary>
public sealed class AnomalibPipelineMemoryTests
{
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

    /// <summary>内置脚本改动频繁且只在训练时才会执行：用 Python 的语法解析提前发现缩进/引号错误（本机没有 Python 时跳过检查）。</summary>
    [Fact]
    public async Task EmbeddedScript_IsSyntacticallyValidPython()
    {
        var python = FindPython();
        if (python is null) { return; }
        // 读原始字节再按 UTF-8 解码：Windows 上 Python 的 stdin 默认用控制台代码页（如 GBK），会把中文注释读坏。
        var start = new ProcessStartInfo(python, "-I -c \"import ast,sys; ast.parse(sys.stdin.buffer.read().decode('utf-8'), 'anomalib_pipeline.py')\"")
        {
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        await process.StandardInput.BaseStream.WriteAsync(new System.Text.UTF8Encoding(false).GetBytes(AnomalibPythonPipeline.Source), TestContext.Current.CancellationToken);
        process.StandardInput.Close();
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, error);
    }

    /// <summary>PaDiM 训练显存的大头是库自带 fit 的“全部位置同时求协方差”：改为内存嵌入仓库 + 分块拟合。</summary>
    [Fact]
    public void Padim_UsesCpuEmbeddingBank_AndChunkedFit()
    {
        var source = AnomalibPythonPipeline.Source;

        Assert.Contains("class CpuEmbeddingBank(list):", source, StringComparison.Ordinal);
        Assert.Contains("super().append(item.detach().cpu())", source, StringComparison.Ordinal);
        Assert.Contains("def chunked_padim_fit(torch_model)", source, StringComparison.Ordinal);
        // 必须装在类上：装在实例上的绑定方法被 pickle / 深拷贝时按函数名反查属性，
        // 服务器端到端验证时因此报过 'PadimModel' object has no attribute 'chunked_padim_fit'。
        Assert.Contains("type(torch_model).fit = chunked_padim_fit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("types.MethodType", source, StringComparison.Ordinal);
        // padim 分支必须安装它（否则优化形同虚设）
        var padimBranch = source[source.IndexOf("if name == \"padim\":", StringComparison.Ordinal)..];
        padimBranch = padimBranch[..padimBranch.IndexOf("if name == \"efficient_ad_small\":", StringComparison.Ordinal)];
        Assert.Contains("install_memory_saving_fit(model.model)", padimBranch, StringComparison.Ordinal);
    }

    /// <summary>分块拟合必须与库自带的估计一致：样本协方差 /(N-1) + 0.01·I + 1e-5·I，再求逆。</summary>
    [Fact]
    public void ChunkedFit_UsesTheSameEstimatorAsTheLibrary()
    {
        var source = AnomalibPythonPipeline.Source;

        Assert.Contains("/ (total - 1)", source, StringComparison.Ordinal);
        Assert.Contains("covariance + 0.01 * identity + 1e-5 * identity", source, StringComparison.Ordinal);
        Assert.Contains("torch.linalg.inv(covariance)", source, StringComparison.Ordinal);
        Assert.Contains("torch_model.gaussian.mean = mean", source, StringComparison.Ordinal);
        Assert.Contains("torch_model.gaussian.inv_covariance = inverse", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AnomalibModelKind.Padim, 256, 384)]
    [InlineData(AnomalibModelKind.EfficientAdSmall, 256, 512)]
    [InlineData(AnomalibModelKind.PatchCore, 512, 768)]
    public void SizeGuidance_MatchesTheMeasuredSweetSpots(AnomalibModelKind kind, int recommended, int warnAbove)
    {
        var guidance = AnomalibModelCatalog.GetSizeGuidance(kind);

        Assert.Equal(recommended, guidance.Recommended);
        Assert.Equal(warnAbove, guidance.WarnAbove);
        // 推荐值必须是合法的训练尺寸，且不高于建议上限
        new AnomalibTrainingOptions { Model = kind, ImageSize = guidance.Recommended }.Validate();
        Assert.True(guidance.Recommended <= guidance.WarnAbove);
        Assert.False(guidance.IsAboveRecommendedMaximum(guidance.Recommended));
        Assert.False(guidance.IsAboveRecommendedMaximum(guidance.WarnAbove));
        Assert.True(guidance.IsAboveRecommendedMaximum(guidance.WarnAbove + 32));
    }

    /// <summary>用户之前训练出的模型（PatchCore 2048、EfficientAD 1728）都远高于建议上限，页面应能提示。</summary>
    [Theory]
    [InlineData(AnomalibModelKind.PatchCore, 2048)]
    [InlineData(AnomalibModelKind.EfficientAdSmall, 1728)]
    [InlineData(AnomalibModelKind.Padim, 640)]
    public void SizeGuidance_FlagsTheOversizedConfigurationsThatMadeRecognitionSlowAndNoisy(AnomalibModelKind kind, int size)
        => Assert.True(AnomalibModelCatalog.GetSizeGuidance(kind).IsAboveRecommendedMaximum(size));
}
