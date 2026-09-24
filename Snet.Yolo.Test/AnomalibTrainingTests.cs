using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Anomalib 训练内核的稳定性、安全性与注册门禁测试。</summary>
public sealed class AnomalibTrainingTests
{
    /// <summary>工程页与训练页必须共用阶段展示，且阶段百分比不能伪装成实际轮数进度。</summary>
    [Theory]
    [InlineData(AnomalibTrainingPhase.Idle, "未开始", 0, false)]
    [InlineData(AnomalibTrainingPhase.PreparingEnvironment, "检查环境", 10, true)]
    [InlineData(AnomalibTrainingPhase.PreparingDataset, "准备数据", 25, true)]
    [InlineData(AnomalibTrainingPhase.Training, "训练中", 50, true)]
    [InlineData(AnomalibTrainingPhase.ValidatingParity, "一致性验证", 90, true)]
    [InlineData(AnomalibTrainingPhase.Complete, "已完成", 100, false)]
    [InlineData(AnomalibTrainingPhase.Failed, "失败", 0, false)]
    public void TrainingStatus_ProvidesConsistentStagePresentation(AnomalibTrainingPhase phase, string label, int percent, bool active)
    {
        var status = new AnomalibTrainingStatus { Phase = phase };

        Assert.Equal(label, status.PhaseLabel);
        Assert.Equal(percent, status.StagePercent);
        Assert.Equal(active, status.IsActive);
    }

    /// <summary>页面切换后读取的状态快照必须保留设备和模型参数，且日志不能共享可变集合。</summary>
    [Fact]
    public void TrainingStatus_ClonePreservesConfigurationAndCopiesLog()
    {
        var status = new AnomalibTrainingStatus
        {
            Model = AnomalibModelKind.EfficientAdSmall,
            Device = "cuda",
            ImageSize = 512,
            MaxEpochs = 80,
            LogTail = ["准备环境"],
        };

        var copy = status.Clone();

        Assert.Equal(status.Model, copy.Model);
        Assert.Equal("cuda", copy.Device);
        Assert.Equal(512, copy.ImageSize);
        Assert.Equal(80, copy.MaxEpochs);
        Assert.Equal(["准备环境"], copy.LogTail);
        Assert.NotSame(status.LogTail, copy.LogTail);
    }

    /// <summary>独立环境必须固定 Anomalib 版本，且不能复用 YOLO 的 train/.env。</summary>
    [Fact]
    public void EnvironmentPlan_UsesDedicatedPinnedEnvironment()
    {
        var plan = AnomalibEnvironmentPlanner.Create(@"C:\app", new PythonLauncher("py", ["-3"]), OsKind.Windows, "cpu");

        Assert.Equal("2.6.2", AnomalibEnvironmentPlanner.AnomalibVersion);
        Assert.EndsWith(Path.Combine("train", "anomalib", ".env"), plan.VenvDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.Combine("train", ".env") + Path.DirectorySeparatorChar, plan.VenvDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Steps.SelectMany(step => step.Command.ArgumentList), value => value == "anomalib==2.6.2");
        Assert.All(plan.Steps, step => Assert.NotNull(step.Command.ArgumentList));
        Assert.All(plan.Steps.Where(step => step.Kind is SetupStepKind.PipInstallTorch or SetupStepKind.PipInstallAnomalib),
            step => Assert.Contains("--no-cache-dir", step.Command.ArgumentList));
    }

