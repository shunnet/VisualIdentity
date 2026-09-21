using Microsoft.Extensions.Configuration;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 训练特性的跨平台 / 代理相关纯逻辑测试（不依赖 GPU、网络与真实进程）。
/// 需求：Windows/Linux/macOS 规划结果、代理重试阶梯、nvidia-smi 解析、含空格路径的 argv。
/// </summary>
[Collection("Database")]
public sealed class TrainingCrossPlatformTests
{
    private static TrainingEnvSnapshot Snapshot(OsKind os, bool withTorch = true, bool withUltralytics = true)
        => new()
        {
            Os = os,
            PythonCmd = os == OsKind.Windows ? "python" : "python3",
            HasPython = true,
            HasPip = true,
            HasVenv = true,
            VenvPath = os == OsKind.Windows ? @"C:\app\train\.env" : "/app/train/.env",
            VenvDirectoryExists = true,
            VenvExists = true,
            VenvHasTorch = withTorch,
            VenvHasUltralytics = withUltralytics,
        };

    private static TrainingEnvSnapshot WindowsCuda(double? computeCap)
    {
        var snap = Snapshot(OsKind.Windows, withTorch: false, withUltralytics: false);
        snap.Gpu = new GpuInfo("NVIDIA GeForce RTX 4090", computeCap is null ? string.Empty : computeCap.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "551.86", 24564, null);
        return snap;
    }

    // ── TASK B1：各系统的 PyTorch 安装通道 ────────────────────────────────────

    [Fact]
    public void WindowsWithCuda_UsesCudaChannelAndCudaIndex()
    {
        var plan = TrainEnvironmentPlanner.Plan(WindowsCuda(8.9));

        Assert.True(plan.UseGpu);
        Assert.True(plan.Accelerated);
        Assert.Equal("0", plan.Device);
        Assert.Equal("cu128", plan.CudaVersion);
        Assert.Null(plan.Warning);

        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        var index = torch.Command.ArgumentList.ToList().IndexOf("--index-url");
        Assert.True(index >= 0);
        Assert.Equal("https://download.pytorch.org/whl/cu128", torch.Command.ArgumentList[index + 1]);
        Assert.Contains("torch==2.9.0", torch.Command.ArgumentList);
        Assert.True(torch.Command.IsNetwork);
    }

    [Fact]
    public void LinuxWithOlderCuda_UsesConservativeCompatibleChannel()
    {
        var snap = Snapshot(OsKind.Linux, withTorch: false, withUltralytics: false);
        snap.Gpu = new GpuInfo("Tesla T4", "7.5", "535.104.05", 15360, null);

        var plan = TrainEnvironmentPlanner.Plan(snap);

        Assert.True(plan.UseGpu);
        Assert.Equal("cu118", plan.CudaVersion);
        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        Assert.Contains("https://download.pytorch.org/whl/cu118", torch.Command.ArgumentList);
        Assert.Contains("torch==2.7.1", torch.Command.ArgumentList);
    }

    [Fact]
    public void WindowsWithoutGpu_InstallCpuWheelsAndWarn()
    {
        var snap = Snapshot(OsKind.Windows, withTorch: false, withUltralytics: false);

        var plan = TrainEnvironmentPlanner.Plan(snap);

        Assert.False(plan.UseGpu);
        Assert.False(plan.Accelerated);
        Assert.Equal("cpu", plan.Device);
        Assert.Equal("cpu", plan.CudaVersion);
        Assert.NotNull(plan.Warning);
        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        Assert.Contains("https://download.pytorch.org/whl/cpu", torch.Command.ArgumentList);
    }

    [Fact]
    public void GpuWithoutComputeCap_StaysOnGpuWithConservativeChannel()
    {
        // 旧驱动不支持 compute_cap：不能静默降级为 CPU 训练
        var plan = TrainEnvironmentPlanner.Plan(WindowsCuda(computeCap: null));

        Assert.True(plan.UseGpu);
        Assert.Equal(CudaMapping.ConservativeChannel, plan.CudaVersion);
        Assert.Equal("0", plan.Device);
        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        Assert.Contains("https://download.pytorch.org/whl/" + CudaMapping.ConservativeChannel, torch.Command.ArgumentList);
    }

