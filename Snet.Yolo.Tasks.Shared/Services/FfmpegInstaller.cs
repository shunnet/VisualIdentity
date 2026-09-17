using Microsoft.Extensions.Logging;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// FFmpeg 自检与安装（视频识别前置条件）：
///  - Windows：未检测到 → 提示用户在"手动指定路径"与"静默下载安装"之间选择；静默安装从 GitHub 下载最新版并解压到部署目录。
///  - Linux（Ubuntu/Debian）：未检测到 → 直接用包管理器全局异步安装；失败才需要用户介入。
/// 安装过程对外暴露 <see cref="State"/>（阶段/百分比/日志尾部），界面据此显示进度；安装完成后把目录记入设置供视频解析复用。
/// </summary>
public sealed class FfmpegInstaller : IDisposable
{
    /// <summary>安装类命令的停滞判定：连续这么久没有输出就中止（避免卡死）。</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);
    /// <summary>单条日志的保留条数。</summary>
    private const int LogTailLimit = 40;

    private readonly MediaToolResolver _resolver;
    private readonly MediaToolSettingsStore _settings;
    private readonly IFfmpegDownloader _downloader;
    private readonly ISystemCommandRunner _runner;
    private readonly ILogger<FfmpegInstaller> _logger;
    private readonly object _lock = new();
    private readonly List<string> _log = new();
    private FfmpegInstallState _state = FfmpegInstallState.Idle;
    private CancellationTokenSource? _runCancellation;
    private bool _needsUserChoice;
    private bool _disposed;

    /// <summary>创建安装器。</summary>
    public FfmpegInstaller(MediaToolResolver resolver, MediaToolSettingsStore settings, IFfmpegDownloader downloader, ISystemCommandRunner runner, ILogger<FfmpegInstaller> logger)
    {
        _resolver = resolver;
        _settings = settings;
        _downloader = downloader;
        _runner = runner;
        _logger = logger;
    }

    /// <summary>状态变化通知（界面订阅后刷新）。</summary>
    public event Action? Changed;

    /// <summary>当前状态快照。</summary>
    public FfmpegInstallState State { get { lock (_lock) { return _state; } } }

    /// <summary>是否需要用户选择安装方式（Windows 未检测到 FFmpeg 时为 true）。</summary>
    public bool NeedsUserChoice { get { lock (_lock) { return _needsUserChoice; } } }

    /// <summary>FFmpeg 是否已可用。</summary>
    public bool IsAvailable => _resolver.TryGetPaths(out _, out _);

