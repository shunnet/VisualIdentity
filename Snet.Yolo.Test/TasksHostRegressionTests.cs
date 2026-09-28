using System.Xml.Linq;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Tasks 主程序的交互与原生依赖回归测试。</summary>
public sealed class TasksHostRegressionTests
{
    [Fact]
    public void TrainingStorage_UsesOnlyAlgorithmScopedPaths()
    {
        var services = Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Services");
        var training = File.ReadAllText(Path.Combine(services, "TrainingService.cs"));
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"train\", \"yolo\", \".env\")", training);
        Assert.Contains("TrainingStoragePath.OwnerDirectory(envRoot, owner)", training);
        Assert.DoesNotContain("Path.Combine(envRoot, \"users\"", training);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, \"train\", \".env\")", training);

        var workspace = File.ReadAllText(Path.Combine(services, "WorkspaceService.cs"));
        Assert.Contains("TrainingStoragePath.OwnerDirectory(Path.Combine(TrainingRoot, \"yolo\"), owner)", workspace);
        Assert.Contains("TrainingStoragePath.OwnerDirectory(Path.Combine(TrainingRoot, \"anomalib\"), owner)", workspace);
        Assert.DoesNotContain("Path.Combine(TrainingRoot, \"users\"", workspace);
        Assert.DoesNotContain("Path.Combine(TrainingRoot, \"anomalib\", \"users\"", workspace);
    }

    [Fact]
    public void TrainingAmp_UsesSameIsolatedSettingsForPreparationAndTraining()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Services", "TrainingService.cs"));
        Assert.Contains("Path.Combine(projectDir, \"ultralytics-config\")", source);
        Assert.Contains("plan.VenvPython, settingsArguments, projectDir, configurationDirectory, cancellationToken", source);
        Assert.Contains("plan.VenvYolo, trainArgs, projectDir, configurationDirectory, cancellationToken", source);
        Assert.Contains("psi.Environment[\"YOLO_CONFIG_DIR\"] = configurationDirectory;", source);
        Assert.Contains("if (settingsExit != 0)", source);
        Assert.DoesNotContain("amp=False", source);
    }

    [Fact]
    public void TrainingStart_PreventsDuplicateSubmissionAndShowsImmediateFeedback()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Components", "Pages", "TrainPage.razor"));

        Assert.Contains("disabled=\"@_starting\"", page, StringComparison.Ordinal);
        Assert.Contains("if (_starting)", page, StringComparison.Ordinal);
        Assert.Contains("spinner-border", page, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"@_starting\"", page, StringComparison.Ordinal);
        Assert.Contains("finally { _starting = false; }", page, StringComparison.Ordinal);
        Assert.Contains("_status = await Training.StartAsync", page, StringComparison.Ordinal);
        Assert.Contains("_configOpen = false;", page, StringComparison.Ordinal);
        Assert.Contains("Message = L(\"TrainingRequestAccepted\")", page, StringComparison.Ordinal);
        Assert.Contains("Toast.ShowReplacing(L(\"TrainingRequestAccepted\"))", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", page, StringComparison.Ordinal);
        Assert.Contains("Training.IsActive(_owner, ProjectId)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CudaTasks_ReferencesOnlyTheGpuOnnxRuntimeProvider()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Cuda", "Snet.Yolo.Tasks.Cuda.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(references, path => path.Contains("ExecutionProvider.Cuda", StringComparison.Ordinal));
        Assert.DoesNotContain(references, path => path.Contains("ExecutionProvider.Cpu", StringComparison.Ordinal));

        var factory = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Cuda", "Services", "ExecutionProviderFactory.cs"));
        Assert.Contains("new CudaExecutionProvider(modelPath, -1, null)", factory, StringComparison.Ordinal);
        Assert.Contains("IsCudaAvailabilityFailure", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuExecutionProvider", factory, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Snet.Yolo.Tasks.Shared")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到 VisualIdentity 仓库根目录。");
    }
}
