using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class GpuTrainingTests
{
    [Fact]
    public void GpuMetrics_ParsesEveryCardInDeviceOrder()
    {
        var gpus = SystemMetrics.ParseGpuCsv("0, RTX 3060, 25, 1024, 12288\n1, RTX 4090, N/A, 2048, 24576\n");

        Assert.Equal(2, gpus.Count);
        Assert.Equal((0, "RTX 3060", 25, 12288d), (gpus[0].Index, gpus[0].Name, gpus[0].Utilization, gpus[0].VramTotalMb));
        Assert.Equal((1, "RTX 4090", 0, 24576d), (gpus[1].Index, gpus[1].Name, gpus[1].Utilization, gpus[1].VramTotalMb));
    }

    [Theory]
    [InlineData("0,1", false, 2)]
    [InlineData("cuda:1,3", true, 2)]
    [InlineData("cuda", true, 1)]
    [InlineData("auto", false, 0)]
    public void GpuSelection_ParsesExplicitDevices(string device, bool anomalib, int count)
        => Assert.Equal(count, GpuDeviceSelection.Parse(device, anomalib).Length);

    [Theory]
    [InlineData("0,0", false)]
    [InlineData("cuda:", true)]
    [InlineData("cuda:0,-1", true)]
    [InlineData("gpu", true)]
    public void GpuSelection_RejectsInvalidDevices(string device, bool anomalib)
        => Assert.Throws<ArgumentException>(() => GpuDeviceSelection.Parse(device, anomalib));

    [Fact]
    public void Padim_RejectsMultipleGpus_ButEfficientAdAllowsThem()
    {
        var options = new AnomalibTrainingOptions { Model = AnomalibModelKind.Padim, Device = "cuda:0,1" };
        Assert.Throws<ArgumentException>(options.Validate);

        options.Model = AnomalibModelKind.PatchCore;
        Assert.Throws<ArgumentException>(options.Validate);
        options.Device = "cuda:1";
        options.Validate();

        options.Model = AnomalibModelKind.EfficientAdSmall;
        options.Device = "cuda:0,1";
        options.Validate();
    }

    [Fact]
    public void PatchCore_TrainingPageSwitchesToSingleGpuAndKeepsSelectedDevice()
    {
        var page = new Snet.Yolo.Tasks.Components.Pages.AnomalibTrainPage();
        var type = page.GetType();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var device = type.GetField("_device", flags)!;
        device.SetValue(page, "cuda:0,1");
        type.GetMethod("SelectModel", flags)!.Invoke(page, [AnomalibModelKind.PatchCore]);
        Assert.Equal("cuda:0", device.GetValue(page));
        Assert.Equal("AnomalibPatchcoreFeatures", type.GetProperty("ModelFeaturesKey", flags)!.GetValue(page));
        type.GetMethod("ToggleGpu", flags)!.Invoke(page, [1]);
        Assert.Equal("cuda:1", device.GetValue(page));
        Assert.True((bool)type.GetProperty("SingleGpuModel", flags)!.GetValue(page)!);
    }

    [Fact]
    public void YoloMultiGpu_CommandUsesAllDevicesAndSingleGpuSelfCheck()
    {
        var options = new TrainingOptions { Device = "0,1,2" };
        var train = YoloCommandBuilder.BuildTrainArguments("data.yaml", options);
        var validation = YoloCommandBuilder.BuildValArguments("data.yaml", "best.pt", options);

        Assert.Contains("device=0,1,2", train);
        Assert.Contains("batch=15", train);
        Assert.Contains("device=0", validation);
    }

    [Fact]
    public void AnomalibPipeline_ConfiguresDistributedTrainingAndMainRankExport()
    {
        Assert.Contains("strategy=\"ddp\" if len(selected_devices) > 1 else \"auto\"", AnomalibPythonPipeline.Source);
        Assert.Contains("if not engine.trainer.is_global_zero:", AnomalibPythonPipeline.Source);
    }
}
