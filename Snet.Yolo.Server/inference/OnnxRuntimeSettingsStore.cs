using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Snet.Yolo.Server.inference;

/// <summary>为 API、Tasks 或桌面宿主原子保存硬件配置；写入失败不改变当前快照。</summary>
public sealed class OnnxRuntimeSettingsStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private OnnxRuntimeSettings _current;

    /// <summary>加载已保存配置；损坏文件使用宿主默认配置并记录日志。</summary>
    /// <param name="path">宿主指定的持久化配置路径。</param>
    /// <param name="defaults">无有效文件时的配置；省略则为 CPU。</param>
    /// <param name="logger">可选诊断日志。</param>
    public OnnxRuntimeSettingsStore(string path, OnnxRuntimeSettings? defaults = null, ILogger<OnnxRuntimeSettingsStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _current = defaults ?? new();
        _current.Validate();
        if (!File.Exists(_path)) { return; }
        try
        {
            var saved = JsonSerializer.Deserialize<OnnxRuntimeSettings>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("ONNX Runtime 配置为空。");
            saved.Validate();
            _current = saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { logger?.LogWarning(error, "无法读取 ONNX Runtime 设置，使用宿主默认配置。"); }
    }

    /// <summary>当前已保存的不可变配置；读取不触发 GPU 探测或下载。</summary>
    public OnnxRuntimeSettings Current { get { lock (_sync) { return _current; } } }

    /// <summary>先完成原子文件替换，再切换内存快照。</summary>
    /// <param name="settings">已经授权并完成硬件可用性验证的配置。</param>
    public void Save(OnnxRuntimeSettings settings)
    {
        settings.Validate();
        lock (_sync)
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
    }
}
