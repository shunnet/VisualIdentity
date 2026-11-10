using System.Text.Json;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 把媒体工具设置持久化到部署目录下的 tools/media-tools.json。
/// 用文件而不是数据库：安装发生在视频解析之前，且必须在任何工作区/数据库之外也能读到。
/// </summary>
public sealed class MediaToolSettingsStore
{
    private readonly object _lock = new();

    /// <summary>使用指定文件路径创建（测试用），默认落在部署目录 tools/media-tools.json。</summary>
    public MediaToolSettingsStore(string? path = null) => FilePath = path ?? DefaultFilePath;

    /// <summary>默认设置文件路径。</summary>
    public static string DefaultFilePath => Path.Combine(AppContext.BaseDirectory, "tools", "media-tools.json");

    /// <summary>设置文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>读取设置；文件缺失或损坏时返回空设置（不抛异常，安装流程不能被它阻断）。</summary>
    public MediaToolSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) { return new MediaToolSettings(); }
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<MediaToolSettings>(json, SerializerOptions) ?? new MediaToolSettings();
            }
            catch
            {
                return new MediaToolSettings();
            }
        }
    }

    /// <summary>保存设置（自动建目录）；写入失败向上抛出，由调用方决定如何提示。</summary>
    public void Save(MediaToolSettings settings)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
            settings.UpdatedAt = DateTimeOffset.Now;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
