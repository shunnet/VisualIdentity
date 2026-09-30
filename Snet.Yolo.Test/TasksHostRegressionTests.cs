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

    /// <summary>保护 SAM 非 root 写入权限及卷声明；静态契约检查不替代容器实机验证。</summary>
    [Theory]
    [InlineData("Tasks.Cpu.Dockerfile", "/app/sam", "$APP_UID", "chown -R \"$APP_UID:$APP_UID\" /app/wwwroot /app/train /app/sam")]
    [InlineData("Tasks.Cuda.Dockerfile", "/app/sam", "1654", "chown -R 1654:1654 /app/wwwroot /app/train /app/sam")]
    [InlineData("Tasks.Windows.Dockerfile", "C:/app/sam", "ContainerUser", "icacls C:/app/sam /grant 'ContainerUser:(OI)(CI)M'")]
    public void TasksDocker_SamDirectoryIsWritableAndPersisted(string file, string path, string user, string permission)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker", file));
        var create = source.IndexOf(user == "ContainerUser" ? "New-Item -ItemType Directory" : "mkdir -p", StringComparison.Ordinal);
        var grant = source.IndexOf(permission, StringComparison.Ordinal);
        var runAs = source.IndexOf("USER " + user, StringComparison.Ordinal);
        Assert.True(create >= 0 && grant > create && runAs > grant);
        Assert.Contains(path, source[create..grant], StringComparison.Ordinal);
        var volumes = source.Split('\n').Single(line => line.StartsWith("VOLUME ", StringComparison.Ordinal));
        Assert.Contains("\"" + path + "\"", volumes, StringComparison.Ordinal);
        if (user == "ContainerUser")
        {
            Assert.Contains("-ErrorAction Stop", source, StringComparison.Ordinal);
            Assert.Contains("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", source, StringComparison.Ordinal);
        }
    }

    /// <summary>中英文部署示例必须显式复用命名卷，不能仅依赖匿名卷声明。</summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("README.en.md")]
    public void TasksDocker_DocumentsNamedSamVolume(string file)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
        Assert.Contains("-v snet-tasks-sam:/app/sam", source, StringComparison.Ordinal);
        Assert.Contains("source=snet-tasks-sam,target=C:/app/sam", source, StringComparison.Ordinal);
    }

    /// <summary>联合验证须保留多选上传、缩略图切换自动识别及退出等待上传的接线。</summary>
    [Fact]
    public void JointValidation_UsesMultipleImagesAndClickRecognition()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Components", "Pages", "JointValidation.razor"));
        Assert.Contains("OnChange=\"UploadAsync\" multiple", source);
        Assert.Contains("args.GetMultipleFiles(JointValidationState.MaximumImages)", source);
        Assert.Contains("class=\"ls-val-image-list\"", source);
        Assert.Contains("@onclick=\"() => SelectImageAsync(image)\"", source);
        Assert.Contains("await IdentifyAsync(_mode);", source);
        Assert.Contains("JointService.IdentifyAsync", source);
        Assert.Contains("SaveImageResult(); PersistState();", source);
        Assert.Contains("saved.Images.Where(image => File.Exists(image.Path))", source);
        Assert.Contains("await _uploadTask", source);
        Assert.DoesNotContain("var file = args.File;", source);
        Assert.DoesNotContain("DeleteImage();", source);
    }

    [Fact]
    public void JointValidation_NoticesReuseTopCenteredFiveSecondToasts()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Snet.Yolo.Tasks.Shared", "Components", "Pages", "JointValidation.razor"));
        Assert.Contains("@inject ToastService Toast", page);
        Assert.Contains("Toast.ShowReplacing(message, type)", page);
        Assert.DoesNotContain("alert alert-danger mt-2", page);
        var toasts = File.ReadAllText(Path.Combine(root, "Snet.Yolo.Tasks.Shared", "Components", "Shell", "Toasts.razor"));
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)", toasts);
        var css = File.ReadAllText(Path.Combine(root, "Snet.Yolo.Tasks.Shared", "wwwroot", "css", "ls.css"));
        Assert.Contains("position: fixed; top: .75rem; left: 50%; transform: translateX(-50%)", css);
    }

    /// <summary>图片分页由真实链接写入地址栏，加载和删除后页码不得重置或越界。</summary>
    [Fact]
    public void AnomalibProject_PaginationRestoresQueryAndCorrectsDeletedLastPage()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Components", "Pages", "AnomalibProjectDetails.razor"));
        Assert.Contains("[SupplyParameterFromQuery(Name = \"page\")]", page);
        Assert.Contains("_imagePage = Math.Clamp(PageQuery ?? 1, 1, TotalImagePages);", page);
        Assert.Contains("href=\"@PageHref(_imagePage - 1)\"", page);
        Assert.Contains("href=\"@PageHref(_imagePage + 1)\"", page);
        Assert.Contains("Navigation.NavigateTo(PageHref(_imagePage), replace: true)", page);
        Assert.DoesNotContain("SetImagePage", page);
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
