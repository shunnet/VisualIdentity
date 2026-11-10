using System.Text.Json;
using SkiaSharp;

namespace Snet.Yolo.Tasks.Services;

/// <summary>将站点外观原子保存到现有持久化数据目录，并通知活动页面刷新品牌。</summary>
public sealed class AppearanceSettingsStore
{
    /// <summary>品牌图片最大上传字节数。</summary>
    public const int MaximumLogoBytes = 1024 * 1024;
    private readonly string _path;
    private readonly object _lock = new();
    private AppearanceSettings _current = new();
    /// <summary>创建配置存储；自定义路径供隔离测试使用。</summary>
    public AppearanceSettingsStore(string? path = null, ILogger<AppearanceSettingsStore>? logger = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "settings", "appearance.json");
        if (!File.Exists(_path)) { return; }
        try
        {
            var saved = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(_path)) ?? new();
            Validate(saved);
            if (saved.LogoDataUrl is { } logo)
            {
                const string prefix = "data:image/png;base64,";
                if (!logo.StartsWith(prefix, StringComparison.Ordinal)) { throw new InvalidDataException("Invalid logo."); }
                saved = saved with { LogoDataUrl = CreateLogoDataUrl(Convert.FromBase64String(logo[prefix.Length..])) };
            }
            _current = saved;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or FormatException)
        { logger?.LogWarning(error, "无法读取站点外观配置，使用默认配置。"); }
    }
    /// <summary>当前已保存的不可变配置。</summary>
    public AppearanceSettings Current { get { lock (_lock) { return _current; } } }
    /// <summary>保存成功后触发。</summary>
    public event Action? Changed;

    /// <summary>校验并原子写入配置；写入失败不修改当前快照。</summary>
    public void Save(AppearanceSettings settings)
    {
        Validate(settings);
        if (settings.LogoDataUrl is { } logo && logo != Current.LogoDataUrl)
        {
            const string prefix = "data:image/png;base64,";
            if (!logo.StartsWith(prefix, StringComparison.Ordinal)) { throw new InvalidDataException("Invalid logo."); }
            settings = settings with { LogoDataUrl = CreateLogoDataUrl(Convert.FromBase64String(logo[prefix.Length..])) };
        }
        settings = settings with { Title = settings.Title.Trim(), Description = settings.Description.Trim() };
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
                File.Move(temporary, _path, overwrite: true);
                _current = settings;
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
        Changed?.Invoke();
    }

    /// <summary>验证图片真实格式及像素尺寸，重新编码后再供匿名登录页使用。</summary>
    public static string CreateLogoDataUrl(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaximumLogoBytes) { throw new InvalidDataException("Invalid logo size."); }
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp)
            || codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 2048)
        { throw new InvalidDataException("Invalid logo format or dimensions."); }
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) { throw new InvalidDataException("Invalid logo image."); }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null || encoded.Size > MaximumLogoBytes) { throw new InvalidDataException("Invalid logo size."); }
        return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
    }

    private static void Validate(AppearanceSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Title) || settings.Title.Length > 80 || settings.Description is null || settings.Description.Length > 200
            || settings.Language is not ("zh-CN" or "en-US") || settings.Theme is not (null or "light" or "dark"))
        { throw new ArgumentException("Invalid appearance settings."); }
    }
}
