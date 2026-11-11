using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// YOLO 训练的显存方案：单卡交给 Ultralytics 官方的 AutoBatch（批大小变小时由 nbs 自动累积梯度，有效批大小不变），
/// 多卡给明确的全局批大小，原生 Linux 单卡减少显存碎片，显存不足时给出用户真能照着做的提示。
/// </summary>
public sealed class YoloVramPlanTests
{
    /// <summary>用默认选项构建指定设备的训练参数。</summary>
    private static IReadOnlyList<string> Train(string device)
        => YoloCommandBuilder.BuildTrainArguments("data.yaml", new TrainingOptions { Device = device });

    /// <summary>单卡训练只传一个 batch=-1，让 AutoBatch 按显存与尺寸选批大小，而不是固定的默认 16。</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void SingleGpu_LetsUltralyticsAutoBatchPickTheBatchSize(string device)
    {
        var arguments = Train(device);

        Assert.Equal("batch=-1", YoloCommandBuilder.AutoBatchArgument);
        Assert.Single(arguments, argument => argument.StartsWith("batch=", StringComparison.Ordinal));
        Assert.Contains(YoloCommandBuilder.AutoBatchArgument, arguments);
    }

    /// <summary>多卡（DDP）不支持 AutoBatch，仍然传能被卡数整除的明确全局批大小。</summary>
    [Theory]
    [InlineData("0,1", "batch=16")]
    [InlineData("0,1,2", "batch=15")]
    public void MultiGpu_KeepsAnExplicitGlobalBatch(string device, string expected)
    {
        var arguments = Train(device);

        Assert.Contains(expected, arguments);
        Assert.DoesNotContain(YoloCommandBuilder.AutoBatchArgument, arguments);
    }

    /// <summary>CPU 与 Apple 芯片上 AutoBatch 只会警告后退回 16，没有意义，所以不传 batch。</summary>
    [Theory]
    [InlineData("cpu")]
    [InlineData("mps")]
    [InlineData("auto")]
    public void WithoutACudaGpu_NoBatchArgumentIsPassed(string device)
        => Assert.DoesNotContain(Train(device), argument => argument.StartsWith("batch=", StringComparison.Ordinal));

    /// <summary>原生 Linux 单卡训练打开可扩展显存段，用 PyTorch 官方推荐的写法减少碎片。</summary>
    [Fact]
    public void ExpandableSegments_AreEnabledForSingleGpuOnNativeLinux()
    {
        var variables = YoloTrainingEnvironment.MemoryVariables("0", isNativeLinux: true, existingAllocatorConfig: null);

        Assert.Equal("PYTORCH_CUDA_ALLOC_CONF", YoloTrainingEnvironment.AllocatorConfigName);
        Assert.Equal("expandable_segments:True", Assert.Single(variables).Value);
        Assert.Equal(YoloTrainingEnvironment.AllocatorConfigName, Assert.Single(variables).Key);
    }

    /// <summary>Windows 与 WSL、多卡、CPU 都保持 PyTorch 默认行为；管理员自己配置过分配器时原样保留。</summary>
    [Theory]
    [InlineData("0", false, null)]
    [InlineData("0,1", true, null)]
    [InlineData("cpu", true, null)]
    [InlineData("mps", true, null)]
    [InlineData("0", true, "max_split_size_mb:128")]
    public void ExpandableSegments_AreLeftOffWhereTheyDoNotApply(string device, bool isNativeLinux, string? existing)
        => Assert.Empty(YoloTrainingEnvironment.MemoryVariables(device, isNativeLinux, existing));

    /// <summary>空白的环境变量等于没配置，仍然启用。</summary>
    [Fact]
    public void ExpandableSegments_TreatABlankExistingValueAsUnset()
        => Assert.NotEmpty(YoloTrainingEnvironment.MemoryVariables("0", isNativeLinux: true, existingAllocatorConfig: "  "));

    /// <summary>三种显存类失败（显存不足、AutoBatch 试到最小批大小也放不下、cuBLAS 申请显存失败）都给出同一条能照着做的提示。</summary>
    [Theory]
    [InlineData("torch.OutOfMemoryError: CUDA out of memory. Tried to allocate 1.75 GiB. GPU 0 has a total capacity of 15.99 GiB")]
    [InlineData("RuntimeError: AutoBatch: no usable batch size found while profiling batch=[1, 2, 4, 8, 16]. See the errors above")]
    [InlineData("RuntimeError: CUDA error: CUBLAS_STATUS_ALLOC_FAILED when calling `cublasCreate(handle)`")]
    public void MemoryFailures_GetAnActionableHint(string line)
    {
        var hint = TrainingService.SummarizeError(["1/50  2.6G  0.9  5.0  0.004  47  640", line], out var needsWeightCommand);

        Assert.NotNull(hint);
        Assert.StartsWith("显存不足", hint);
        Assert.Contains("训练尺寸（imgsz）", hint);
        Assert.Contains("更小的模型", hint);
        // 界面上没有批大小可调，不能再让用户去“调小 batch”。
        Assert.DoesNotContain("调小 imgsz / batch", hint);
        Assert.False(needsWeightCommand);
    }

    /// <summary>与显存无关的报错仍走原来的提示，不会被新增的显存规则误吞。</summary>
    [Fact]
    public void UnrelatedFailures_KeepTheirOwnHints()
    {
        Assert.Equal("训练脚本报错，详见上方日志。", TrainingService.SummarizeError(["Traceback", "ValueError: something else went wrong error"], out _));
    }
}
