using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server;
using Snet.Yolo.Server.inference;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Server.yolo.Models.Interfaces;
using System.Diagnostics;
using System.IO;

namespace Snet.Yolo.Tool.Services;

/// <summary>统一 WPF 五类验证的硬件设置、持久化与模型会话生命周期。</summary>
/// <param name="settings">桌面程序独立的原子配置存储。</param>
internal sealed class ToolRuntimeService(OnnxRuntimeSettingsStore settings) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<OnnxType, ToolSessionEntry> _sessions = [];
    private bool _disposed;

    // IR 8 / opset 13 的一元素 Identity 模型，只探测设备，不下载业务权重。
    private static readonly byte[] ProbeModel = Convert.FromBase64String("CAg6OwoQCgF4EgF5IghJZGVudGl0eRIFcHJvYmVaDwoBeBIKCggIARIECgIIAWIPCgF5EgoKCAgBEgQKAggBQgQKABAN");

    /// <summary>已保存的不可变设备快照；读取不初始化 CUDA。</summary>
    public OnnxRuntimeSettings Current => settings.Current;

    /// <summary>检查目标硬件并保存；准备或写入失败时保留原配置及会话。</summary>
    /// <param name="configuration">CPU 或指定编号的 NVIDIA GPU。</param>
    /// <param name="cancellationToken">在检查前及配置提交前取消。</param>
    /// <returns>本次成功提交的设备快照。</returns>
    public OnnxRuntimeSettings Configure(OnnxRuntimeSettings configuration, CancellationToken cancellationToken = default)
    {
        configuration.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); }
        if (configuration.Hardware == OnnxRuntimeHardware.Cuda)
        {
            if (!OnnxSessionOptions.SupportsCuda) { throw new NotSupportedException("当前原生运行库不支持 CUDA，请选择 CPU。"); }
            using var options = OnnxSessionOptions.CreateCuda(configuration.GpuId);
            using var probe = new InferenceSession(ProbeModel, options);
        }

        List<ToolSessionEntry> removed = [];
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var changed = settings.Current.CacheKey != configuration.CacheKey;
            settings.Save(configuration);
            if (changed)
            {
                foreach (var entry in _sessions.Values) { if (Retire(entry)) { removed.Add(entry); } }
                _sessions.Clear();
            }
        }
        foreach (var entry in removed) { DisposeEntry(entry); }
        return configuration;
    }

    /// <summary>按当前设备获取会话；每种任务只缓存最近使用的一个模型。</summary>
    /// <param name="modelPath">待识别的 ONNX 模型路径。</param>
    /// <param name="type">检测、分类、分割、姿态或定向检测类型。</param>
    /// <returns>必须在识别结束后释放的会话租约。</returns>
    public ToolSessionLease Acquire(string modelPath, OnnxType type)
    {
        var file = new FileInfo(Path.GetFullPath(modelPath));
        var stamp = file.LastWriteTimeUtc.Ticks;
        var length = file.Length;
        ToolSessionEntry? removed = null;
        ToolSessionLease lease;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sessions.TryGetValue(type, out var entry);
            if (entry is null || !string.Equals(entry.ModelPath, file.FullName, StringComparison.OrdinalIgnoreCase)
                || entry.ModelStamp != stamp || entry.ModelLength != length)
            {
                var snapshot = settings.Current;
                IExecutionProvider provider = snapshot.Hardware == OnnxRuntimeHardware.Cpu
                    ? new CpuExecutionProvider(file.FullName) : new CudaExecutionProvider(file.FullName, snapshot.GpuId);
                IdentityOperate operate;
                try { operate = new IdentityOperate(new IdentityData { Hardware = provider, IdentifyType = type }); }
                catch { provider.Dispose(); throw; }
                var replacement = new ToolSessionEntry(file.FullName, stamp, length, operate, provider);
                if (entry is not null && Retire(entry)) { removed = entry; }
                _sessions[type] = entry = replacement;
            }
            entry.Users++;
            lease = new ToolSessionLease(this, entry);
        }
        if (removed is not null) { DisposeEntry(removed); }
        return lease;
    }

    /// <summary>归还一次识别的使用权，在锁外释放不再使用的旧会话。</summary>
    /// <param name="entry">本次租用的会话。</param>
    internal void Release(ToolSessionEntry entry)
    {
        bool dispose;
        lock (_sync)
        {
            entry.Users--;
            dispose = entry.Retired && Retire(entry);
        }
        if (dispose) { DisposeEntry(entry); }
    }

    /// <summary>退役会话；活动识别完成前不取得释放权。</summary>
    /// <param name="entry">需要退役的会话。</param>
    /// <returns>是否由当前调用负责释放。</returns>
    private static bool Retire(ToolSessionEntry entry)
    {
        entry.Retired = true;
        if (entry.Users != 0 || entry.Disposed) { return false; }
        entry.Disposed = true;
        return true;
    }

    /// <summary>释放操作及提供程序；资源清理异常记录到诊断输出，不回滚已保存配置。</summary>
    /// <param name="entry">已取得释放权的条目。</param>
    private static void DisposeEntry(ToolSessionEntry entry)
    {
        try
        {
            try { entry.Operate.Dispose(); }
            finally { entry.Provider.Dispose(); }
        }
        catch (Exception error) { Trace.TraceError("WPF YOLO 会话释放失败：{0}", error); }
    }

    /// <summary>关闭运行服务；仍在识别的会话由最后一个使用者归还后释放。</summary>
    public void Dispose()
    {
        List<ToolSessionEntry> removed = [];
        lock (_sync)
        {
            if (_disposed) { return; }
            _disposed = true;
            foreach (var entry in _sessions.Values) { if (Retire(entry)) { removed.Add(entry); } }
            _sessions.Clear();
        }
        foreach (var entry in removed) { DisposeEntry(entry); }
    }
}