    /// <summary>pip 在真正的错误后输出大量临时目录警告时，安装失败详情仍须保留先前根因。</summary>
    [Fact]
    public void TrainingShell_PreservesEarlyPipErrorBeforeCleanupWarnings()
    {
        var output = new TrainingShell.TailBuffer(120);
        output.Append("ERROR: Could not install packages due to an OSError: [Errno 28] No space left on device");
        for (var index = 0; index < 30; index++)
        {
            output.Append($"WARNING: Failed to remove contents in a temporary directory '/tmp/pip-unpack-{index}'.");
        }

        var failure = output.ToString();
        Assert.StartsWith("ERROR: Could not install packages", failure, StringComparison.Ordinal);
        Assert.Contains("No space left on device", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("pip-unpack-29", failure, StringComparison.Ordinal);
    }

    /// <summary>数据划分必须只由内容哈希和随机种子决定，不能受上传顺序影响。</summary>
    [Fact]
    public void DatasetPlanner_IsStableAcrossInputOrder()
    {
        var images = Enumerable.Range(0, 20)
            .Select(index => new AnomalibImageSource($"image-{index}.png", index.ToString("x64")))
            .ToArray();
        var options = new AnomalibTrainingOptions { CalibrationRatio = 0.2, RandomSeed = 42 };

        var first = AnomalibDatasetPlanner.Create(images, options);
        var second = AnomalibDatasetPlanner.Create(images.Reverse().ToArray(), options);

        Assert.Equal(16, first.TrainingImages.Count);
        Assert.Equal(4, first.CalibrationImages.Count);
        Assert.Equal(first.TrainingImages.Select(image => image.ContentSha256), second.TrainingImages.Select(image => image.ContentSha256));
        Assert.Equal(first.CalibrationImages.Select(image => image.ContentSha256), second.CalibrationImages.Select(image => image.ContentSha256));
        Assert.Empty(first.TrainingImages.Select(image => image.ContentSha256).Intersect(first.CalibrationImages.Select(image => image.ContentSha256)));
    }

    /// <summary>重复内容不能同时进入训练集和校准集，避免一致性样本泄漏。</summary>
    [Fact]
    public void DatasetPlanner_RemovesDuplicateContent()
    {
        var images = Enumerable.Range(0, 12)
            .Select(index => new AnomalibImageSource($"image-{index}.png", (index % 10).ToString("x64")))
            .ToArray();

        var split = AnomalibDatasetPlanner.Create(images, new AnomalibTrainingOptions { CalibrationRatio = 0.2 });

        Assert.Equal(10, split.TotalUniqueImages);
        Assert.Equal(10, split.TrainingImages.Concat(split.CalibrationImages).Select(image => image.ContentSha256).Distinct().Count());
    }

    /// <summary>三种首期模型都必须映射到明确的 Python 模型名，PatchCore 必须保留实验标识。</summary>
    [Theory]
    [InlineData(AnomalibModelKind.Padim, "padim", false)]
    [InlineData(AnomalibModelKind.EfficientAdSmall, "efficient_ad_small", false)]
    [InlineData(AnomalibModelKind.PatchcoreExperimental, "patchcore", true)]
    public void ModelDescriptor_MapsSupportedModels(AnomalibModelKind kind, string pythonName, bool experimental)
    {
        var descriptor = AnomalibModelCatalog.Get(kind);

        Assert.Equal(pythonName, descriptor.PythonName);
        Assert.Equal(experimental, descriptor.Experimental);
    }

    /// <summary>Python 调用必须使用独立参数项，带空格和 shell 元字符的路径不能被重新拼接执行。</summary>
    [Fact]
    public void CommandBuilder_UsesExplicitArgumentList()
    {
        var command = AnomalibCommandBuilder.BuildPipeline(
            @"C:\app dir\train\anomalib\.env\Scripts\python.exe",
            @"C:\app dir\train\anomalib\pipeline.py",
            @"C:\data & models\config.json",
            @"C:\data & models\result.json");

        Assert.Equal(@"C:\app dir\train\anomalib\.env\Scripts\python.exe", command.Executable);
        Assert.Equal([@"C:\app dir\train\anomalib\pipeline.py", "--config", @"C:\data & models\config.json", "--result", @"C:\data & models\result.json"], command.ArgumentList);
    }

    /// <summary>只有全部样本、分数、标签与掩码都通过时，模型才允许注册。</summary>
    [Fact]
    public void ParityParser_AcceptsPassingGate()
    {
        const string json = """
            {"status":"passed","sampleCount":5,"normalSampleCount":5,"normalFalsePositiveCount":0,"maxScoreDifference":0.0004,"minimumMaskIou":0.998,"labelMismatches":0,"errors":[]}
            """;

        var result = AnomalibParityResult.Parse(json);

        Assert.True(result.Passed);
        Assert.True(result.CanRegister);
        Assert.Equal(5, result.SampleCount);
    }

    /// <summary>Python 即使错误写出 passed，只要有标签不一致，C# 侧仍应关闭注册门禁。</summary>
    [Fact]
    public void ParityParser_RejectsInternallyInconsistentResult()
    {
        const string json = """
            {"status":"passed","sampleCount":5,"normalSampleCount":5,"normalFalsePositiveCount":0,"maxScoreDifference":0.0004,"minimumMaskIou":0.998,"labelMismatches":1,"errors":[]}
            """;

        var result = AnomalibParityResult.Parse(json);

        Assert.False(result.Passed);
        Assert.False(result.CanRegister);
        Assert.Contains("标签", result.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>校准集正常图片误报率超过百分之五时，即使两种模型输出一致也不得注册。</summary>
    [Theory]
    [InlineData(19, 1, false)]
    [InlineData(20, 1, true)]
    [InlineData(20, 2, false)]
    public void ParityParser_EnforcesNormalFalsePositiveGate(int count, int falsePositives, bool expected)
    {
        var json = $$"""{"status":"passed","sampleCount":{{count}},"normalSampleCount":{{count}},"normalFalsePositiveCount":{{falsePositives}},"maxScoreDifference":0,"minimumMaskIou":1,"labelMismatches":0,"errors":[]}""";

        var result = AnomalibParityResult.Parse(json);

        Assert.Equal(expected, result.CanRegister);
        if (!expected) { Assert.Contains("误报", result.FailureReason, StringComparison.Ordinal); }
    }

    /// <summary>零个样本时应优先呈现实际异常，避免笼统的样本数提示掩盖根因。</summary>
    [Fact]
    public void ParityFailure_PrefersUnderlyingErrorOverEmptySampleCount()
    {
        var result = AnomalibParityResult.Parse("""{"status":"failed","sampleCount":0,"maxScoreDifference":1,"minimumMaskIou":0,"labelMismatches":0,"errors":["model output is invalid"]}""");

        Assert.Equal("model output is invalid", result.FailureReason);
    }

    /// <summary>一致性脚本不能再通过旧推理器反序列化不可信的 PyTorch 模型文件。</summary>
    [Fact]
    public void PythonPipeline_UsesInMemoryModelWithoutPickleLoading()
    {
        Assert.DoesNotContain("TorchInferencer", AnomalibPythonPipeline.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("TRUST_REMOTE_CODE", AnomalibPythonPipeline.Source, StringComparison.Ordinal);
        Assert.Contains("torch.inference_mode()", AnomalibPythonPipeline.Source, StringComparison.Ordinal);
    }

    /// <summary>EfficientAD Small 使用 Anomalib 默认枚举尺寸，不能传入无效字符串 s。</summary>
    [Fact]
    public void PythonPipeline_UsesSupportedEfficientAdSmallSize()
    {
        Assert.Contains("return EfficientAd(pre_processor=", AnomalibPythonPipeline.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("model_size=\"s\"", AnomalibPythonPipeline.Source, StringComparison.Ordinal);
    }

    /// <summary>模型创建阶段失败时应报告流水线错误，不得误报为一致性验证失败。</summary>
    [Fact]
    public async Task TrainingService_ReportsPipelineFailureBeforeParity()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeRunner("""{"status":"failed","sampleCount":0,"errors":["'s' is not a valid EfficientAdModelSize"]}""", 1);
            var registrar = new FakeRegistrar();
            var service = new AnomalibTrainingService(runner, registrar);
            var result = await service.TrainAsync(new AnomalibTrainingRequest
            {
                Owner = "snet",
                ProjectId = "project-1",
                WorkingDirectory = root,
                PythonExecutable = "python",
                Images = CreateImageFiles(root, 10),
                Options = new AnomalibTrainingOptions(),
            }, null, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("训练流水线失败", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("一致性验证失败", result.Message, StringComparison.Ordinal);
            Assert.Contains("'s' is not a valid EfficientAdModelSize", result.Message, StringComparison.Ordinal);
            Assert.Equal(0, registrar.CallCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>非零退出且写出结构化失败时应展示校准样本的真正错误，仍禁止注册。</summary>
    [Fact]
    public async Task TrainingService_ShowsStructuredParityError_WhenProcessFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeRunner("""{"status":"failed","sampleCount":0,"maxScoreDifference":1,"minimumMaskIou":0,"labelMismatches":0,"errors":["sample.png: ONNX output missing"]}""", 3);
            var registrar = new FakeRegistrar();
            var service = new AnomalibTrainingService(runner, registrar);
            var result = await service.TrainAsync(new AnomalibTrainingRequest
            {
                Owner = "snet",
                ProjectId = "project-1",
                WorkingDirectory = root,
                PythonExecutable = "python",
                Images = CreateImageFiles(root, 10),
                Options = new AnomalibTrainingOptions(),
            }, null, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("sample.png: ONNX output missing", result.Message, StringComparison.Ordinal);
            Assert.Equal(0, registrar.CallCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>一致性失败时服务不得调用模型注册器。</summary>
    [Fact]
    public async Task TrainingService_DoesNotRegister_WhenParityFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeRunner("""
                {"status":"failed","sampleCount":2,"maxScoreDifference":0.8,"minimumMaskIou":0.1,"labelMismatches":1,"errors":["score mismatch"]}
                """, parityReached: true);
            var registrar = new FakeRegistrar();
            var service = new AnomalibTrainingService(runner, registrar);
            var request = new AnomalibTrainingRequest
            {
                Owner = "snet",
                ProjectId = "project-1",
                WorkingDirectory = root,
                PythonExecutable = "python",
                Images = CreateImageFiles(root, 10),
                Options = new AnomalibTrainingOptions(),
            };

            var result = await service.TrainAsync(request, null, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.Parity.CanRegister);
            Assert.Contains("一致性验证失败", result.Message, StringComparison.Ordinal);
            Assert.Equal(0, registrar.CallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>即使结果文件声称通过，Python 进程非零退出也不得注册模型。</summary>
    [Fact]
    public async Task TrainingService_DoesNotRegister_WhenProcessFailsAfterWritingPass()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeRunner("""{"status":"passed","sampleCount":5,"normalSampleCount":5,"normalFalsePositiveCount":0,"maxScoreDifference":0,"minimumMaskIou":1,"labelMismatches":0,"errors":[]}""", 2);
            var registrar = new FakeRegistrar();
            var service = new AnomalibTrainingService(runner, registrar);
            var result = await service.TrainAsync(new AnomalibTrainingRequest
            {
                Owner = "snet",
                ProjectId = "project-1",
                WorkingDirectory = root,
                PythonExecutable = "python",
                Images = CreateImageFiles(root, 10),
                Options = new AnomalibTrainingOptions(),
            }, null, CancellationToken.None);
            Assert.False(result.Succeeded);
            Assert.Equal(0, registrar.CallCount);
            Assert.Empty(Directory.EnumerateDirectories(root, "anomalib-run-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>正常图上传可接受 GIF 时，训练也应通过同一格式边界继续到流水线。</summary>
    [Fact]
    public async Task TrainingService_AcceptsUploadedGifImage()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var images = CreateImageFiles(root, 10).ToArray();
            var gif = Path.ChangeExtension(images[0], ".gif");
            File.Move(images[0], gif);
            images[0] = gif;
            var runner = new FakeRunner("""{"status":"failed","sampleCount":0,"normalSampleCount":0,"normalFalsePositiveCount":0,"maxScoreDifference":1,"minimumMaskIou":0,"labelMismatches":0,"errors":["pipeline reached"]}""", 3);
            var service = new AnomalibTrainingService(runner, new FakeRegistrar());

            var result = await service.TrainAsync(new AnomalibTrainingRequest
            {
                Owner = "snet",
                ProjectId = "project-1",
                WorkingDirectory = root,
                PythonExecutable = "python",
                Images = images,
                Options = new AnomalibTrainingOptions(),
            }, null, CancellationToken.None);

            Assert.Contains("pipeline reached", result.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>创建具有不同内容的最小图片占位文件，供服务的数据集复制流程使用。</summary>
    private static IReadOnlyList<string> CreateImageFiles(string root, int count)
    {
        var paths = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(root, $"source-{index}.png");
            File.WriteAllBytes(path, [(byte)index, 1, 2, 3]);
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>写入预设门禁结果的进程运行器。</summary>
    private sealed class FakeRunner(string resultJson, int exitCode = 0, bool parityReached = false) : IAnomalibProcessRunner
    {
        /// <summary>模拟成功执行 Python，并写出一致性结果。</summary>
        public Task<AnomalibProcessResult> RunAsync(AnomalibCommand command, string workingDirectory, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            if (parityReached) { onOutput?.Invoke("VISUALIDENTITY_PHASE:parity"); }
            var resultPath = command.ArgumentList[4];
            File.WriteAllText(resultPath, resultJson);
            return Task.FromResult(new AnomalibProcessResult(exitCode, "done"));
        }
    }

    /// <summary>记录注册调用次数的模型注册器。</summary>
    private sealed class FakeRegistrar : IAnomalibModelRegistrar
    {
        /// <summary>模型注册调用次数。</summary>
        public int CallCount { get; private set; }

        /// <summary>记录一次注册调用。</summary>
        public Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
