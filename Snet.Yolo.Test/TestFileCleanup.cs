using System.Diagnostics;

namespace Snet.Yolo.Test;

/// <summary>仅用于测试自建临时文件的收尾；短暂文件共享冲突允许等待，永久占用和其他错误仍抛出。</summary>
internal static class TestFileCleanup
{
    /// <summary>删除测试目录，最多等待两秒释放 Windows 文件共享锁，不重跑任何业务操作或断言。</summary>
    public static void DeleteDirectory(string path) => RetrySharingViolation(() => Directory.Delete(path, recursive: true));

    /// <summary>删除测试文件，保留业务异常及永久文件锁失败。</summary>
    public static void DeleteFile(string path) => RetrySharingViolation(() => File.Delete(path));

    /// <summary>只重试共享冲突及锁冲突；目录不存在视为已清理，其他错误立即抛出。</summary>
    private static void RetrySharingViolation(Action cleanup)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try { cleanup(); return; }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException error) when (OperatingSystem.IsWindows()
                && (error.HResult & 0xffff) is 32 or 33
                && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
            { Thread.Sleep(20); }
        }
    }
}