    [Fact]
    public void MacWithMps_SelectsMpsWithoutCudaIndex()
    {
        var snap = Snapshot(OsKind.Mac, withTorch: false, withUltralytics: false);
        snap.HasMps = true;

        var plan = TrainEnvironmentPlanner.Plan(snap);

        // MPS 不是 CUDA：UseGpu 必须为 false，但仍属于硬件加速
        Assert.False(plan.UseGpu);
        Assert.True(plan.Accelerated);
        Assert.Equal("mps", plan.Device);
        Assert.Equal("mps", plan.CudaVersion);
        Assert.Null(plan.Warning);

        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        Assert.DoesNotContain("--index-url", torch.Command.ArgumentList);
        Assert.DoesNotContain(torch.Command.ArgumentList, argument => argument.Contains("pytorch.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Apple MPS", torch.Description);
    }

    [Fact]
    public void MacWithoutMps_FallsBackToCpuAndDefaultPypi()
    {
        var snap = Snapshot(OsKind.Mac, withTorch: false, withUltralytics: false);

        var plan = TrainEnvironmentPlanner.Plan(snap);

        Assert.False(plan.UseGpu);
        Assert.False(plan.Accelerated);
        Assert.Equal("cpu", plan.Device);
        Assert.NotNull(plan.Warning);
        var torch = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        // macOS 的 arm64 wheel 只在默认 PyPI 索引提供
        Assert.DoesNotContain("--index-url", torch.Command.ArgumentList);
    }

    [Theory]
    [InlineData(OsKind.Windows, "cpu", 5.2)]
    [InlineData(OsKind.Linux, "cu118", 6.1)]
    [InlineData(OsKind.Linux, "cu118", 7.5)]
    [InlineData(OsKind.Windows, "cu128", 8.6)]
    public void CudaMapping_MapsComputeCapabilityToWheelChannel(OsKind os, string expected, double computeCap)
    {
        var snap = Snapshot(os, withTorch: false, withUltralytics: false);
        snap.Gpu = new GpuInfo("GPU", computeCap.ToString(System.Globalization.CultureInfo.InvariantCulture), os == OsKind.Windows ? "551.86" : "535.104.05", 8192, null);

        var plan = TrainEnvironmentPlanner.Plan(snap);

        Assert.Equal(expected, plan.CudaVersion);
        Assert.Equal(expected != "cpu", plan.UseGpu);
    }

    [Theory]
    [InlineData(OsKind.Linux, "524.99", "cu118")]
    [InlineData(OsKind.Linux, "525.60.13", "cu128")]
    [InlineData(OsKind.Windows, "526.99", "cu118")]
    [InlineData(OsKind.Windows, "527.41", "cu128")]
    public void CudaMapping_UsesDriverCompatibleChannel(OsKind os, string driver, string expected)
        => Assert.Equal(expected, CudaMapping.Select(8.6, driver, os));

    [Fact]
    public void GpuEnvironment_WithCpuTorch_IsNotReadyAndGetsRebuilt()
    {
        var snap = WindowsCuda(8.9);
        snap.VenvExists = true;
        snap.VenvHasTorch = true;
        snap.VenvHasUltralytics = true;
        snap.TorchCudaVersion = null;
        snap.TorchCudaAvailable = false;

        var plan = TrainEnvironmentPlanner.Plan(snap);

        Assert.False(plan.EnvReady);
        Assert.Contains(plan.Steps, step => step.Kind == SetupStepKind.RecreateVenv);
        Assert.Contains(plan.Steps, step => step.Kind == SetupStepKind.PipInstallTorch);
    }

    [Theory]
    [InlineData("12.8\nTrue\n", "12.8", true)]
    [InlineData("cpu\nFalse\n", null, false)]
    public void TorchEnvironmentProbe_ParsesCudaBuildAndAvailability(string output, string? version, bool available)
    {
        var result = TorchRuntimeProbe.ParseEnvironment(output, string.Empty);
        Assert.Equal(version, result.CudaVersion);
        Assert.Equal(available, result.Available);
    }

    [Fact]
    public void ReadyEnvironment_ProducesNoSetupSteps()
    {
        var plan = TrainEnvironmentPlanner.Plan(Snapshot(OsKind.Linux));

        Assert.True(plan.EnvReady);
        Assert.Empty(plan.Steps);
    }

    [Theory]
    [InlineData(OsKind.Windows, "winget install")]
    [InlineData(OsKind.Mac, "brew install python")]
    [InlineData(OsKind.Linux, "sudo apt install -y python3 python3-pip python3-venv")]
    public void MissingPython_EmitsOsSpecificRemediation(OsKind os, string expected)
    {
        var snap = Snapshot(os, withTorch: false, withUltralytics: false);
        snap.HasPython = false;
        snap.HasVenv = false;

        var plan = TrainEnvironmentPlanner.Plan(snap);

        var info = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.Info);
        Assert.Contains(expected, info.Description);
        Assert.Empty(info.Command.Executable);
        Assert.True(plan.Steps.Count > 1); // 仍需创建 venv 并安装依赖
    }