    /// <summary>
    /// 上传视频后的自检：已可用则直接返回；缺失时 Windows 置"需要用户选择"标志，
    /// Linux 直接用包管理器后台安装（不弹窗），失败时界面再提示。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvailable) { SetUserChoice(false); return; }
        if (OperatingSystem.IsWindows())
        {
            SetUserChoice(true);
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Idle,
                "未检测到 FFmpeg。视频识别需要 FFmpeg 解码，请选择安装方式。", null, null, null, Snapshot()));
            return;
        }
        if (TryBuildPackageCommand(out _, out _, out _))
        {
            // 后台异步安装：不能让上传流程等它；用 None 而不是调用方的取消标记，
            // 避免上传任务结束时把安装一起取消（安装由本服务的 Cancel() 控制）。
            _ = StartAsync(CancellationToken.None);
            return;
        }
        SetUserChoice(true);
        Publish(new FfmpegInstallState(FfmpegInstallPhase.Idle,
            "未检测到 FFmpeg，且当前系统没有 apt-get。请手动指定 FFmpeg 路径。", null, null, null, Snapshot()));
    }

    /// <summary>开始安装（Windows 静默下载安装 / Linux 包管理器全局安装）。</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_state.IsBusy) { return; }
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts = _runCancellation;
            _needsUserChoice = false;
        }
        using var registration = cts;
        try
        {
            ClearLog();
            if (OperatingSystem.IsWindows()) { await InstallOnWindowsAsync(cts.Token); }
            else { await InstallWithPackageManagerAsync(cts.Token); }
        }
        catch (OperationCanceledException)
        {
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Failed, "FFmpeg 安装已取消。", null, "已取消", null, Snapshot()));
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "FFmpeg 安装失败");
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Failed, "FFmpeg 安装失败：" + error.Message, null, error.Message, null, Snapshot()));
        }
    }

    /// <summary>使用用户指定的可执行文件或目录（验证通过后记录，供视频解析复用）。</summary>
    public async Task<(bool Ok, string? Error)> ApplyManualPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) { return (false, "请填写 FFmpeg 路径。"); }
        if (!MediaToolResolver.TryResolveUserPath(path, out var ffmpeg, out var ffprobe, out var error))
        {
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Failed, "指定的路径无效：" + (error ?? path), null, error, null, Snapshot()));
            return (false, error);
        }
        try
        {
            ClearLog();
            await RecordAndVerifyAsync(ffmpeg, ffprobe, "manual", cancellationToken);
            SetUserChoice(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "指定 FFmpeg 路径校验失败");
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Failed, "FFmpeg 不可用：" + ex.Message, null, ex.Message, null, Snapshot()));
            return (false, ex.Message);
        }
    }

    /// <summary>取消正在进行的安装。</summary>
    public void Cancel()
    {
        lock (_lock) { _runCancellation?.Cancel(); }
    }

    /// <summary>关闭失败提示（界面上的横幅/弹窗不再显示，但保留错误信息）。</summary>
    public void Dismiss()
    {
        lock (_lock)
        {
            if (_state.Phase != FfmpegInstallPhase.Failed) { return; }
            _state = FfmpegInstallState.Idle;
            _needsUserChoice = false;
        }
        Raise();
    }

    /// <summary>Windows：下载最新版压缩包 → 解压到部署目录 → 校验并记录。</summary>
    private async Task InstallOnWindowsAsync(CancellationToken cancellationToken)
    {
        var installDirectory = _resolver.ResolvePlatformInstallDirectory();
        Directory.CreateDirectory(installDirectory);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "Snet.Yolo", "ffmpeg");
        Publish(new FfmpegInstallState(FfmpegInstallPhase.Downloading, "正在从 GitHub 下载 FFmpeg 最新版…", 0, null, null, Snapshot()));
        var download = await _downloader.DownloadLatestAsync(temporaryDirectory, (written, total) =>
        {
            var percent = total is > 0 and not 0 ? (int)(written * 100 / total.Value) : (int?)null;
            var size = total is > 0 ? $"{Megabytes(written)} / {Megabytes(total.Value)} MB" : Megabytes(written) + " MB";
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Downloading, $"正在下载 FFmpeg 安装包（{size}）…", percent, null, null, Snapshot()));
        }, cancellationToken);
        var archivePath = download.ArchivePath;
        AppendLog($"已下载 FFmpeg {download.Version}：{Path.GetFileName(archivePath)}");

        try
        {
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Extracting, "正在解压到 " + installDirectory + " …", 100, null, null, Snapshot()));
            var extracted = FfmpegArchiveExtractor.Extract(archivePath, installDirectory, name => AppendLog("已解压 " + name));
            if (!extracted) { throw new InvalidOperationException("压缩包中没有找到 ffmpeg/ffprobe 可执行文件。"); }
        }
        finally
        {
            TryDelete(archivePath);
        }

        await RecordAndVerifyAsync(
            Path.Combine(installDirectory, ExecutableName("ffmpeg")),
            Path.Combine(installDirectory, ExecutableName("ffprobe")),
            "download",
            cancellationToken);
    }

    /// <summary>Linux（Ubuntu/Debian）：apt-get 全局安装；失败时先更新索引再重试一次。</summary>
    private async Task InstallWithPackageManagerAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildPackageCommand(out var executable, out var installArguments, out var reason))
        {
            throw new InvalidOperationException(reason ?? "当前系统不支持自动安装，请手动指定 FFmpeg 路径。");
        }

        Publish(new FfmpegInstallState(FfmpegInstallPhase.Installing, "正在使用 apt 全局安装 FFmpeg…", null, null, null, Snapshot()));
        var (exitCode, _, stalled) = await _runner.RunStreamingAsync(executable, installArguments, AppendLog, StallTimeout, cancellationToken);
        if (exitCode != 0)
        {
            var updateArguments = BuildPackageArguments(executable, "update");
            Publish(new FfmpegInstallState(FfmpegInstallPhase.Installing, "安装未成功，正在更新软件包索引后重试…", null, null, null, Snapshot()));
            await _runner.RunStreamingAsync(executable, updateArguments, AppendLog, StallTimeout, cancellationToken);
            (exitCode, _, stalled) = await _runner.RunStreamingAsync(executable, installArguments, AppendLog, StallTimeout, cancellationToken);
        }
        if (exitCode != 0)
        {
            throw new InvalidOperationException(stalled
                ? "安装命令长时间没有输出，已中止。可手动指定 FFmpeg 路径。"
                : $"apt-get 安装失败（退出码 {exitCode}）。若当前账号无 sudo 权限，请手动安装或指定 FFmpeg 路径。");
        }

        var ffmpeg = _runner.FindOnPath("ffmpeg") ?? SystemCommandRunner.FirstExisting("/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg");
        var ffprobe = _runner.FindOnPath("ffprobe") ?? SystemCommandRunner.FirstExisting("/usr/bin/ffprobe", "/usr/local/bin/ffprobe", "/snap/bin/ffprobe");
        if (ffmpeg is null || ffprobe is null) { throw new InvalidOperationException("安装命令已成功，但仍未找到 ffmpeg/ffprobe 可执行文件。"); }
        await RecordAndVerifyAsync(ffmpeg, ffprobe, "package", cancellationToken);
    }

    /// <summary>记录安装目录 → 清缓存 → 重新解析 → 版本探测，全部通过才算安装完成。</summary>
    private async Task RecordAndVerifyAsync(string ffmpegPath, string ffprobePath, string source, CancellationToken cancellationToken)
    {
        Publish(new FfmpegInstallState(FfmpegInstallPhase.Verifying, "正在校验 FFmpeg…", 100, null, null, Snapshot()));
        _settings.Save(new MediaToolSettings { FFmpegPath = ffmpegPath, FFprobePath = ffprobePath, Source = source });
        _resolver.Refresh();
        if (!_resolver.TryGetPaths(out var paths, out var error) || paths is null)
        {
            throw new InvalidOperationException(error ?? "安装完成后仍无法定位 FFmpeg。");
        }
        var version = await TryReadVersionAsync(paths.FFmpeg, cancellationToken);
        AppendLog("已记录 FFmpeg：" + paths.FFmpeg);
        Publish(new FfmpegInstallState(FfmpegInstallPhase.Completed,
            version is null ? "FFmpeg 已就绪。" : "FFmpeg 已就绪：" + version,
            100, null, paths.FFmpeg, Snapshot()));
    }

    /// <summary>读取 ffmpeg -version 的第一行；失败返回 null（不影响安装结论）。</summary>
    private async Task<string?> TryReadVersionAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        try
        {
            var first = (string?)null;
            await _runner.RunStreamingAsync(ffmpegPath, new[] { "-version" }, line =>
            {
                first ??= line.Trim();
            }, TimeSpan.FromSeconds(20), cancellationToken);
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "读取 FFmpeg 版本失败：{Path}", ffmpegPath);
            return null;
        }
    }

    /// <summary>判断能否用包管理器安装，并给出可直接执行的命令（Linux 非 root 时套 sudo）。</summary>
    private bool TryBuildPackageCommand(out string executable, out IReadOnlyList<string> arguments, out string? reason)
    {
        executable = string.Empty;
        arguments = Array.Empty<string>();
        reason = null;
        if (OperatingSystem.IsWindows())
        {
            reason = "Windows 不支持包管理器安装。";
            return false;
        }
        var aptGet = _runner.FindOnPath("apt-get") ?? SystemCommandRunner.FirstExisting("/usr/bin/apt-get", "/usr/local/bin/apt-get");
        if (aptGet is null)
        {
            reason = "当前系统未找到 apt-get，请手动指定 FFmpeg 路径。";
            return false;
        }
        executable = aptGet;
        arguments = BuildPackageArguments(aptGet, "install", "-y", "ffmpeg");
        return true;
    }

    /// <summary>生成 apt 命令参数；非 root 且存在 sudo 时套一层 sudo -n（不交互，拿不到权限就快速失败）。</summary>
    private IReadOnlyList<string> BuildPackageArguments(string aptGet, params string[] arguments)
    {
        if (SystemCommandRunner.IsRoot()) { return arguments; }
        var sudo = _runner.FindOnPath("sudo") ?? SystemCommandRunner.FirstExisting("/usr/bin/sudo", "/bin/sudo");
        if (sudo is null) { return arguments; }
        var withSudo = new List<string> { "-n", aptGet };
        withSudo.AddRange(arguments);
        return withSudo;
    }

    /// <summary>把日志与状态变化推给界面。</summary>
    private void Publish(FfmpegInstallState state)
    {
        lock (_lock) { _state = state; }
        Raise();
    }

    private void SetUserChoice(bool value)
    {
        lock (_lock) { _needsUserChoice = value; }
        Raise();
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) { return; }
        lock (_lock)
        {
            _log.Add(line.Trim());
            if (_log.Count > LogTailLimit) { _log.RemoveRange(0, _log.Count - LogTailLimit); }
        }
    }

    private void ClearLog()
    {
        lock (_lock) { _log.Clear(); }
    }

    private IReadOnlyList<string> Snapshot()
    {
        lock (_lock) { return _log.ToList(); }
    }

    private void Raise()
    {
        if (_disposed) { return; }
        try { Changed?.Invoke(); } catch (Exception error) { _logger.LogWarning(error, "FFmpeg 安装状态通知失败"); }
    }

    private static string ExecutableName(string baseName) => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    private static string Megabytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>释放：取消进行中的安装。</summary>
    public void Dispose()
    {
        _disposed = true;
        lock (_lock)
        {
            _runCancellation?.Cancel();
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }
}
