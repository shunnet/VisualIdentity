namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.sam;
using System.Collections.Concurrent;

/// <summary>
/// 一次 SAM 环境重置：释放已加载的会话，删除旧的应用私有 CUDA 13 / cuDNN 9 运行库，重新安装最新的固定版本并验证可加载。
/// 已下载的模型权重不动（SAM 模型有自己的更新与回退机制），避免重置时重新下载数 GB 文件。
/// 状态只用 volatile 与并发队列维护，不加锁。
/// </summary>
public sealed class SamEnvironmentResetOperation
{
    /// <summary>日志最多保留的行数。</summary>
    private const int MaximumLogLines = 500;

    /// <summary>带时间戳的日志。</summary>
    private readonly ConcurrentQueue<string> _logs = new();

    /// <summary>重置是否仍在执行。</summary>
    private volatile bool _active = true;

    /// <summary>安装与验证是否全部成功。</summary>
    private volatile bool _succeeded;

    /// <summary>最近一条进度或结果说明。</summary>
    private volatile string _message = "正在准备重置 SAM 环境…";

    /// <summary>重置是否仍在执行。</summary>
    public bool IsActive => _active;

    /// <summary>读取独立的状态副本，格式与 YOLO / Anomalib 的重置状态一致，界面可复用同一套日志显示。</summary>
    public TrainingEnvironmentResetStatus Snapshot() => new(_active, _succeeded, _message, _logs.ToArray());

    /// <summary>后台执行重置；取消和失败都记录结果并释放活动状态。</summary>
    /// <param name="installer">CUDA 运行库安装器。</param>
    /// <param name="sam">共享 SAM 运行时，重置前释放它持有的会话。</param>
    /// <param name="gpuId">验证运行库时使用的 GPU 编号。</param>
    /// <param name="token">宿主停止时取消。</param>
    internal async Task RunAsync(CudaRuntimeInstaller installer, SamOnnxRuntime sam, int gpuId, CancellationToken token)
    {
        string? backup = null;
        var succeeded = false;
        try
        {
            Log("释放已加载的 SAM 会话……");
            await sam.ReleaseSessionsAsync(token);
            backup = MoveAsideOldRuntime();
            Log("重新安装 CUDA 13 / cuDNN 9 运行库并验证……");
            var preparation = await installer.EnsureAsync(Log, token, gpuId);
            if (!preparation.GpuReady) { throw new InvalidOperationException(preparation.Message); }
            Log(preparation.Message);
            var failure = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory, Log);
            if (failure is not null) { throw new InvalidOperationException(failure); }
            _succeeded = true;
            succeeded = true;
            Log("SAM 环境重置完成：GPU 运行库可用，模型权重保持不变。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Log("SAM 环境重置已取消。"); }
        catch (Exception error) { Log("SAM 环境重置失败：" + error.Message); }
        finally
        {
            FinishBackup(backup, succeeded);
            _active = false;
        }
    }

    /// <summary>
    /// 把旧的运行库目录移到备份名下，而不是直接删除：新的安装成功后再删备份，失败或取消时恢复，
    /// 避免重置失败（例如网络中断）把原本可用的运行库也丢了。Windows 上已加载的 DLL 可能无法移动，此时保留并由随后的安装覆盖。
    /// </summary>
    /// <returns>备份目录；没有旧目录或无法移动时为空。</returns>
    private string? MoveAsideOldRuntime()
    {
        var directory = CudaRuntimeInstaller.InstallDirectory;
        if (!Directory.Exists(directory)) { Log("没有旧的运行库目录，直接安装。"); return null; }
        var backup = directory + ".old-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.Move(directory, backup);
            Log("旧的运行库已暂时移到备份目录，安装成功后删除，失败则恢复：" + backup);
            return backup;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log("旧的运行库正被占用，无法移动（" + error.Message + "），将由新的安装覆盖；必要时请重启应用。");
            return null;
        }
    }

    /// <summary>成功后删除旧运行库的备份；失败或取消时用备份恢复重置前的运行库。</summary>
    /// <param name="backup">备份目录；为空表示没有备份。</param>
    /// <param name="succeeded">重置是否成功。</param>
    private void FinishBackup(string? backup, bool succeeded)
    {
        if (backup is null) { return; }
        var directory = CudaRuntimeInstaller.InstallDirectory;
        try
        {
            if (succeeded) { Directory.Delete(backup, recursive: true); return; }
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
            Directory.Move(backup, directory);
            Log("已恢复重置前的运行库。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log("清理或恢复旧运行库时出错（" + error.Message + "），备份保留在：" + backup);
        }
    }

    /// <summary>追加一行日志并更新最近说明。</summary>
    private void Log(string message)
    {
        _message = message;
        _logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_logs.Count > MaximumLogLines && _logs.TryDequeue(out _)) { }
    }
}
