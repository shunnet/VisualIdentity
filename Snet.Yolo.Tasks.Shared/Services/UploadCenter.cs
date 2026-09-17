namespace Snet.Yolo.Tasks.Services;

using Microsoft.AspNetCore.Components.Forms;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Import;

/// <summary>上传任务的业务类型。</summary>
public enum UploadKind
{
    /// <summary>向普通工程批量导入图片。</summary>
    ProjectImages,
    /// <summary>向分类工程按类别导入图片。</summary>
    ProjectClassImages,
    /// <summary>导入“YOLO (with images)”ZIP 数据集。</summary>
    ProjectYoloArchive,
    /// <summary>向验证页导入待识别图片或视频。</summary>
    ValidationImages,
    /// <summary>向验证页导入 ONNX 模型。</summary>
    ValidationModel,
}

/// <summary>上传任务所处阶段。</summary>
public enum UploadPhase { Running, Completed, Failed, Cancelled }

/// <summary>
/// 上传意图：页面在渲染时声明“当前入口提交的文件要做什么”。
/// 只保存纯数据、不含页面引用，因此任务可以脱离页面继续执行。
/// </summary>
public sealed record UploadIntent(UploadKind Kind, string ScopeKey, string ProjectId = "", string ClassName = "", int ModelIndex = -1);

/// <summary>浏览器文件的名称与大小快照（用于界面展示，避免页面缓存 IBrowserFile）。</summary>
public sealed record UploadFileInfo(string Name, long Size);

/// <summary>等待用户确认的文件选择（分类导入、ONNX 模型导入）。</summary>
public sealed record UploadSelection(UploadKind Kind, string ScopeKey, IReadOnlyList<UploadFileInfo> Files);

/// <summary>上传任务状态的只读快照。</summary>
public sealed record UploadJob(
    string ScopeKey,
    UploadKind Kind,
    UploadPhase Phase,
    string Status,
    int Total,
    int Completed,
    int? Percent,
    string? Error,
    DateTime UpdatedAt)
{
    /// <summary>任务是否仍在进行。</summary>
    public bool IsRunning => Phase == UploadPhase.Running;
}

/// <summary>确认前可编辑的草稿参数（分类名称、模型信息）。</summary>
public sealed class UploadDraft
{
    /// <summary>分类导入使用的类别名称。</summary>
    public string ClassName { get; set; } = string.Empty;
    /// <summary>ONNX 模型显示名称。</summary>
    public string ModelName { get; set; } = string.Empty;
    /// <summary>ONNX 模型描述。</summary>
    public string ModelDescribe { get; set; } = string.Empty;
    /// <summary>ONNX 模型类型（OnnxType 名称）。</summary>
    public string ModelType { get; set; } = "ObjectDetection";
}

/// <summary>
/// 上传中心（Blazor Server 中注册为 Scoped，即每个信号连接电路一份）。
///
/// 关键点：真正的文件读取入口是外壳布局里的三个常驻 &lt;InputFile&gt;（见 AppShellLayout）。
/// 页面只声明上传意图并渲染进度；因为 DOM 输入元素在页面切换时不会被卸载，
/// 1) 读取文件时仍能按 id 找到输入元素，上传不会中断；
/// 2) 上传任务由本服务持有，页面销毁不会取消任务；
/// 3) 切回页面后从 <see cref="GetJob"/> 读回进度，任务与状态都保持。
/// </summary>
public sealed class UploadCenter : IDisposable
{
    /// <summary>常驻图片/视频输入元素 id（由 AppShellLayout 渲染）。</summary>
    public const string ImagesInputId = "ls-upload-images";
    /// <summary>常驻 ZIP 输入元素 id（由 AppShellLayout 渲染）。</summary>
    public const string ArchiveInputId = "ls-upload-archive";
    /// <summary>常驻 ONNX 输入元素 id（由 AppShellLayout 渲染）。</summary>
    public const string OnnxInputId = "ls-upload-onnx";

    /// <summary>单次选择允许的最大文件数。</summary>
    public const int MaxFilesPerSelection = 100;
    /// <summary>单张图片/单个视频允许的最大字节数。</summary>
    public const long MaxImageBytes = UploadedFileValidator.MaximumImageFileBytes;
    /// <summary>ZIP 数据集允许的最大字节数。</summary>
    public const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;
    /// <summary>ONNX 模型允许的最大字节数。</summary>
    public const long MaxOnnxBytes = 500L * 1024 * 1024;

