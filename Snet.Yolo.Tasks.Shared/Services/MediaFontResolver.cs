using SkiaSharp;

namespace Snet.Yolo.Tasks.Services;

/// <summary>中文字体提供方（抽成接口便于单测安装流程）。</summary>
public interface ICjkFontProvider
{
    /// <summary>当前是否已有可用的中文字体（含中文字形）。</summary>
    bool HasCjkFont { get; }

    /// <summary>解析出的字体文件路径（系统字体匹配时可能为空）。</summary>
    string? ResolvedPath { get; }

    /// <summary>取用于绘制的字体；实在找不到时返回默认字体（此时中文会画成方框）。</summary>
    SKTypeface Resolve();

    /// <summary>记录字体文件（安装/发现后调用），并让下一次解析立刻生效。</summary>
    void Record(string? fontPath);

    /// <summary>清空缓存，重新解析。</summary>
    void Refresh();
}

/// <summary>
/// 中文字体解析：视频结果帧由 SkiaSharp 绘制，若不显式指定字体就会用 SKTypeface.Default
/// （Windows 上是 Segoe UI、Linux 上常是 DejaVu Sans），这些字体<b>没有中文字形</b>，
/// 标签里的中文会被画成方框（tofu）。这里按"安装记录 → 系统字体匹配 → 常见字体文件"的顺序找中文字体。
/// </summary>
public sealed class MediaFontResolver : ICjkFontProvider, IDisposable
{
    /// <summary>用于判断字体是否覆盖中文的样字。</summary>
    private const string ProbeText = "纸";

    private readonly MediaToolSettingsStore _settings;
    private readonly object _lock = new();
    private SKTypeface? _cached;
    private string? _resolvedPath;
    private bool _resolved;
    private bool _usedFallback;

    /// <summary>创建解析器（单例，结果缓存）。</summary>
    public MediaFontResolver(MediaToolSettingsStore settings) => _settings = settings;

    /// <summary>当前是否已有可用中文字体。</summary>
    public bool HasCjkFont
    {
        get
        {
            EnsureResolved();
            lock (_lock) { return !_usedFallback; }
        }
    }

    /// <summary>字体文件路径（系统字体匹配时为空）。</summary>
    public string? ResolvedPath
    {
        get
        {
            EnsureResolved();
            lock (_lock) { return _resolvedPath; }
        }
    }

    /// <summary>取绘制用字体（缓存）。</summary>
    public SKTypeface Resolve()
    {
        EnsureResolved();
        lock (_lock) { return _cached ?? SKTypeface.Default; }
    }

    /// <summary>记录字体文件并刷新（安装完中文字体后调用）。</summary>
    public void Record(string? fontPath)
    {
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            var settings = _settings.Load();
            settings.CjkFontPath = fontPath;
            _settings.Save(settings);
        }
        Refresh();
    }

    /// <summary>清空缓存，下次解析重新查找（安装字体后字体服务缓存可能仍是旧的）。</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            _cached?.Dispose();
            _cached = null;
            _resolved = false;
            _usedFallback = false;
            _resolvedPath = null;
        }
    }

    /// <summary>各平台常见的中文字体文件（按优先级）。</summary>
    public static IReadOnlyList<string> KnownFontFiles { get; } = OperatingSystem.IsWindows()
        ? new[]
        {
            @"C:\Windows\Fonts\msyh.ttc", @"C:\Windows\Fonts\msyhbd.ttc", @"C:\Windows\Fonts\simhei.ttf",
            @"C:\Windows\Fonts\simsun.ttc", @"C:\Windows\Fonts\Deng.ttf",
        }
        : OperatingSystem.IsMacOS()
            ? new[]
            {
                "/System/Library/Fonts/PingFang.ttc", "/System/Library/Fonts/STHeiti Light.ttc",
                "/System/Library/Fonts/Hiragino Sans GB.ttc", "/Library/Fonts/Arial Unicode.ttf",
            }
            : new[]
            {
                "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
                "/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf",
                "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
                "/usr/share/fonts/google-noto-cjk/NotoSansCJK-Regular.ttc",
                "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc",
                "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
                "/usr/share/fonts/truetype/arphic/uming.ttc",
            };

    /// <summary>判断字体是否真的包含中文字形（避免挑到一个同样没有中文的字体）。</summary>
    public static bool CoversChinese(SKTypeface? typeface)
    {
        if (typeface is null) { return false; }
        try
        {
            using var font = new SKFont(typeface);
            return font.ContainsGlyphs(ProbeText);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureResolved()
    {
        lock (_lock)
        {
            if (_resolved) { return; }
            _resolved = true;
            var recorded = _settings.Load().CjkFontPath;
            foreach (var candidate in CandidateTypefaces(recorded))
            {
                if (!CoversChinese(candidate.Typeface)) { candidate.Typeface?.Dispose(); continue; }
                _cached = candidate.Typeface;
                _resolvedPath = candidate.Path;
                return;
            }
            _cached = SKTypeface.Default;
            _usedFallback = true;
        }
    }

    private static IEnumerable<(SKTypeface? Typeface, string? Path)> CandidateTypefaces(string? recordedPath)
    {
        if (!string.IsNullOrWhiteSpace(recordedPath) && File.Exists(recordedPath))
        {
            yield return (TryLoadFile(recordedPath), recordedPath);
        }
        yield return (TryMatchCharacter(), null);
        foreach (var path in KnownFontFiles)
        {
            if (File.Exists(path)) { yield return (TryLoadFile(path), path); }
        }
    }

    private static SKTypeface? TryLoadFile(string path)
    {
        try { return SKTypeface.FromFile(path, 0); }
        catch { return null; }
    }

    private static SKTypeface? TryMatchCharacter()
    {
        try { return SKFontManager.Default.MatchCharacter('中'); }
        catch { return null; }
    }

    /// <summary>释放缓存的字体对象。</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _cached?.Dispose();
            _cached = null;
        }
    }
}