    [Fact]
    public void MissingVenvModule_EmitsDebianAndFedoraRemediation()
    {
        var snap = Snapshot(OsKind.Linux, withUltralytics: false);
        snap.HasVenv = false;

        var plan = TrainEnvironmentPlanner.Plan(snap);

        var info = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.Info);
        Assert.Contains("python3-venv", info.Description);
        Assert.Contains("dnf install", info.Description);
    }

    [Fact]
    public void VenvStep_PassesPathAsSingleArgumentForBothLayouts()
    {
        foreach (var os in new[] { OsKind.Windows, OsKind.Linux, OsKind.Mac })
        {
            var snap = Snapshot(os, withTorch: false, withUltralytics: false);
            var plan = TrainEnvironmentPlanner.Plan(snap);
            var venv = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.RecreateVenv);

            var args = venv.Command.ArgumentList;
            Assert.Equal("-m", args[args.Count - 3]);
            Assert.Equal("venv", args[args.Count - 2]);
            Assert.Equal(snap.VenvPath, args[args.Count - 1]);
            Assert.DoesNotContain("\"", args[args.Count - 1]);
            // venv 内解释器/入口脚本的路径按系统区分
            Assert.Equal(os == OsKind.Windows, plan.VenvPython.EndsWith("python.exe", StringComparison.Ordinal));
            Assert.Equal(os == OsKind.Windows, plan.VenvYolo.EndsWith("yolo.exe", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PyVenvLauncherPrefixArgumentsSurviveIntoVenvCommand()
    {
        var snap = Snapshot(OsKind.Windows, withTorch: false, withUltralytics: false);
        snap.PythonCmd = "py";
        snap.PythonArguments = new[] { "-3" };

        var plan = TrainEnvironmentPlanner.Plan(snap);
        var venv = Assert.Single(plan.Steps, step => step.Kind == SetupStepKind.RecreateVenv);

        Assert.Equal("py", venv.Command.Executable);
        Assert.Equal(new[] { "-3", "-m", "venv", snap.VenvPath }, venv.Command.ArgumentList);
    }

    // ── TASK A：代理重试阶梯 ────────────────────────────────────────────────

    [Fact]
    public void PipAttemptOne_KeepsPlannedArgumentsAndInheritsEnvironment()
    {
        var planned = new[] { "-m", "pip", "install", "torch", "--index-url", "https://download.pytorch.org/whl/cu124" };

        var args = PipProxyPolicy.BuildArguments(planned, attempt: 1, configuredProxy: null);

        Assert.Equal(planned, args);
        Assert.Null(PipProxyPolicy.BuildEnvironmentOverrides(1));
    }

    [Fact]
    public void PipAttemptOne_WithConfiguredProxy_AppendsProxyArgument()
    {
        var planned = new[] { "-m", "pip", "install", "ultralytics" };

        var args = PipProxyPolicy.BuildArguments(planned, attempt: 1, configuredProxy: "  http://10.0.0.9:8080  ");

        Assert.Equal(new[] { "-m", "pip", "install", "ultralytics", "--proxy", "http://10.0.0.9:8080" }, args);
    }

    [Fact]
    public void PipAttemptOne_IgnoresBlankConfiguredProxy()
    {
        var planned = new[] { "-m", "pip", "install", "ultralytics" };

        Assert.Equal(planned, PipProxyPolicy.BuildArguments(planned, 1, "   "));
        Assert.Equal(planned, PipProxyPolicy.BuildArguments(planned, 1, string.Empty));
    }

    [Fact]
    public void PipAttemptTwo_DisablesProxyExplicitly()
    {
        var planned = new[] { "-m", "pip", "install", "torch" };

        var args = PipProxyPolicy.BuildArguments(planned, attempt: 2, configuredProxy: "http://10.0.0.9:8080");

        // --proxy "" 必须以两个独立参数下发（空字符串是 pip 认可的“禁用代理”写法）
        Assert.Equal(new[] { "-m", "pip", "install", "torch", "--proxy", string.Empty }, args);
        Assert.Equal(string.Empty, args[args.Count - 1]);
        // 日志里仍应可读地显示为 --proxy ""
        Assert.Equal("-m pip install torch --proxy \"\"", CommandLine.JoinArguments(args));
    }

    [Fact]
    public void PipAttemptTwo_RemovesEveryProxyVariableAndSetsNoProxy()
    {
        var overrides = PipProxyPolicy.BuildEnvironmentOverrides(2);

        Assert.NotNull(overrides);
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
        {
            Assert.True(overrides!.ContainsKey(name), name);
            Assert.Null(overrides[name]);
        }
        foreach (var name in new[] { "NO_PROXY", "no_proxy" })
        {
            Assert.Equal("*", overrides![name]);
        }
    }

    [Fact]
    public async Task ProxyDisabledAttempt_RunsChildProcessWithoutProxyEnvironment()
    {
        // 真实复现客户现场：父进程带有失效代理，第 2 次尝试必须在子进程中彻底清除
        var names = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names) { Environment.SetEnvironmentVariable(name, "http://dead.proxy.invalid:9"); }
            Environment.SetEnvironmentVariable("NO_PROXY", null);

            var executable = OperatingSystem.IsWindows() ? "cmd.exe" : "/usr/bin/env";
            IReadOnlyList<string> arguments = OperatingSystem.IsWindows() ? new[] { "/c", "set" } : Array.Empty<string>();

            var (code, stdout, stderr) = await TrainingShell.RunAsync(executable, arguments, null, PipProxyPolicy.BuildEnvironmentOverrides(2));

            Assert.Equal(0, code);
            var output = stdout + "\n" + stderr;
            foreach (var name in names)
            {
                Assert.DoesNotContain(name + "=", output, StringComparison.OrdinalIgnoreCase);
            }
            // 正向证据：覆盖字典确实传给了子进程
            Assert.Contains("NO_PROXY=*", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var pair in previous) { Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
            Environment.SetEnvironmentVariable("NO_PROXY", null);
            Environment.SetEnvironmentVariable("no_proxy", null);
        }
    }

    [Fact]
    public void ProxyFailureMessage_NamesCauseAndRemedies()
    {
        var message = PipProxyPolicy.FailureMessage("安装 GPU(CUDA cu124) 版 PyTorch");

        Assert.Contains("代理", message);
        Assert.Contains("Training:Proxy", message);
        Assert.Contains("appsettings.json", message);
        Assert.Contains("pip.conf", message);
        Assert.Equal(2, PipProxyPolicy.MaxAttempts);
    }

    [Fact]
    public void NetworkStepsAreFlaggedAndNonNetworkStepsAreNot()
    {
        var plan = TrainEnvironmentPlanner.Plan(Snapshot(OsKind.Linux, withTorch: false, withUltralytics: false));

        Assert.All(plan.Steps.Where(step => step.Kind is SetupStepKind.PipInstallTorch or SetupStepKind.PipInstallYolo), step => Assert.True(step.IsNetwork, step.Description));
        Assert.All(plan.Steps.Where(step => step.Kind == SetupStepKind.RecreateVenv), step => Assert.False(step.IsNetwork, step.Description));
    }

    // ── TASK A3：Training:Proxy 配置读取 ────────────────────────────────────

    [Fact]
    public void ReadProxy_ReturnsNullWhenSectionIsMissingOrBlank()
    {
        Assert.Null(TrainingService.ReadProxy(null));
        Assert.Null(TrainingService.ReadProxy(new ConfigurationBuilder().Build()));
        Assert.Null(TrainingService.ReadProxy(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Training:Proxy"] = "" }).Build()));
        Assert.Null(TrainingService.ReadProxy(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Training:Proxy"] = "   " }).Build()));
    }

    [Fact]
    public void ReadProxy_TrimsConfiguredValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Training:Proxy"] = " http://proxy.local:3128 " })
            .Build();

        Assert.Equal("http://proxy.local:3128", TrainingService.ReadProxy(configuration));
    }

    // ── TASK B4：nvidia-smi 解析 ────────────────────────────────────────────

    [Fact]
    public void CudaMapping_UnknownCapabilityIsConservativeNotCpu()
    {
        Assert.Equal("cpu", CudaMapping.Map(null));
        Assert.Equal(CudaMapping.ConservativeChannel, CudaMapping.MapUnknown());
        Assert.Null(CudaMapping.TryParse("  "));
        Assert.Null(CudaMapping.TryParse(null));
        Assert.Equal(8.6, CudaMapping.TryParse("8.6"));
    }

    [Fact]
    public void NvidiaSmiCandidates_TryPathFirstThenAbsoluteLocations()
    {
        var linux = NvidiaSmi.CandidateExecutables(OsKind.Linux);
        Assert.Equal("nvidia-smi", linux[0]);
        Assert.Contains("/usr/bin/nvidia-smi", linux);
        Assert.Contains("/usr/local/bin/nvidia-smi", linux);
        Assert.Contains("/usr/local/nvidia/bin/nvidia-smi", linux);
        Assert.Contains("/usr/lib/wsl/lib/nvidia-smi", linux);

        var windows = NvidiaSmi.CandidateExecutables(OsKind.Windows);
        Assert.Equal("nvidia-smi", windows[0]);
        Assert.Contains(@"C:\Windows\System32\nvidia-smi.exe", windows);

        Assert.Contains("--query-gpu=name,compute_cap,driver_version,memory.total", NvidiaSmi.RichQuery);
        Assert.Contains("--query-gpu=name,driver_version,memory.total", NvidiaSmi.LegacyQuery);
        Assert.Equal("--format=csv", NvidiaSmi.RichQuery[NvidiaSmi.RichQuery.Count - 1]);
        Assert.Equal("--format=csv", NvidiaSmi.LegacyQuery[NvidiaSmi.LegacyQuery.Count - 1]);
    }

    [Fact]
    public void NvidiaSmiParser_ReadsComputeCapWhenAvailable()
    {
        const string output = "name, compute_cap, driver_version, memory.total\n"
            + "NVIDIA GeForce RTX 4090, 8.9, 551.86, 24564 MiB\n";

        var gpu = Assert.Single(NvidiaSmiParser.ParseCsv(output));

        Assert.Equal("NVIDIA GeForce RTX 4090", gpu.Name);
        Assert.Equal("8.9", gpu.ComputeCap);
        Assert.Equal("551.86", gpu.DriverVersion);
        Assert.Equal(24564L, gpu.MemoryMb);
        Assert.True(gpu.HasGpu);
        Assert.Equal(8.9, gpu.ComputeCapParsed);
    }

    [Fact]
    public void NvidiaSmiParser_HandlesDriverWithoutComputeCapColumn()
    {
        // 旧驱动的降级查询：列数不同，且绝不能把 driver_version 当成 compute_cap
        const string output = "name, driver_version, memory.total\n"
            + "Tesla V100-SXM2-16GB, 470.57.02, 16160 MiB\n";

        var gpu = Assert.Single(NvidiaSmiParser.ParseCsv(output));

        Assert.Equal("Tesla V100-SXM2-16GB", gpu.Name);
        Assert.Equal(string.Empty, gpu.ComputeCap);
        Assert.Equal("470.57.02", gpu.DriverVersion);
        Assert.Equal(16160L, gpu.MemoryMb);
        Assert.Null(gpu.ComputeCapParsed);
        Assert.True(gpu.HasGpu);
    }

    [Fact]
    public void NvidiaSmiParser_ReadsMultipleGpus()
    {
        const string output = "name, compute_cap, driver_version, memory.total\n"
            + "NVIDIA GeForce RTX 3090, 8.6, 550.54, 24576 MiB\n"
            + "NVIDIA GeForce RTX 3060, 8.6, 550.54, 12288 MiB\n";

        var gpus = NvidiaSmiParser.ParseCsv(output);

        Assert.Equal(2, gpus.Count);
        Assert.Equal("NVIDIA GeForce RTX 3090", gpus[0].Name);
        Assert.Equal("NVIDIA GeForce RTX 3060", gpus[1].Name);
        Assert.Equal(12288L, gpus[1].MemoryMb);
    }

    [Fact]
    public void NvidiaSmiParser_HandlesHeaderlessAndDegenerateOutput()
    {
        Assert.Empty(NvidiaSmiParser.ParseCsv(string.Empty));
        Assert.Empty(NvidiaSmiParser.ParseCsv("   \n  \n"));
        Assert.Empty(NvidiaSmiParser.ParseCsv("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver."));

        // --format=csv,noheader 且含 compute_cap
        var withCap = Assert.Single(NvidiaSmiParser.ParseCsv("RTX 2080 Ti, 7.5, 512.15, 11264 MiB\n"));
        Assert.Equal("7.5", withCap.ComputeCap);

        // --format=csv,noheader 且不含 compute_cap
        var withoutCap = Assert.Single(NvidiaSmiParser.ParseCsv("RTX 2080 Ti, 512.15, 11264 MiB\n"));
        Assert.Equal(string.Empty, withoutCap.ComputeCap);
        Assert.Equal("512.15", withoutCap.DriverVersion);
    }

    // ── TASK B5：含空格路径的 argv 构建 ─────────────────────────────────────

    [Fact]
    public void BuildTrainArguments_KeepsSpaceContainingPathInOneElement()
    {
        var options = new TrainingOptions { Task = "detect", Model = "yolo26n.pt", Epochs = 50, ImgSize = 640, Device = "0" };
        const string dataYaml = @"C:\Program Files\Visual Identity\train\data.yaml";

        var args = YoloCommandBuilder.BuildTrainArguments(dataYaml, options);

        Assert.Equal(8, args.Count);
        Assert.Equal("data=" + dataYaml, args[2]);
        Assert.Single(args, argument => argument.Contains("data.yaml", StringComparison.Ordinal));
        Assert.DoesNotContain("data.yaml", args);
        Assert.Equal("model=yolo26n.pt", args[3]);
    }

    [Fact]
    public void BuildTrainArguments_HandlesUnixPathWithSpaces()
    {
        var options = new TrainingOptions { Task = "segment", Model = "yolo11n-seg.pt", Epochs = 3, ImgSize = 320, Device = "mps" };
        const string dataYaml = "/home/ys/my tasks/VisualIdentity/train/data.yaml";

        var args = YoloCommandBuilder.BuildTrainArguments(dataYaml, options);

        Assert.Equal("segment", args[0]);
        Assert.Equal("train", args[1]);
        Assert.Equal("data=" + dataYaml, args[2]);
        Assert.Equal("device=mps", args[6]);
    }

    [Fact]
    public void BuildValArguments_KeepsSpaceContainingModelInOneElement()
    {
        var options = new TrainingOptions { Task = "pose", Model = "yolo11m-pose.pt", ImgSize = 640, Device = "cpu" };
        const string modelPath = @"C:\Program Files\runs\pose\train\weights\best.pt";

        var args = YoloCommandBuilder.BuildValArguments("data.yaml", modelPath, options);

        Assert.Equal(new[] { "pose", "val", "data=data.yaml", "model=" + modelPath, "imgsz=640", "device=cpu", "verbose=True" }, args);
    }

    [Fact]
    public void BuildExportArguments_KeepsSpaceContainingModelInOneElement()
    {
        var args = YoloCommandBuilder.BuildExportArguments(@"C:\Program Files\runs\best.pt", 18);

        Assert.Equal(new[] { "export", @"model=C:\Program Files\runs\best.pt", "format=onnx", "imgsz=640", "opset=18" }, args);
    }

    [Fact]
    public void LoggedCommandLine_QuotesOnlyArgumentsThatNeedIt()
    {
        var options = new TrainingOptions { Task = "detect", Model = "yolo26n.pt", Epochs = 10, ImgSize = 640, Device = "cpu" };

        var plain = YoloCommandBuilder.BuildTrain("yolo", "data.yaml", options);
        Assert.StartsWith("yolo detect train data=data.yaml model=yolo26n.pt epochs=10 imgsz=640 device=cpu verbose=True", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", plain, StringComparison.Ordinal);

        var spaced = YoloCommandBuilder.BuildTrain("yolo", @"C:\My Data\data.yaml", options);
        // 与既有日志格式一致：只给 key=value 中的 value 加引号
        Assert.Contains("data=\"C:\\My Data\\data.yaml\"", spaced, StringComparison.Ordinal);
        Assert.DoesNotContain(" task=", spaced, StringComparison.Ordinal);
        Assert.Equal("data=\"C:\\My Data\\data.yaml\"", CommandLine.Quote(@"data=C:\My Data\data.yaml"));
        Assert.Equal("--index-url", CommandLine.Quote("--index-url"));
        Assert.Equal("x=\"a b\"", CommandLine.Quote("x=a b"));
    }

    [Theory]
    [InlineData("detect", "detect")]
    [InlineData("SEGMENT", "segment")]
    [InlineData("obb", "obb")]
    [InlineData("unknown", "detect")]
    [InlineData(null, "detect")]
    public void BuildTrainArguments_NormalizesTask(string? task, string expected)
    {
        var args = YoloCommandBuilder.BuildTrainArguments("data.yaml", new TrainingOptions { Task = task ?? string.Empty });

        Assert.Equal(expected, args[0]);
    }

    // ── TASK B2/B3：Python 探测与 venv 重建 ─────────────────────────────────

    [Fact]
    public void PythonCandidates_OrderMatchesPlatformConventions()
    {
        Assert.Equal(new[] { "python", "py" }, PythonDiscovery.Candidates(OsKind.Windows).Select(c => c.Executable));
        Assert.Equal(new[] { "-3" }, PythonDiscovery.Candidates(OsKind.Windows)[1].PrefixArguments);
        Assert.Equal(new[] { "python3", "python" }, PythonDiscovery.Candidates(OsKind.Linux).Select(c => c.Executable));
        Assert.Equal(new[] { "python3", "python" }, PythonDiscovery.Candidates(OsKind.Mac).Select(c => c.Executable));
    }

    [Fact]
    public void PythonLauncher_CombinesPrefixAndRequestedArguments()
    {
        var launcher = new PythonLauncher("py", new[] { "-3" });

        Assert.Equal(new[] { "-3", "-m", "venv", "--help" }, launcher.WithArguments(PythonDiscovery.VenvProbeArguments));
        Assert.Equal(new[] { "-m", "venv", "--help" }, new PythonLauncher("python3", Array.Empty<string>()).WithArguments(PythonDiscovery.VenvProbeArguments));
    }

    [Theory]
    [InlineData("Python 3.12.1", "", true)]
    [InlineData("", "Python 3.10.12", true)]
    [InlineData("Python 2.7.18", "", false)]
    [InlineData("", "", false)]
    public void IsPython3_RejectsLegacyInterpreters(string stdout, string stderr, bool expected)
    {
        Assert.Equal(expected, PythonDiscovery.IsPython3(stdout, stderr));
    }

    [Fact]
    public void VenvRebuilder_ResetRemovesStaleDirectorySoVenvCannotReuseIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "vi-venv-test-" + Guid.NewGuid().ToString("N"));
        var venv = Path.Combine(root, ".env");
        Directory.CreateDirectory(Path.Combine(venv, "bin"));
        File.WriteAllText(Path.Combine(venv, "bin", "python"), "stale");

        try
        {
            VenvRebuilder.Reset(venv);

            // 半损坏的 venv 必须先整目录删除，否则 python -m venv 只会复用旧目录
            Assert.False(Directory.Exists(venv));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }

    [Fact]
    public void VenvRebuilder_ResetCreatesMissingParentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vi-venv-test-" + Guid.NewGuid().ToString("N"));
        var venv = Path.Combine(root, "train", ".env");

        try
        {
            VenvRebuilder.Reset(venv);

            Assert.True(Directory.Exists(Path.Combine(root, "train")));
            Assert.False(Directory.Exists(venv));
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }

    // ── TASK B1：设备选择 ──────────────────────────────────────────────────

    [Theory]
    [InlineData(OsKind.Windows, true, "0")]
    [InlineData(OsKind.Linux, true, "0")]
    [InlineData(OsKind.Windows, false, "cpu")]
    [InlineData(OsKind.Mac, true, "mps")]
    [InlineData(OsKind.Mac, false, "cpu")]
    public void TorchRuntimeProbe_SelectsDevicePerPlatform(OsKind os, bool available, string expected)
    {
        Assert.Equal(expected, TorchRuntimeProbe.SelectDevice(os, available));
    }

    [Fact]
    public void TorchRuntimeProbe_UsesMpsCheckOnlyOnMac()
    {
        Assert.Contains("torch.backends.mps.is_available()", TorchRuntimeProbe.Script(OsKind.Mac), StringComparison.Ordinal);
        Assert.Contains("torch.cuda.is_available()", TorchRuntimeProbe.Script(OsKind.Linux), StringComparison.Ordinal);
        Assert.Contains("torch.cuda.is_available()", TorchRuntimeProbe.Script(OsKind.Windows), StringComparison.Ordinal);
        Assert.Equal(new[] { "-c", TorchRuntimeProbe.Script(OsKind.Linux) }, TorchRuntimeProbe.Arguments(OsKind.Linux));
    }
}
