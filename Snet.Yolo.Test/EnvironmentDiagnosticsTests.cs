using Snet.Yolo.Tasks.Components.Shell;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>环境自检：探测输出解析、逐项判定与“是否需要重置”的规则。</summary>
public sealed class EnvironmentDiagnosticsTests
{
    /// <summary>探测脚本只输出一行 JSON；前面的警告文本被忽略。</summary>
    [Fact]
    public void Parse_ReadsLastJsonLine()
    {
        var facts = PythonEnvironmentProbe.Parse("UserWarning: something\n"
            + """{"torch":"2.11.0+cu128","torchCuda":"12.8","cudaAvailable":true,"kernelOk":true,"devices":["NVIDIA GeForce RTX 4080"],"anomalib":"2.6.2","onnxruntime":"1.31.1"}""");

        Assert.NotNull(facts);
        Assert.Equal("2.11.0+cu128", facts.Torch);
        Assert.Equal("12.8", facts.TorchCuda);
        Assert.True(facts.CudaAvailable);
        Assert.True(facts.KernelOk);
        Assert.Equal(["NVIDIA GeForce RTX 4080"], facts.Devices);
        Assert.Equal("2.6.2", facts.Anomalib);
        Assert.Null(facts.Ultralytics);
    }

    /// <summary>没有 JSON 或 JSON 损坏时返回空。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("{ broken")]
    public void Parse_ReturnsNullWithoutValidJson(string output) => Assert.Null(PythonEnvironmentProbe.Parse(output));

    /// <summary>构造探测事实。</summary>
    private static PythonEnvironmentFacts Facts(bool cuda, bool kernel, string torchCuda = "12.8", string? anomalib = AnomalibEnvironmentPlanner.AnomalibVersion,
        string? onnx = AnomalibEnvironmentPlanner.OnnxRuntimeVersion, string? ultralytics = "8.4.0")
        => new()
        {
            Torch = "2.11.0", TorchCuda = torchCuda, CudaAvailable = cuda, KernelOk = kernel,
            Devices = cuda ? ["RTX 4080"] : [], Anomalib = anomalib, OnnxRuntime = onnx, Ultralytics = ultralytics,
        };

    /// <summary>构造报告，宿主机是否有 GPU 决定 GPU 项是否为必需。</summary>
    private static EnvironmentReport Report(EnvironmentComponent component, PythonEnvironmentFacts facts, int hostGpus)
        => new(component, EnvironmentDiagnosticsService.BuildPythonItems(component, "python", facts, hostGpus), hostGpus > 0, DateTime.UtcNow);

    /// <summary>宿主机有 GPU 且 PyTorch 的 CUDA 与 GPU 运算都正常时环境就绪。</summary>
    [Fact]
    public void GpuHost_WithWorkingCuda_IsReady()
    {
        var report = Report(EnvironmentComponent.Anomalib, Facts(cuda: true, kernel: true), hostGpus: 1);
        Assert.True(report.Ready);
        Assert.True(report.GpuReady);
        Assert.False(report.NeedsRepair);
    }

    /// <summary>宿主机有 GPU 却装了 CPU 版 PyTorch：必须提示修复（这正是“选了 GPU 却用不上”的场景）。</summary>
    [Fact]
    public void GpuHost_WithCpuTorch_NeedsRepair()
    {
        var report = Report(EnvironmentComponent.Yolo, Facts(cuda: false, kernel: false, torchCuda: string.Empty), hostGpus: 1);
        Assert.False(report.Ready);
        Assert.True(report.NeedsRepair);
        Assert.Contains(report.Items, item => item.Key == "GpuTorch" && !item.Ok && item.Required);
    }

    /// <summary>宿主机没有 GPU 时，CPU 版 PyTorch 是正常的，不要求修复。</summary>
    [Fact]
    public void CpuHost_WithCpuTorch_IsReady()
    {
        var report = Report(EnvironmentComponent.Yolo, Facts(cuda: false, kernel: false, torchCuda: string.Empty), hostGpus: 0);
        Assert.True(report.Ready);
        Assert.False(report.NeedsRepair);
    }

    /// <summary>Anomalib 必须是固定版本；版本不符或缺包都需要修复，YOLO 缺 ultralytics 同理。</summary>
    [Fact]
    public void PinnedPackages_AreRequired()
    {
        Assert.True(Report(EnvironmentComponent.Anomalib, Facts(cuda: false, kernel: false, anomalib: "2.5.0"), 0).NeedsRepair);
        Assert.True(Report(EnvironmentComponent.Anomalib, Facts(cuda: false, kernel: false, onnx: null), 0).NeedsRepair);
        Assert.True(Report(EnvironmentComponent.Yolo, Facts(cuda: false, kernel: false, ultralytics: null), 0).NeedsRepair);
    }

    /// <summary>纯 CPU 的 SAM 发行包装在有 GPU 的机器上：GPU 运行库不是要求项，重置也修不好，不应一直提示需要修复。</summary>
    [Fact]
    public void GpuHost_WithCpuOnlySamBuild_IsNotMarkedForRepair()
    {
        EnvironmentCheckItem[] items =
        [
            new("Execution", false, "ONNX Runtime CPU", Required: false),
            new("GpuDriver", true, "GPU 0 · RTX 4080", Required: false),
            new("GpuRuntime", false, "当前发行包不含 CUDA 执行提供程序。", Required: false),
        ];
        var report = new EnvironmentReport(EnvironmentComponent.Sam, items, HostHasGpu: true, DateTime.UtcNow);

        Assert.True(report.GpuReady);
        Assert.False(report.NeedsRepair);
    }

    /// <summary>探测脚本必须包含真实 GPU 张量运算，而不只是 CUDA 可见性检查。</summary>
    [Fact]
    public void ProbeScript_RunsRealKernelOnGpu() => Assert.Contains("torch.ones(1, device=\"cuda\")", PythonEnvironmentProbe.Script, StringComparison.Ordinal);

    /// <summary>运行库缺失说明只保留“缺什么”的第一句，不把搜索过的全部目录带进界面。</summary>
    [Theory]
    [InlineData("缺少 CUDA 运行库 a.dll、b.dll。已搜索目录：C:\\x; D:\\y。", "缺少 CUDA 运行库 a.dll、b.dll。")]
    [InlineData("没有句号的说明", "没有句号的说明")]
    [InlineData("", "")]
    public void FirstSentence_KeepsOnlyTheFirstSentence(string message, string expected)
        => Assert.Equal(expected, EnvironmentDiagnosticsService.FirstSentence(message));

    /// <summary>曲线折线：缺失值处断开，少于两个有效点时没有路径。</summary>
    [Fact]
    public void MetricCurve_BuildsPathWithGaps()
    {
        Assert.Equal(string.Empty, MetricCurve.BuildPath([1d], 100, 40));
        Assert.Equal(string.Empty, MetricCurve.BuildPath([null, null], 100, 40));
        var path = MetricCurve.BuildPath([1d, 2d, null, 3d, 4d], 100, 40);
        Assert.StartsWith("M1 ", path);
        Assert.Equal(2, path.Split('M').Length - 1);
        Assert.DoesNotContain("NaN", path);
    }
}
