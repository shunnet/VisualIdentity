using System.Text.Json;
using Snet.Yolo.Tasks.Core.Config;

namespace Snet.Yolo.Tasks.Services;

/// <summary>将全站推理硬件原子保存到 data 持久化卷；写入失败不改变当前快照。</summary>
public sealed class OnnxRuntimeSettingsStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private OnnxRuntimeSettings _current = new();

    /// <summary>加载已保存配置；损坏文件使用 CPU 默认配置并记录日志。</summary>
    /// <param name="path">可选隔离配置路径；默认使用应用持久化数据目录。</param>
    /// <param name="logger">可选诊断日志。</param>
    public OnnxRuntimeSettingsStore(string? path = null, ILogger<OnnxRuntimeSettingsStore>? logger = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "settings", "onnx-runtime.json");
        if (!File.Exists(_path)) { return; }
        try
        {
            var saved = JsonSerializer.Deserialize<OnnxRuntimeSettings>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("ONNX Runtime 配置为空。");
            saved.Validate();
            _current = saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { logger?.LogWarning(error, "无法读取 ONNX Runtime 设置，使用 CPU 默认配置。"); }
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
