using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snet.Yolo.Tasks.Services;
using System.IO.Compression;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// FFmpeg 自检/安装：Windows 静默下载安装（假下载器产出压缩包）、手动指定路径、
/// Linux 包管理器安装（假执行器）、中文字体补齐、失败处理与"不影响其它上传流程"。
/// 探测关闭系统级发现（DiscoverInstalledTools=false），否则开发机上装的 FFmpeg 会让安装器
/// 正确地"跳过安装"，这些用例就测不到安装逻辑了。
/// </summary>
public sealed class FfmpegInstallerTests
{
    /// <summary>假下载器：按请求生成一个含 bin/ffmpeg(.exe) 与 bin/ffprobe(.exe) 的 zip，并回调进度。</summary>
    private sealed class FakeDownloader : IFfmpegDownloader
    {
        private readonly bool _failWith;
        public int Calls { get; private set; }
        public List<(long Written, long? Total)> Progress { get; } = new();

        public FakeDownloader(bool failWith = false) => _failWith = failWith;

        public Task<(string ArchivePath, string Version)> DownloadLatestAsync(string destinationDirectory, Action<long, long?> onProgress, CancellationToken cancellationToken)
        {
            Calls++;
            if (_failWith) { throw new InvalidOperationException("下载失败：连接被重置"); }
            Directory.CreateDirectory(destinationDirectory);
            var archivePath = Path.Combine(destinationDirectory, "ffmpeg-test-essentials_build.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { MediaToolResolverTests.ExecutableName("ffmpeg"), MediaToolResolverTests.ExecutableName("ffprobe") })
                {
                    var entry = archive.CreateEntry("ffmpeg-test/bin/" + name);
                    using var stream = entry.Open();
                    stream.Write(new byte[] { 1, 2, 3 });
                }
            }
            onProgress(5, 10);
            onProgress(10, 10);
            Progress.Add((10, 10));
            return Task.FromResult((archivePath, "test-1.0"));
        }
    }

    /// <summary>假执行器：模拟包管理器安装（成功时"装"出 ffmpeg/ffprobe），并记录执行的命令。</summary>
    private sealed class FakeRunner : ISystemCommandRunner
    {
        private readonly bool _succeed;
        private readonly string? _installDirectory;
        public List<string> Commands { get; } = new();

        public FakeRunner(bool succeed, string? installDirectory = null)
        {
            _succeed = succeed;
            _installDirectory = installDirectory;
        }

        public Task<(int ExitCode, string Tail, bool Stalled)> RunStreamingAsync(string file, IReadOnlyList<string> arguments, Action<string> onOutput, TimeSpan stallTimeout, CancellationToken cancellationToken)
        {
            Commands.Add(file + " " + string.Join(' ', arguments));
            if (arguments.Contains("-version")) { onOutput("ffmpeg version test-1.0"); return Task.FromResult((0, string.Empty, false)); }
            if (!_succeed)
            {
                onOutput("E: Could not open lock file /var/lib/dpkg/lock-frontend");
                return Task.FromResult((100, string.Empty, false));
            }
            if (_installDirectory is not null) { MediaToolResolverTests.WriteFakeTools(_installDirectory); }
            onOutput("Setting up ffmpeg ...");
            return Task.FromResult((0, string.Empty, false));
        }

        public bool FileExists(string path) => File.Exists(path);

        public string? FindOnPath(string executableName)
        {
            if (executableName is "apt-get" or "sudo") { return "/usr/bin/" + executableName; }
            if (_installDirectory is null) { return null; }
            var candidate = Path.Combine(_installDirectory, executableName);
            return File.Exists(candidate) ? candidate : null;
        }
    }

    /// <summary>假中文字体提供方：用于验证"缺字体时顺带安装"的分支。</summary>
    private sealed class FakeFonts : ICjkFontProvider
    {
        public FakeFonts(bool hasFont) => HasCjkFont = hasFont;

        public bool HasCjkFont { get; private set; }
        public string? ResolvedPath { get; private set; }
        public int RecordCalls { get; private set; }

