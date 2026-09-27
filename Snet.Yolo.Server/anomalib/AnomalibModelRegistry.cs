namespace Snet.Yolo.Server.Anomalib;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Snet.Yolo.Server;

/// <summary>经训练门禁注册或从模型包导入、可供独立验证页加载的 Anomalib 模型。</summary>
public sealed class RegisteredAnomalibModel
{
    /// <summary>验证页显示的模型名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>用户填写的模型描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>注册标记是否包含用户填写的名称。</summary>
    public bool HasCustomName { get; init; }

    /// <summary>模型所在工程的标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>一次训练运行的标识。</summary>
    public required string RunId { get; init; }

    /// <summary>训练模型类型。</summary>
    public required AnomalibModelKind Model { get; init; }

    /// <summary>ONNX 模型的绝对路径。</summary>
    public required string OnnxPath { get; init; }

    /// <summary>模型清单的绝对路径。</summary>
    public required string ManifestPath { get; init; }

    /// <summary>完成注册的 UTC 时间。</summary>
    public required DateTime RegisteredAtUtc { get; init; }
}

/// <summary>可按页面当前语言呈现的 Anomalib 模型包校验错误。</summary>
public sealed class AnomalibModelImportException(string resourceKey, Exception? innerException = null)
    : Exception(resourceKey, innerException)
{
    /// <summary>错误说明对应的双语资源键。</summary>
    public string ResourceKey { get; } = resourceKey;
}

/// <summary>将通过门禁的模型保存在用户隔离的训练目录，并只枚举已注册产物。</summary>
public sealed class AnomalibModelRegistry
{
    private readonly string _storageRoot;

    /// <summary>Creates a registry rooted in the TASKS training store unless another store is specified.</summary>
    public AnomalibModelRegistry(string? storageRoot = null)
    {
        _storageRoot = Path.GetFullPath(storageRoot ?? Path.Combine(AppContext.BaseDirectory, "train", "anomalib"));
    }

    /// <summary>导入包大小上限（512 MiB）。</summary>
    public const long MaximumPackageBytes = 512L * 1024 * 1024;

    /// <summary>模型文件大小上限（500 MiB）。</summary>
    private const long MaximumModelBytes = 500L * 1024 * 1024;

    /// <summary>部署清单大小上限（1 MiB）。</summary>
    private const long MaximumManifestBytes = 1024 * 1024;

    /// <summary>模型注册标记的固定文件名。</summary>
    private const string MarkerName = "registered.json";