    private static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(120);

    private readonly WorkspaceService _workspaces;
    private readonly ValidationService _validation;
    private readonly ValidationState _validationState;
    private readonly ToastService _toasts;
    private readonly CurrentUserContext _currentUser;
    private readonly LanguageManager _language;
    private readonly FfmpegInstaller _ffmpeg;
    private readonly ILogger<UploadCenter> _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, MutableJob> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<UploadSlot, UploadIntent> _intents = new();
    private PendingSelection? _pending;
    private int _disposed;

    /// <summary>创建上传中心。</summary>
    public UploadCenter(
        WorkspaceService workspaces,
        ValidationService validation,
        ValidationState validationState,
        ToastService toasts,
        CurrentUserContext currentUser,
        LanguageManager language,
        FfmpegInstaller ffmpeg,
        ILogger<UploadCenter> logger)
    {
        _workspaces = workspaces;
        _validation = validation;
        _validationState = validationState;
        _toasts = toasts;
        _currentUser = currentUser;
        _language = language;
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    /// <summary>
    /// 上传视频后的 FFmpeg 自检：缺失时 Windows 弹窗让用户选择、Linux 直接后台安装。
    /// 失败只提示，绝不影响其它图片上传流程（异常全部吞掉并记日志）。
    /// </summary>
    private void TriggerFfmpegSelfCheck()
    {
        _ = Task.Run(async () =>
        {
            try { await _ffmpeg.EnsureAsync(); }
            catch (Exception error) { _logger.LogWarning(error, "FFmpeg 自检失败（不影响上传）"); }
        });
    }

    /// <summary>上传状态或进度发生变化；订阅方应刷新界面。</summary>
    public event Action? Changed;

    /// <summary>确认前可编辑的草稿参数。</summary>
    public UploadDraft Draft { get; } = new();

    /// <summary>是否存在正在执行的上传任务。</summary>
    public bool Busy
    {
        get { lock (_gate) { return _jobs.Values.Any(job => job.IsRunning); } }
    }

    /// <summary>当前正在执行的上传任务（任意页面）；没有时返回空。</summary>
    public UploadJob? ActiveJob
    {
        get
        {
            lock (_gate)
            {
                foreach (var job in _jobs.Values)
                {
                    if (job.IsRunning) { return job.Snapshot(); }
                }
                return null;
            }
        }
    }

    /// <summary>读取指定范围的上传任务快照；没有任务时返回空。</summary>
    public UploadJob? GetJob(string scopeKey)
    {
        lock (_gate) { return _jobs.TryGetValue(scopeKey, out var job) ? job.Snapshot() : null; }
    }

    /// <summary>当前等待确认的文件选择；没有时返回空。</summary>
    public UploadSelection? Pending
    {
        get { lock (_gate) { return _pending?.Selection; } }
    }

    /// <summary>声明输入元素本次要执行的上传意图（由页面在渲染时调用，不触发重绘）。</summary>
    public void Arm(UploadIntent intent)
    {
        lock (_gate) { _intents[SlotOf(intent.Kind)] = intent; }
    }

    /// <summary>取消并清除等待确认的文件选择。</summary>
    public void ClearPending()
    {
        lock (_gate) { _pending = null; }
        RaiseChanged(force: true);
    }

    /// <summary>清除指定范围的已完成任务状态。</summary>
    public void ClearJob(string scopeKey)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(scopeKey, out var job) && !job.IsRunning) { _jobs.Remove(scopeKey); }
        }
        RaiseChanged(force: true);
    }

    /// <summary>更新草稿参数（不广播重绘：调用方组件在事件处理后本来就会重绘）。</summary>
    public void UpdateDraft(Action<UploadDraft> mutate) => mutate(Draft);

    /// <summary>取消指定范围正在执行的上传任务。</summary>
    public void Cancel(string scopeKey)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(scopeKey, out var job)) { job.Cancel(); }
        }
        RaiseChanged(force: true);
    }

    /// <summary>
    /// 工程详情页需要声明的全部上传意图：图片槽位（检测工程直接导入，分类工程走弹窗确认）
    /// 与 ZIP 槽位（仅检测工程有该按钮）。
    ///
    /// 集中在这里是为了避免页面漏声明某个槽位——漏掉的表现是点了按钮只弹一句“操作失败”：
    /// 输入元素本身存在，但对应的上传意图没有登记。
    /// </summary>
    public static IReadOnlyList<UploadIntent> ProjectPageIntents(bool isClassify, string scopeKey, string projectId)
    {
        var intents = new List<UploadIntent>
        {
            new(isClassify ? UploadKind.ProjectClassImages : UploadKind.ProjectImages, scopeKey, projectId),
        };
        if (!isClassify) { intents.Add(new UploadIntent(UploadKind.ProjectYoloArchive, scopeKey, projectId)); }
        return intents;
    }

    /// <summary>
    /// 验证页需要声明的上传意图：ONNX 槽位（弹窗里选模型文件）与文件槽位（需先选中模型才能上传）。
    /// 与 <see cref="ProjectPageIntents"/> 同理，集中声明避免漏掉某个槽位。
    /// </summary>
    public static IReadOnlyList<UploadIntent> ValidationPageIntents(int modelIndex, string fileScopeKey, string modelScopeKey)
    {
        var intents = new List<UploadIntent> { new(UploadKind.ValidationModel, modelScopeKey) };
        if (modelIndex >= 0) { intents.Add(new UploadIntent(UploadKind.ValidationImages, fileScopeKey, ModelIndex: modelIndex)); }
        return intents;
    }

    /// <summary>图片/视频输入元素的选择回调。</summary>
    public Task OnImagesSelectedAsync(InputFileChangeEventArgs args) => HandleSelectionAsync(UploadSlot.Images, args);

    /// <summary>ZIP 输入元素的选择回调。</summary>
    public Task OnArchiveSelectedAsync(InputFileChangeEventArgs args) => HandleSelectionAsync(UploadSlot.Archive, args);

    /// <summary>ONNX 输入元素的选择回调。</summary>
    public Task OnOnnxSelectedAsync(InputFileChangeEventArgs args) => HandleSelectionAsync(UploadSlot.Onnx, args);

    /// <summary>确认等待中的文件选择并开始执行上传任务。</summary>
    public void ConfirmPending()
    {
        PendingSelection? pending;
        lock (_gate) { pending = _pending; }
        if (pending is null) { return; }

        var intent = pending.Intent;
        if (intent.Kind == UploadKind.ProjectClassImages)
        {
            var className = (Draft.ClassName ?? string.Empty).Trim();
            if (className.Length == 0) { _toasts.ShowError(_language.Translate("ClassName") + " 不能为空。"); return; }
            intent = intent with { ClassName = className };
        }

        lock (_gate) { _pending = null; }
        StartJob(intent, pending.Files);
    }

    /// <summary>释放：取消仍在执行的任务（电路断开后浏览器文件已无法继续读取）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
    }

    private Task HandleSelectionAsync(UploadSlot slot, InputFileChangeEventArgs args)
    {
        UploadIntent? intent;
        lock (_gate) { _intents.TryGetValue(slot, out intent); }
        if (intent is null)
        {
            // 正常情况下不会发生：页面每次渲染都会声明全部槽位的意图。
            // 这里给出可诊断的提示，而不是让用户看到一句无从下手的“操作失败”。
            _logger.LogWarning("上传槽位 {Slot} 没有登记上传意图：页面未声明该槽位或需要刷新", slot);
            _toasts.ShowError("上传入口未就绪，请刷新页面后重试。");
            return Task.CompletedTask;
        }
        if (Busy)
        {
            _toasts.ShowWarning(_language.Translate("UploadInProgress"));
            return Task.CompletedTask;
        }

        IReadOnlyList<IBrowserFile> files;
        try { files = args.GetMultipleFiles(MaxFilesPerSelection); }
        catch (InvalidOperationException)
        {
            _toasts.ShowError($"单次最多选择 {MaxFilesPerSelection} 个文件。");
            return Task.CompletedTask;
        }

        var accepted = new List<IBrowserFile>();
        foreach (var file in files)
        {
            if (IsAcceptable(intent.Kind, file.Name, file.Size, out var reason)) { accepted.Add(file); }
            else { _toasts.ShowWarning(reason); }
        }
        if (accepted.Count == 0) { return Task.CompletedTask; }

        if (RequiresConfirmation(intent.Kind))
        {
            var selection = new UploadSelection(intent.Kind, intent.ScopeKey, accepted.Select(file => new UploadFileInfo(file.Name, file.Size)).ToArray());
            lock (_gate) { _pending = new PendingSelection(intent, accepted, selection); }
            RaiseChanged(force: true);
            return Task.CompletedTask;
        }

        StartJob(intent, accepted);
        return Task.CompletedTask;
    }

    /// <summary>按上传类型校验文件类型与大小；拒绝时给出可读原因。</summary>
    public static bool IsAcceptable(UploadKind kind, string fileName, long size, out string reason)
    {
        reason = string.Empty;
        switch (kind)
        {
            case UploadKind.ProjectYoloArchive:
                if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { reason = "只接受 .zip 压缩包。"; return false; }
                if (size is <= 0 or > MaxArchiveBytes) { reason = "ZIP 文件大小无效。"; return false; }
                return true;
            case UploadKind.ValidationModel:
                if (!fileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) { reason = "只接受 .onnx 模型文件。"; return false; }
                if (size is < 8 or > MaxOnnxBytes) { reason = "ONNX 文件大小无效。"; return false; }
                return true;
            default:
                if (size is <= 0 or > MaxImageBytes) { reason = $"文件 {fileName} 超出大小限制。"; return false; }
                try { _ = UploadedFileValidator.GetExtension(fileName, allowVideo: kind == UploadKind.ValidationImages); return true; }
                catch (InvalidDataException error) { reason = error.Message; return false; }
        }
    }

    /// <summary>分类导入与模型导入需要用户确认后再执行。</summary>
    public static bool RequiresConfirmation(UploadKind kind)
        => kind is UploadKind.ProjectClassImages or UploadKind.ValidationModel;

    private void StartJob(UploadIntent intent, IReadOnlyList<IBrowserFile> files)
    {
        MutableJob job;
        lock (_gate)
        {
            if (_jobs.Values.Any(existing => existing.IsRunning))
            {
                _toasts.ShowWarning(_language.Translate("UploadInProgress"));
                return;
            }
            job = new MutableJob(intent, files.Count, RaiseChanged);
            _jobs[intent.ScopeKey] = job;
        }
        RaiseChanged(force: true);
        // 任务在线程池执行：页面销毁与重绘都不会取消它。
        _ = Task.Run(() => RunJobAsync(job, files));
    }

    private async Task RunJobAsync(MutableJob job, IReadOnlyList<IBrowserFile> files)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, job.Token);
        var cancellationToken = linked.Token;
        try
        {
            switch (job.Intent.Kind)
            {
                case UploadKind.ProjectImages:
                    await ImportProjectImagesAsync(job, files, classFolder: null, cancellationToken);
                    break;
                case UploadKind.ProjectClassImages:
                    await ImportProjectImagesAsync(job, files, job.Intent.ClassName, cancellationToken);
                    break;
                case UploadKind.ProjectYoloArchive:
                    await ImportProjectArchiveAsync(job, files[0], cancellationToken);
                    break;
                case UploadKind.ValidationImages:
                    await ImportValidationImagesAsync(job, files, cancellationToken);
                    break;
                case UploadKind.ValidationModel:
                    await ImportValidationModelAsync(job, files[0], cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("未知的上传类型。");
            }
            job.Complete(_language.Translate("UploadComplete"));
            job.Notify(force: true);
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            job.Notify(force: true);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "上传任务失败 {Kind} {Scope}", job.Intent.Kind, job.Intent.ScopeKey);
            job.Fail(error.Message);
            job.Notify(force: true);
            _toasts.ShowError(error.Message);
        }
    }

    /// <summary>向工程导入图片（普通工程或分类工程的某个类别），失败时回滚已写入的文件与任务。</summary>
    private async Task ImportProjectImagesAsync(MutableJob job, IReadOnlyList<IBrowserFile> files, string? classFolder, CancellationToken cancellationToken)
    {
        var project = await _workspaces.GetProjectAsync(job.Intent.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("工程不存在或无权访问。");
        var originalTaskCount = project.Tasks.Count;
        var createdFiles = new List<string>();
        var saved = false;
        try
        {
            var (root, urlPrefix) = await _workspaces.GetProjectUploadLocationAsync(job.Intent.ProjectId);
            Directory.CreateDirectory(root);
            var nextId = project.Tasks.Count == 0 ? 1L : project.Tasks.Max(task => task.Id ?? 0L) + 1;
            var status = _language.Translate("UploadImage");
            job.Begin(files.Count, status);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var storedName = await StoreImageAsync(job, file, root, status, cancellationToken);
                createdFiles.Add(Path.Combine(root, storedName));
                var data = new System.Text.Json.Nodes.JsonObject { ["image"] = urlPrefix + storedName };
                if (!string.IsNullOrEmpty(classFolder)) { data["class"] = classFolder; }
                project.Tasks.Add(new AnnotationTask { Id = nextId++, Data = data });
                job.CompleteFile(status);
            }
            await _workspaces.SaveProjectAsync(project, cancellationToken);
            saved = true;
            _toasts.ShowSuccess(classFolder is null
                ? _language.Translate("ImportDone") + " (" + files.Count + ")"
                : _language.Translate("ImportDone") + "（" + classFolder + "）");
        }
        catch
        {
            if (!saved)
            {
                project.Tasks.RemoveRange(originalTaskCount, project.Tasks.Count - originalTaskCount);
                foreach (var path in createdFiles) { TryDelete(path); }
            }
            throw;
        }
    }

    /// <summary>导入“YOLO (with images)”ZIP：先落盘、再自检、最后逐张写入工程。</summary>
    private async Task ImportProjectArchiveAsync(MutableJob job, IBrowserFile file, CancellationToken cancellationToken)
    {
        var project = await _workspaces.GetProjectAsync(job.Intent.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("工程不存在或无权访问。");
        var currentConfig = Snet.Yolo.Tasks.Core.Config.LabelingConfigParser.Parse(project.LabelConfigXml);
        if (Snet.Yolo.Tasks.Core.Config.YoloTaskRegistry.FromConfig(currentConfig) != Snet.Yolo.Tasks.Core.Config.YoloTaskType.Detect)
        {
            throw new InvalidDataException(_language.Translate("YoloZipDetectOnly"));
        }

        var originalTaskCount = project.Tasks.Count;
        var originalLabelConfig = project.LabelConfigXml;
        var createdFiles = new List<string>();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "Snet.Yolo", "imports");
        var temporaryArchive = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".zip");
        var saved = false;
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var uploadStatus = _language.Translate("UploadingYoloZip");
            job.Begin(1, uploadStatus);
            await CopyBrowserFileWithProgressAsync(job, file, temporaryArchive, uploadStatus, cancellationToken);

            job.ReportPercent(25, _language.Translate("InspectingYoloZip"));
            var plan = await Task.Run(() => YoloWithImagesImporter.Inspect(temporaryArchive), cancellationToken);
            var importedLabels = YoloWithImagesImporter.MergeLabels(project.LabelConfigXml, plan.Classes);
            var parsedImportedConfig = Snet.Yolo.Tasks.Core.Config.LabelingConfigParser.Parse(importedLabels.Xml);
            var rectangleControl = parsedImportedConfig.Controls.First(control => control.Kind == Snet.Yolo.Tasks.Core.Config.ControlTagKind.RectangleLabels);

            var (uploadsRoot, urlPrefix) = await _workspaces.GetProjectUploadLocationAsync(job.Intent.ProjectId);
            Directory.CreateDirectory(uploadsRoot);
            var nextId = project.Tasks.Count == 0 ? 1L : project.Tasks.Max(task => task.Id ?? 0L) + 1;

            var importStatus = _language.Translate("ImportingYoloImages");
            job.ReportPercent(30, importStatus);
            await using var archiveStream = new FileStream(temporaryArchive, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new System.IO.Compression.ZipArchive(archiveStream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
            var imported = 0;
            foreach (var image in plan.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = FindArchiveEntry(archive, image.ImageEntryPath);
                var stored = await StoreArchiveImageAsync(entry, image.OriginalFileName, uploadsRoot);
                createdFiles.Add(Path.Combine(uploadsRoot, stored.FileName));
                project.Tasks.Add(BuildImportedTask(nextId++, urlPrefix + stored.FileName, stored.Width, stored.Height, image, importedLabels.ClassNames, rectangleControl));
                imported++;
                job.ReportPercent(30 + (int)Math.Round(imported * 70d / Math.Max(1, plan.Images.Count)), importStatus);
            }

            project.LabelConfigXml = importedLabels.Xml;
            await _workspaces.SaveProjectAsync(project, cancellationToken);
            saved = true;
            _toasts.ShowSuccess(string.Format(System.Globalization.CultureInfo.CurrentCulture, _language.Translate("YoloImportSuccess"), plan.Images.Count, plan.AnnotationCount, plan.Classes.Count));
        }
        catch
        {
            if (!saved)
            {
                project.Tasks.RemoveRange(originalTaskCount, project.Tasks.Count - originalTaskCount);
                project.LabelConfigXml = originalLabelConfig;
                foreach (var path in createdFiles) { TryDelete(path); }
            }
            throw;
        }
        finally
        {
            TryDelete(temporaryArchive);
        }
    }

    /// <summary>向验证页导入待识别图片或视频。</summary>
    private async Task ImportValidationImagesAsync(MutableJob job, IReadOnlyList<IBrowserFile> files, CancellationToken cancellationToken)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var (uploadDirectory, urlPrefix) = await _validation.GetValidationUploadLocationAsync();
        Directory.CreateDirectory(uploadDirectory);
        var status = _language.Translate("UploadImage");
        job.Begin(files.Count, status);
        var uploaded = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var extension = UploadedFileValidator.GetExtension(file.Name, allowVideo: true);
                var isVideo = UploadedFileValidator.IsVideo(extension);
                var storedName = "val_" + Guid.NewGuid().ToString("N")[..12] + extension;
                var destinationPath = Path.Combine(uploadDirectory, storedName);
                var temporaryPath = destinationPath + ".upload";
                try
                {
                    await using (var source = file.OpenReadStream(MaxImageBytes))
                    await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await CopyStreamWithProgressAsync(job, source, destination, file.Size, status, cancellationToken);
                    }
                    await _validation.ValidateUploadedFileAsync(temporaryPath, isVideo, cancellationToken);
                    File.Move(temporaryPath, destinationPath);
                    _validationState.AddImage(owner, job.Intent.ModelIndex, Path.GetFileName(file.Name), urlPrefix + storedName, isVideo, (isVideo ? "video/" : "image/") + extension.TrimStart('.'));
                    _validation.TrackValidationFile(destinationPath);
                    uploaded++;
                    if (isVideo) { TriggerFfmpegSelfCheck(); }
                }
                catch
                {
                    TryDelete(temporaryPath);
                    TryDelete(destinationPath);
                    throw;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _toasts.ShowError(error.Message);
            }
            job.CompleteFile(status);
        }
        if (uploaded > 0) { _toasts.ShowSuccess(_language.Translate("ImportDone") + " (" + uploaded + ")"); }
    }

    /// <summary>注册 ONNX 模型。</summary>
    private async Task ImportValidationModelAsync(MutableJob job, IBrowserFile file, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Snet.Yolo.Server.models.@enum.OnnxType>(Draft.ModelType, out var modelType))
        {
            modelType = Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection;
        }
        var fileName = Path.GetFileName(file.Name);
        var status = _language.Translate("UploadOnnx");
        job.Begin(1, status);
        await using var stream = file.OpenReadStream(MaxOnnxBytes);
        var result = await _validation.AddModelAsync(stream, string.IsNullOrWhiteSpace(fileName) ? Draft.ModelName + ".onnx" : fileName, Draft.ModelDescribe ?? string.Empty, modelType);
        if (!result.Status) { throw new InvalidOperationException(result.Message ?? _language.Translate("OperationFailed")); }
        job.CompleteFile(status);
        _toasts.ShowSuccess(_language.Translate("ModelAdded"));
    }

    /// <summary>把浏览器文件写入临时文件，完成真实解码校验后原子移动到目标目录。</summary>
    private static async Task<string> StoreImageAsync(MutableJob job, IBrowserFile file, string directory, string status, CancellationToken cancellationToken)
    {
        var extension = UploadedFileValidator.GetExtension(file.Name, allowVideo: false);
        var storedName = Guid.NewGuid().ToString("N") + extension;
        var destinationPath = Path.Combine(directory, storedName);
        var temporaryPath = destinationPath + ".upload";
        try
        {
            await using (var source = file.OpenReadStream(MaxImageBytes))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyStreamWithProgressAsync(job, source, destination, file.Size, status, cancellationToken);
            }
            UploadedFileValidator.ValidateImage(temporaryPath);
            File.Move(temporaryPath, destinationPath);
            return storedName;
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(destinationPath);
            throw;
        }
    }

    /// <summary>把浏览器文件流式写入临时 ZIP，并按字节更新前 25% 的进度。</summary>
    private static async Task CopyBrowserFileWithProgressAsync(MutableJob job, IBrowserFile file, string destinationPath, string status, CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream(MaxArchiveBytes);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var copied = await CopyStreamWithProgressAsync(job, source, destination, file.Size, status, cancellationToken, percentScale: 25, percentOffset: 0);
        if (copied != file.Size) { throw new InvalidDataException("ZIP 上传不完整，请重试。"); }
    }

    /// <summary>
    /// 流式复制并按字节上报进度（每 1 MiB 或读取结束时刷新一次）。
    /// percentScale 非空时把字节进度按比例映射到百分比区间，否则按“当前文件占整体比例”上报。
    /// </summary>
    private static async Task<long> CopyStreamWithProgressAsync(
        MutableJob job,
        Stream source,
        Stream destination,
        long totalBytes,
        string status,
        CancellationToken cancellationToken,
        int? percentScale = null,
        int percentOffset = 0)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(256 * 1024);
        long copied = 0;
        long lastReported = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) { break; }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (copied - lastReported < 1024L * 1024 && copied < totalBytes) { continue; }
                lastReported = copied;
                if (percentScale is int scale)
                {
                    job.ReportPercent(totalBytes <= 0 ? percentOffset : percentOffset + (int)Math.Clamp(Math.Round(copied * (double)scale / totalBytes), 0d, scale), status);
                }
                else
                {
                    job.ReportBytes(status + " " + FormatBytes(copied) + " / " + FormatBytes(totalBytes), totalBytes <= 0 ? 0d : (double)copied / totalBytes);
                }
            }
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        return copied;
    }

    /// <summary>在自检过的 ZIP 中按规范路径查找图片条目。</summary>
    private static System.IO.Compression.ZipArchiveEntry FindArchiveEntry(System.IO.Compression.ZipArchive archive, string normalizedPath)
        => archive.Entries.FirstOrDefault(entry => entry.FullName.Replace('\\', '/').Trim('/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidDataException($"ZIP 图片条目不存在：{normalizedPath}。");

    /// <summary>把 ZIP 图片安全写入项目目录，验证真实编码后返回文件名与尺寸。</summary>
    private static async Task<(string FileName, int Width, int Height)> StoreArchiveImageAsync(System.IO.Compression.ZipArchiveEntry entry, string originalFileName, string directory)
    {
        var extension = UploadedFileValidator.GetExtension(originalFileName, allowVideo: false);
        var storedName = Guid.NewGuid().ToString("N") + extension;
        var destinationPath = Path.Combine(directory, storedName);
        var temporaryPath = destinationPath + ".upload";
        try
        {
            await using var source = entry.Open();
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(256 * 1024);
                long copied = 0;
                try
                {
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                        if (read == 0) { break; }
                        copied += read;
                        if (copied > MaxImageBytes) { throw new InvalidDataException($"图片 {originalFileName} 超过 100 MiB。"); }
                        await destination.WriteAsync(buffer.AsMemory(0, read));
                    }
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
            }
            var dimensions = UploadedFileValidator.ValidateImageAndGetDimensions(temporaryPath);
            File.Move(temporaryPath, destinationPath);
            return (storedName, dimensions.Width, dimensions.Height);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(destinationPath);
            throw;
        }
    }

    /// <summary>把导入计划中的归一化 YOLO 框转换为项目标注任务。</summary>
    private static AnnotationTask BuildImportedTask(long taskId, string imageUrl, int width, int height, YoloImportImage image, IReadOnlyList<string> classes, Snet.Yolo.Tasks.Core.Config.ControlTagInfo control)
    {
        var rows = image.Boxes.Select(box => new ResultRow
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Type = RegionType.RectangleLabels,
            FromName = control.Name,
            ToName = control.ToName ?? "image",
            OriginalWidth = width,
            OriginalHeight = height,
            ImageRotation = 0,
            Value = new System.Text.Json.Nodes.JsonObject
            {
                ["x"] = (box.XCenter - box.Width / 2d) * 100d,
                ["y"] = (box.YCenter - box.Height / 2d) * 100d,
                ["width"] = box.Width * 100d,
                ["height"] = box.Height * 100d,
                ["rotation"] = 0d,
                ["rectanglelabels"] = new System.Text.Json.Nodes.JsonArray(classes[box.ClassIndex]),
            },
        }).ToList();
        var now = DateTime.UtcNow;
        return new AnnotationTask
        {
            Id = taskId,
            CreatedAt = now,
            UpdatedAt = now,
            Data = new System.Text.Json.Nodes.JsonObject { ["image"] = imageUrl },
            Annotations = new List<Annotation> { new() { Result = rows, ResultCount = rows.Count, CreatedAt = now, UpdatedAt = now } },
        };
    }

    /// <summary>把字节数格式化为便于阅读的进度文本。</summary>
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.##} GB"
        : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.##} MB" : $"{bytes / 1024d:0.#} KB";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RaiseChanged(bool force)
    {
        if (!force && Volatile.Read(ref _disposed) != 0) { return; }
        try { Changed?.Invoke(); }
        catch (Exception error) { _logger.LogWarning(error, "上传状态通知失败"); }
    }

    private static UploadSlot SlotOf(UploadKind kind) => kind switch
    {
        UploadKind.ProjectYoloArchive => UploadSlot.Archive,
        UploadKind.ValidationModel => UploadSlot.Onnx,
        _ => UploadSlot.Images,
    };

    /// <summary>常驻输入元素槽位。</summary>
    private enum UploadSlot { Images, Archive, Onnx }

    /// <summary>等待确认的文件选择（含浏览器文件句柄）。</summary>
    private sealed record PendingSelection(UploadIntent Intent, IReadOnlyList<IBrowserFile> Files, UploadSelection Selection);

    /// <summary>可变的任务状态；所有读取都通过 <see cref="Snapshot"/> 返回不可变副本。</summary>
    private sealed class MutableJob
    {
        private readonly object _gate = new();
        private readonly Action<bool> _onChanged;
        private readonly CancellationTokenSource _cancellation = new();
        private DateTime _lastNotifyAt = DateTime.MinValue;
        private int _completed;
        private double _fileFraction;
        private int? _percentOverride;
        private string _status = string.Empty;
        private UploadPhase _phase = UploadPhase.Running;
        private string? _error;

        public MutableJob(UploadIntent intent, int total, Action<bool> onChanged)
        {
            Intent = intent;
            Total = total;
            _onChanged = onChanged;
        }

        public UploadIntent Intent { get; }
        public int Total { get; private set; }
        public CancellationToken Token => _cancellation.Token;
        public bool IsRunning { get { lock (_gate) { return _phase == UploadPhase.Running; } } }

        public void Begin(int total, string status)
        {
            lock (_gate) { Total = Math.Max(1, total); _completed = 0; _fileFraction = 0; _percentOverride = null; _status = status; }
            Notify(force: false);
        }

        public void ReportBytes(string status, double fileFraction)
        {
            lock (_gate) { _status = status; _fileFraction = Math.Clamp(fileFraction, 0d, 1d); }
            Notify(force: false);
        }

        public void ReportPercent(int percent, string status)
        {
            lock (_gate) { _status = status; _percentOverride = Math.Clamp(percent, 0, 100); }
            Notify(force: false);
        }

        public void CompleteFile(string status)
        {
            lock (_gate) { _status = status; _completed++; _fileFraction = 0; }
            Notify(force: false);
        }

        public void Complete(string status)
        {
            lock (_gate) { _phase = UploadPhase.Completed; _status = status; _completed = Total; _percentOverride = 100; _fileFraction = 0; }
        }

        public void Fail(string message)
        {
            lock (_gate) { _phase = UploadPhase.Failed; _status = message; _error = message; }
        }

        public void Cancel()
        {
            lock (_gate) { if (_phase == UploadPhase.Running) { _phase = UploadPhase.Cancelled; _status = "已停止"; } }
            try { _cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        public UploadJob Snapshot()
        {
            lock (_gate)
            {
                var total = Math.Max(1, Total);
                var percent = _percentOverride ?? (int)Math.Round((_completed + _fileFraction) * 100d / total);
                return new UploadJob(Intent.ScopeKey, Intent.Kind, _phase, _status, Total, _completed, Math.Clamp(percent, 0, 100), _error, DateTime.UtcNow);
            }
        }

        /// <summary>按节流间隔推送进度，避免每个数据块都重绘界面。</summary>
        public void Notify(bool force)
        {
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                if (!force && now - _lastNotifyAt < NotifyInterval) { return; }
                _lastNotifyAt = now;
            }
            _onChanged(force);
        }
    }
}
