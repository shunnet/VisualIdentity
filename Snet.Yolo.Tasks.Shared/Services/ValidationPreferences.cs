using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Snet.Yolo.Tasks.Services;

/// <summary>在 Tasks 持久数据卷中保存独立的验证选项，不将图片或推理结果写入参数文件。</summary>
public sealed class ValidationPreferences
{
    private readonly string _root;
    private readonly ILogger<ValidationPreferences> _logger;
    private readonly object _gate = new();

    /// <summary>使用与应用设置相同的持久数据目录，测试可指定独立根目录。</summary>
    public ValidationPreferences(ILogger<ValidationPreferences> logger, string? root = null)
    {
        _logger = logger;
        _root = root ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "settings", "validation");
    }

    /// <summary>读取所属用户、页面和模型的已保存选项。</summary>
    public T? Read<T>(string owner, string key) where T : class
    {
        var path = PathFor(owner, key);
        lock (_gate)
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(error, "无法读取验证参数 {Key}", key);
                return null;
            }
        }
    }

    /// <summary>原子替换参数文件，避免进程退出或并发保存留下半个 JSON。</summary>
    public bool Save<T>(string owner, string key, T value)
    {
        var path = PathFor(owner, key);
        var temporary = path + ".tmp";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(value));
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(error, "无法保存验证参数 {Key}", key);
                return false;
            }
        }
    }

    /// <summary>稳定键使用摘要，模型标识中的分隔符不能变成文件路径。</summary>
    private string PathFor(string owner, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return Path.Combine(_root, UserStoragePath.Segment(owner), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    }
}