    /// <summary>JSON 文件读写选项。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>检查门禁、文件位置和模型摘要后原子写入注册标记。</summary>
    public async Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!artifact.Parity.CanRegister) { throw new InvalidDataException("Anomalib 模型未通过一致性门禁。"); }
        var projectRoot = ProjectRootFor(artifact.Owner, artifact.ProjectId);
        var artifactDirectory = Path.GetDirectoryName(Path.GetFullPath(artifact.OnnxPath))
            ?? throw new InvalidDataException("ONNX 模型路径无效。");
        if (!artifactDirectory.StartsWith(projectRoot + Path.DirectorySeparatorChar, PathComparison)
            || !string.Equals(Path.GetFileName(artifactDirectory), "artifacts", StringComparison.Ordinal)
            || !string.Equals(Path.GetFullPath(artifact.ManifestPath), Path.Combine(artifactDirectory, "model.manifest.json"), PathComparison)
            || !string.Equals(Path.GetFullPath(artifact.CheckpointPath), Path.Combine(artifactDirectory, "model.ckpt"), PathComparison)
            || !string.Equals(Path.GetFileName(artifact.OnnxPath), "model.onnx", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Anomalib 模型产物不在所属工程的训练目录中。");
        }
        if (!File.Exists(artifact.CheckpointPath)) { throw new InvalidDataException("Anomalib checkpoint 文件缺失。"); }
        var manifest = AnomalibManifestSerializer.Deserialize(await File.ReadAllTextAsync(artifact.ManifestPath, cancellationToken));
        await using var modelStream = new FileStream(artifact.OnnxPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(modelStream, cancellationToken));
        if (!string.Equals(digest, manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ONNX 模型摘要与 Anomalib 清单不一致。");
        }
        var marker = new RegistrationMarker(artifact.Owner, artifact.ProjectId, artifact.Model, DateTime.UtcNow, artifact.DatasetSha256);
        var markerPath = Path.Combine(artifactDirectory, MarkerName);
        var temporaryPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(marker, JsonOptions), cancellationToken);
            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); } }
    }

    /// <summary>列出指定登录用户已通过门禁的模型；无效或残缺的标记会被忽略。</summary>
    public async Task<IReadOnlyList<RegisteredAnomalibModel>> ListAsync(string owner, CancellationToken cancellationToken = default)
    {
        var ownerRoot = Path.Combine(_storageRoot, "users", OwnerStoragePath.Segment(owner));
        if (!Directory.Exists(ownerRoot)) { return []; }
        var models = new List<RegisteredAnomalibModel>();
        foreach (var projectDirectory in Directory.EnumerateDirectories(ownerRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectId = Path.GetFileName(projectDirectory);
            foreach (var runDirectory in Directory.EnumerateDirectories(projectDirectory, "anomalib-run-*", SearchOption.TopDirectoryOnly))
            {
                var directory = Path.Combine(runDirectory, "artifacts");
                var markerPath = Path.Combine(directory, MarkerName);
                var onnxPath = Path.Combine(directory, "model.onnx");
                var manifestPath = Path.Combine(directory, "model.manifest.json");
                if (!File.Exists(markerPath) || !File.Exists(onnxPath) || !File.Exists(manifestPath)) { continue; }
                try
                {
                    var marker = JsonSerializer.Deserialize<RegistrationMarker>(await File.ReadAllTextAsync(markerPath, cancellationToken), JsonOptions);
                    if (marker is null || !string.Equals(marker.Owner, owner, StringComparison.Ordinal)
                        || !string.Equals(marker.ProjectId, projectId, StringComparison.Ordinal)
                        || !Enum.IsDefined(marker.Model)) { continue; }
                    models.Add(new RegisteredAnomalibModel
                    {
                        Name = string.IsNullOrWhiteSpace(marker.Name)
                            ? $"{(marker.Model == AnomalibModelKind.PatchcoreExperimental ? "PatchCore" : AnomalibModelCatalog.Get(marker.Model).DisplayName)} · {projectId}"
                            : marker.Name,
                        Description = marker.Description ?? string.Empty,
                        HasCustomName = !string.IsNullOrWhiteSpace(marker.Name),
                        ProjectId = projectId,
                        RunId = Path.GetFileName(runDirectory),
                        Model = marker.Model,
                        OnnxPath = onnxPath,
                        ManifestPath = manifestPath,
                        RegisteredAtUtc = marker.RegisteredAtUtc,
                    });
                }
                catch (JsonException) { /* 损坏的标记不能进入验证模型列表。 */ }
            }
        }
        return models.OrderByDescending(static model => model.RegisteredAtUtc).ToArray();
    }

    /// <summary>导入本系统的 ONNX 与解析清单 ZIP 包；校验摘要和图契约后才注册。</summary>
    public async Task<RegisteredAnomalibModel> ImportAsync(string owner, Stream package, string name, string description,
        AnomalibModelKind modelKind, IProgress<long>? uploadProgress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(package);
        name = name?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120 || description.Length > 1000) { throw new AnomalibModelImportException("AnomalibImportInvalidDetails"); }
        _ = AnomalibModelCatalog.Get(modelKind);

        var projectId = "import-" + Guid.NewGuid().ToString("N");
        var runId = "anomalib-run-" + Guid.NewGuid().ToString("N");
        var projectRoot = ProjectRootFor(owner, projectId);
        if (Directory.Exists(projectRoot)) { throw new IOException("模型导入目录已存在。"); }
        var artifactDirectory = Path.Combine(projectRoot, runId, "artifacts");
        var packagePath = Path.Combine(projectRoot, "package.tmp");
        var onnxPath = Path.Combine(artifactDirectory, "model.onnx");
        var manifestPath = Path.Combine(artifactDirectory, "model.manifest.json");
        var markerPath = Path.Combine(artifactDirectory, MarkerName);
        var temporaryMarkerPath = markerPath + ".tmp";
        var completed = false;
        Directory.CreateDirectory(artifactDirectory);
        try
        {
            await using (var destination = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous))
            {
                await CopyLimitedAsync(package, destination, MaximumPackageBytes, cancellationToken, uploadProgress);
            }

            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count != 2
                || archive.GetEntry("model.onnx") is not { } modelEntry
                || archive.GetEntry("model.manifest.json") is not { } manifestEntry
                || modelEntry.Length is <= 0 or > MaximumModelBytes
                || manifestEntry.Length is <= 0 or > MaximumManifestBytes)
            {
                throw new AnomalibModelImportException("AnomalibPackageRequired");
            }
            string manifestJson;
            await using (var manifestStream = manifestEntry.Open())
            await using (var text = new MemoryStream())
            {
                await CopyLimitedAsync(manifestStream, text, MaximumManifestBytes, cancellationToken);
                manifestJson = System.Text.Encoding.UTF8.GetString(text.ToArray());
            }
            var manifest = AnomalibManifestSerializer.Deserialize(manifestJson);
            if (manifest.Algorithm != ModelAlgorithm(modelKind)) { throw new AnomalibModelImportException("AnomalibPackageTypeMismatch"); }
            await using (var source = modelEntry.Open())
            await using (var destination = new FileStream(onnxPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous))
            {
                await CopyLimitedAsync(source, destination, MaximumModelBytes, cancellationToken);
            }
            await using (var modelStream = new FileStream(onnxPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(modelStream, cancellationToken));
                if (!string.Equals(digest, manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AnomalibModelImportException("AnomalibPackageHashMismatch");
                }
            }
            ValidateOnnxContract(onnxPath, manifest);
            await File.WriteAllTextAsync(manifestPath, AnomalibManifestSerializer.Serialize(manifest), cancellationToken);
            var registeredAtUtc = DateTime.UtcNow;
            var marker = new RegistrationMarker(owner, projectId, modelKind, registeredAtUtc, string.Empty, name, description);
            await File.WriteAllTextAsync(temporaryMarkerPath, JsonSerializer.Serialize(marker, JsonOptions), cancellationToken);
            File.Move(temporaryMarkerPath, markerPath);
            completed = true;
            return new RegisteredAnomalibModel
            {
                Name = name,
                Description = description,
                HasCustomName = true,
                ProjectId = projectId,
                RunId = runId,
                Model = modelKind,
                OnnxPath = onnxPath,
                ManifestPath = manifestPath,
                RegisteredAtUtc = registeredAtUtc,
            };
        }
        catch (AnomalibManifestException error) { throw new AnomalibModelImportException("AnomalibPackageInvalid", error); }
        catch (OnnxRuntimeException error) { throw new AnomalibModelImportException("AnomalibPackageContractMismatch", error); }
        catch (AnomalibOnnxContractException error) { throw new AnomalibModelImportException("AnomalibPackageContractMismatch", error); }
        catch (AnomalibModelImportException) { throw; }
        catch (InvalidDataException error) { throw new AnomalibModelImportException("AnomalibPackageInvalid", error); }
        finally
        {
            if (File.Exists(packagePath)) { File.Delete(packagePath); }
            if (File.Exists(temporaryMarkerPath)) { File.Delete(temporaryMarkerPath); }
            if (!completed)
            {
                if (File.Exists(markerPath)) { File.Delete(markerPath); }
                if (File.Exists(manifestPath)) { File.Delete(manifestPath); }
                if (File.Exists(onnxPath)) { File.Delete(onnxPath); }
                if (Directory.Exists(artifactDirectory)) { Directory.Delete(artifactDirectory); }
                var runDirectory = Path.GetDirectoryName(artifactDirectory)!;
                if (Directory.Exists(runDirectory)) { Directory.Delete(runDirectory); }
                if (Directory.Exists(projectRoot)) { Directory.Delete(projectRoot); }
            }
        }
    }

    /// <summary>在复制时严格限制解压前后字节数。</summary>
    private static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        var buffer = new byte[256 * 1024];
        long copied = 0;
        var lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            copied += count;
            if (copied > limit) { throw new AnomalibModelImportException("AnomalibPackageTooLarge"); }
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            if (progress is not null && System.Diagnostics.Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 100)
            {
                progress.Report(copied);
                lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        progress?.Report(copied);
    }

    /// <summary>将页面选择的模型种类映射为部署清单中的算法名称。</summary>
    private static AnomalibAlgorithm ModelAlgorithm(AnomalibModelKind kind) => kind switch
    {
        AnomalibModelKind.Padim => AnomalibAlgorithm.Padim,
        AnomalibModelKind.EfficientAdSmall => AnomalibAlgorithm.EfficientAdSmall,
        AnomalibModelKind.PatchcoreExperimental => AnomalibAlgorithm.PatchCore,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>以 CPU 加载 ONNX 并核对输入及四个输出，避免导入不可用或错配的模型。</summary>
    private static void ValidateOnnxContract(string path, AnomalibModelManifest manifest)
    {
        using var session = new InferenceSession(path);
        if (!session.InputMetadata.TryGetValue(manifest.Input.Name, out var input)
            || input.ElementDataType != TensorElementType.Float
            || input.Dimensions.Length != 4
            || input.Dimensions[1] is not (-1 or 3)
            || input.Dimensions[2] is not (-1 or 0) && input.Dimensions[2] != manifest.Input.Height
            || input.Dimensions[3] is not (-1 or 0) && input.Dimensions[3] != manifest.Input.Width)
        {
            throw new AnomalibModelImportException("AnomalibPackageContractMismatch");
        }
        _ = AnomalibOutputContractDiscovery.Discover(manifest,
            session.OutputMetadata.Select(pair => new OnnxTensorDescriptor(pair.Key, ToElementType(pair.Value.ElementDataType),
                pair.Value.Dimensions.Select(static dimension => (long)dimension))));
    }

    /// <summary>将 ONNX Runtime 数据类型映射为严格输出契约的数据类型。</summary>
    private static OnnxTensorElementType ToElementType(TensorElementType type) => type switch
    {
        TensorElementType.Float => OnnxTensorElementType.Float32,
        TensorElementType.Double => OnnxTensorElementType.Float64,
        TensorElementType.Int64 => OnnxTensorElementType.Int64,
        TensorElementType.UInt8 => OnnxTensorElementType.UInt8,
        TensorElementType.Bool => OnnxTensorElementType.Boolean,
        _ => throw new AnomalibOnnxContractException("Anomalib ONNX 输出含不支持的元素类型：" + type),
    };

    /// <summary>按工程和运行标识读取用户自己的模型，避免由请求参数拼装任意文件路径。</summary>
    public async Task<RegisteredAnomalibModel?> FindAsync(string owner, string projectId, string runId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeSegment(projectId) || !runId.StartsWith("anomalib-run-", StringComparison.Ordinal)
            || !IsSafeSegment(runId)) { return null; }
        return (await ListAsync(owner, cancellationToken)).FirstOrDefault(model => model.ProjectId == projectId && model.RunId == runId);
    }

    /// <summary>Updates display metadata without changing the algorithm bound to the ONNX manifest.</summary>
    public async Task<bool> UpdateAsync(string owner, string projectId, string runId, string name,
        string? description, CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120 || description.Length > 1000)
        {
            throw new AnomalibModelImportException("AnomalibImportInvalidDetails");
        }
        var model = await FindAsync(owner, projectId, runId, cancellationToken);
        if (model is null) { return false; }
        var markerPath = Path.Combine(Path.GetDirectoryName(model.OnnxPath)!, MarkerName);
        var marker = JsonSerializer.Deserialize<RegistrationMarker>(await File.ReadAllTextAsync(markerPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("Anomalib model registration is invalid.");
        var temporaryPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath,
                JsonSerializer.Serialize(marker with { Name = name, Description = description }, JsonOptions), cancellationToken);
            File.Move(temporaryPath, markerPath, overwrite: true);
            return true;
        }
        finally { if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); } }
    }

    /// <summary>删除当前用户指定运行的已注册模型产物，保留项目、训练图片与其他运行。</summary>
    public async Task<bool> DeleteAsync(string owner, string projectId, string runId, CancellationToken cancellationToken = default)
    {
        var model = await FindAsync(owner, projectId, runId, cancellationToken);
        if (model is null) { return false; }
        var projectRoot = ProjectRootFor(owner, projectId);
        var runDirectory = Path.GetFullPath(Path.Combine(projectRoot, runId));
        var artifactDirectory = Path.GetFullPath(Path.Combine(runDirectory, "artifacts"));
        if (!runDirectory.StartsWith(projectRoot + Path.DirectorySeparatorChar, PathComparison)
            || !string.Equals(Path.GetDirectoryName(artifactDirectory), runDirectory, PathComparison)
            || (new DirectoryInfo(projectRoot).Attributes & FileAttributes.ReparsePoint) != 0
            || (new DirectoryInfo(runDirectory).Attributes & FileAttributes.ReparsePoint) != 0
            || (new DirectoryInfo(artifactDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("模型产物目录无效。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(artifactDirectory, recursive: true);
        return true;
    }

    /// <summary>将 ONNX 模型和必需的解析清单流式写入 ZIP，不包含可执行 pickle checkpoint。</summary>
    public static async Task WritePackageAsync(RegisteredAnomalibModel model, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(destination);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (path, name) in new[] { (model.OnnxPath, "model.onnx"), (model.ManifestPath, "model.manifest.json") })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    /// <summary>先在模型产物目录生成临时 ZIP，再以可寻址文件流下载；关闭响应流时自动删除临时文件。</summary>
    public static async Task<FileStream> OpenPackageDownloadAsync(RegisteredAnomalibModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var directory = Path.GetDirectoryName(model.OnnxPath) ?? throw new InvalidDataException("模型文件路径无效。");
        var temporaryPath = Path.Combine(directory, $".download-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            {
                await WritePackageAsync(model, output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            return new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        }
        catch
        {
            if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
            throw;
        }
    }

    /// <summary>生成用户和工程限定的训练目录。</summary>
    public static string ProjectRoot(string owner, string projectId)
    {
        if (!IsSafeSegment(projectId)) { throw new ArgumentException("工程标识无效。", nameof(projectId)); }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "train", "anomalib", "users", OwnerStoragePath.Segment(owner), projectId));
    }

    private string ProjectRootFor(string owner, string projectId)
    {
        if (!IsSafeSegment(projectId)) { throw new ArgumentException("工程标识无效。", nameof(projectId)); }
        return Path.GetFullPath(Path.Combine(_storageRoot, "users", OwnerStoragePath.Segment(owner), projectId));
    }

    /// <summary>检查单个 URL 或目录片段不会越出父目录。</summary>
    private static bool IsSafeSegment(string value)
        => !string.IsNullOrWhiteSpace(value) && value == Path.GetFileName(value) && value is not "." and not "..";

    /// <summary>按当前操作系统比较规范化路径。</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>磁盘上仅保存可核验的注册元数据，不保存客户端传入的路径。</summary>
    private sealed record RegistrationMarker(string Owner, string ProjectId, AnomalibModelKind Model, DateTime RegisteredAtUtc, string DatasetSha256,
        string? Name = null, string? Description = null);
}