        public SkiaSharp.SKTypeface Resolve() => SkiaSharp.SKTypeface.Default;

        public void Record(string? fontPath)
        {
            RecordCalls++;
            ResolvedPath = fontPath;
            if (fontPath is not null) { HasCjkFont = true; }
        }

        public void Refresh() { }
    }

    private static FfmpegInstaller CreateInstaller(string installRoot, string settingsPath, IFfmpegDownloader downloader, ISystemCommandRunner runner, ICjkFontProvider? fonts = null)
    {
        var options = Options.Create(new MediaToolOptions { InstallDirectory = installRoot, DiscoverInstalledTools = false });
        var settings = new MediaToolSettingsStore(settingsPath);
        var resolver = new MediaToolResolver(options, settings);
        return new FfmpegInstaller(resolver, settings, downloader, runner, fonts ?? new FakeFonts(hasFont: true), NullLogger<FfmpegInstaller>.Instance);
    }

    /// <summary>
    /// 回归：提权时 <c>-n</c> 与 apt-get 路径都是 <b>sudo 的参数</b>，绝不能传进 apt-get 本身
    /// （线上出现过 "E: Command line option 'n' [from -n] is not understood"，就是这里拼错导致的）。
    /// </summary>
    [Fact]
    public void BuildPackageCommand_WithSudo_KeepsSudoArgumentsSeparateFromApt()
    {
        var (executable, arguments) = FfmpegInstaller.BuildPackageCommand("/usr/bin/apt-get", "/usr/bin/sudo", new[] { "install", "-y", "fonts-noto-cjk" });

        Assert.Equal("/usr/bin/sudo", executable);
        Assert.Equal(new[] { "-n", "/usr/bin/apt-get", "install", "-y", "fonts-noto-cjk" }, arguments);
        Assert.DoesNotContain("-n", arguments.Skip(2));
        Assert.DoesNotContain("/usr/bin/apt-get", arguments.Skip(2));
    }

    /// <summary>不需要提权时直接执行 apt-get，不加任何多余参数。</summary>
    [Fact]
    public void BuildPackageCommand_WithoutSudo_RunsAptDirectly()
    {
        var (executable, arguments) = FfmpegInstaller.BuildPackageCommand("/usr/bin/apt-get", null, new[] { "install", "-y", "ffmpeg" });

        Assert.Equal("/usr/bin/apt-get", executable);
        Assert.Equal(new[] { "install", "-y", "ffmpeg" }, arguments);
    }

    [Fact]
    public async Task StartAsync_WindowsDownload_ExtractsRecordsAndReportsProgress()
    {
        var root = MediaToolResolverTests.NewDirectory();
        var installRoot = Path.Combine(root, "tools", "ffmpeg");
        var settingsPath = Path.Combine(root, "media-tools.json");
        try
        {
            if (!OperatingSystem.IsWindows()) { return; }   // 下载安装路径只在 Windows 生效
            var downloader = new FakeDownloader();
            var installer = CreateInstaller(installRoot, settingsPath, downloader, new FakeRunner(succeed: true));
            var phases = new List<FfmpegInstallPhase>();
            installer.Changed += () => phases.Add(installer.State.Phase);

            await installer.StartAsync();

            Assert.Equal(FfmpegInstallPhase.Completed, installer.State.Phase);
            Assert.Equal(1, downloader.Calls);
            // 解压到 部署目录/tools/ffmpeg/win-<arch>，并记录到设置文件
            var installedDirectory = Path.GetDirectoryName(installer.State.InstalledPath!)!;
            Assert.Equal(installRoot, Path.GetDirectoryName(installedDirectory));
            Assert.StartsWith("win-", Path.GetFileName(installedDirectory), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(installedDirectory, MediaToolResolverTests.ExecutableName("ffmpeg")), installer.State.InstalledPath);
            var recorded = new MediaToolSettingsStore(settingsPath).Load();
            Assert.Equal("download", recorded.Source);
            Assert.Contains("win-", recorded.FFmpegPath!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(FfmpegInstallPhase.Downloading, phases);
            Assert.Contains(FfmpegInstallPhase.Extracting, phases);
            Assert.Contains(FfmpegInstallPhase.Verifying, phases);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_DownloadFailure_ReportsErrorWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(failWith: true), new FakeRunner(succeed: true));

            await installer.StartAsync();

            Assert.Equal(FfmpegInstallPhase.Failed, installer.State.Phase);
            Assert.Contains("下载失败", installer.State.Error!, StringComparison.Ordinal);
            Assert.True(installer.State.IsVisible);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ApplyManualPath_RecordsToolsAndMarksAvailable()
    {
        var root = MediaToolResolverTests.NewDirectory();
        var tools = MediaToolResolverTests.NewDirectory();
        try
        {
            MediaToolResolverTests.WriteFakeTools(tools);
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), new FakeRunner(succeed: true));

            var (ok, error) = await installer.ApplyManualPathAsync(tools);

            Assert.True(ok, error);
            Assert.True(installer.IsAvailable);
            Assert.Equal("manual", new MediaToolSettingsStore(Path.Combine(root, "media-tools.json")).Load().Source);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(tools, true);
        }
    }

    [Fact]
    public async Task ApplyManualPath_InvalidPath_FailsWithReason()
    {
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), new FakeRunner(succeed: true));

            var (ok, error) = await installer.ApplyManualPathAsync(Path.Combine(root, "不存在"));

            Assert.False(ok);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(FfmpegInstallPhase.Failed, installer.State.Phase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_PackageManager_RunsAptAndRecordsInstalledDirectory()
    {
        if (OperatingSystem.IsWindows()) { return; }   // 包管理器路径只在 Linux/macOS 生效
        var root = MediaToolResolverTests.NewDirectory();
        var installed = Path.Combine(root, "usr-bin");
        try
        {
            var runner = new FakeRunner(succeed: true, installDirectory: installed);
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), runner);

            await installer.StartAsync();

            Assert.Equal(FfmpegInstallPhase.Completed, installer.State.Phase);
            Assert.Contains(runner.Commands, command => command.Contains("apt-get") && command.Contains("install") && command.Contains("ffmpeg"));
            // 提权包装必须形如 "sudo -n <apt-get> install -y ffmpeg"：-n 不能被传给 apt-get
            Assert.Contains(runner.Commands, command =>
            {
                var parts = command.Split(' ');
                var aptIndex = Array.FindIndex(parts, part => part.EndsWith("apt-get", StringComparison.Ordinal));
                return aptIndex > 0 && !parts.Skip(aptIndex + 1).Contains("-n", StringComparer.Ordinal);
            });
            Assert.Equal("package", new MediaToolSettingsStore(Path.Combine(root, "media-tools.json")).Load().Source);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_PackageManagerFailure_EndsFailedWithSudoHint()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), new FakeRunner(succeed: false));

            await installer.StartAsync();

            Assert.Equal(FfmpegInstallPhase.Failed, installer.State.Phase);
            Assert.Contains("apt-get", installer.State.Error!, StringComparison.Ordinal);
            // 失败时必须给出可操作建议（手动指定路径）
            Assert.Contains("手动", installer.State.Error!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EnsureAsync_WhenAlreadyAvailable_DoesNotAskUser()
    {
        var root = MediaToolResolverTests.NewDirectory();
        var tools = MediaToolResolverTests.NewDirectory();
        try
        {
            MediaToolResolverTests.WriteFakeTools(tools);
            var settingsPath = Path.Combine(root, "media-tools.json");
            new MediaToolSettingsStore(settingsPath).Save(new MediaToolSettings { FFmpegPath = tools });
            var downloader = new FakeDownloader();
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), settingsPath, downloader, new FakeRunner(succeed: true));

            await installer.EnsureAsync();

            Assert.True(installer.IsAvailable);
            Assert.False(installer.NeedsUserChoice);
            Assert.Equal(0, downloader.Calls);
            Assert.Equal(FfmpegInstallPhase.Idle, installer.State.Phase);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(tools, true);
        }
    }

    [Fact]
    public async Task StartAsync_WithCjkFontReady_DoesNotInstallFontPackages()
    {
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var runner = new FakeRunner(succeed: true, installDirectory: Path.Combine(root, "bin"));
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), runner, new FakeFonts(hasFont: true));

            await installer.StartAsync();

            Assert.DoesNotContain(runner.Commands, command => command.Contains("fonts-", StringComparison.Ordinal));
            Assert.Contains(installer.State.LogTail, line => line.Contains("中文字体已就绪", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_WithoutCjkFont_InstallsFontOnLinuxOrWarnsOnWindows()
    {
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var fonts = new FakeFonts(hasFont: false);
            var runner = new FakeRunner(succeed: true, installDirectory: Path.Combine(root, "bin"));
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(), runner, fonts);

            await installer.StartAsync();

            if (OperatingSystem.IsWindows())
            {
                // Windows 上字体应随系统存在：缺字体时只提示，不跑包管理器
                Assert.DoesNotContain(runner.Commands, command => command.Contains("fonts-", StringComparison.Ordinal));
                Assert.Contains(installer.State.LogTail, line => line.Contains("中文字体", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(runner.Commands, command => command.Contains("fonts-noto-cjk", StringComparison.Ordinal));
            }
            // 字体缺失不能把整体状态打成失败（FFmpeg 已装好）
            Assert.NotEqual(FfmpegInstallPhase.Failed, installer.State.Phase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>阻塞型下载器：一直等到取消为止，用于验证"取消能真正中断安装"。</summary>
    private sealed class BlockingDownloader : IFfmpegDownloader
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(string ArchivePath, string Version)> DownloadLatestAsync(string destinationDirectory, Action<long, long?> onProgress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            onProgress(1, 100);
            await Task.Delay(Timeout.Infinite, cancellationToken);   // 只在取消时结束
            throw new InvalidOperationException("不应到达这里。");
        }
    }

    /// <summary>阻塞型命令执行器：同样只在取消时结束（Linux 包管理器路径用）。</summary>
    private sealed class BlockingRunner : ISystemCommandRunner
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(int ExitCode, string Tail, bool Stalled)> RunStreamingAsync(string file, IReadOnlyList<string> arguments, Action<string> onOutput, TimeSpan stallTimeout, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            onOutput("正在安装…");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("不应到达这里。");
        }

        public bool FileExists(string path) => File.Exists(path);
        public string? FindOnPath(string executableName) => executableName is "apt-get" or "sudo" ? "/usr/bin/" + executableName : null;
    }

    [Fact]
    public async Task Cancel_InterruptsRunningInstall_AndLeavesNoErrorBanner()
    {
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            var downloader = new BlockingDownloader();
            var runner = new BlockingRunner();
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), downloader, runner);

            var running = installer.StartAsync();
            var started = await Task.WhenAny(
                OperatingSystem.IsWindows() ? downloader.Started.Task : runner.Started.Task,
                Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.NotSame(started, running);   // 确认已经进入安装阶段（而不是立刻失败）

            installer.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(FfmpegInstallPhase.Cancelled, installer.State.Phase);
            Assert.False(installer.State.IsVisible);      // 取消不是错误：横幅不再显示
            Assert.Null(installer.State.Error);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Dismiss_ClearsFailureBanner()
    {
        var root = MediaToolResolverTests.NewDirectory();
        try
        {
            // Windows 走下载失败、其它平台走包管理器失败，两边都会进入 Failed
            var installer = CreateInstaller(Path.Combine(root, "tools", "ffmpeg"), Path.Combine(root, "media-tools.json"), new FakeDownloader(failWith: true), new FakeRunner(succeed: false));

            await installer.StartAsync();
            Assert.True(installer.State.IsVisible);

            installer.Dismiss();

            Assert.False(installer.State.IsVisible);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
