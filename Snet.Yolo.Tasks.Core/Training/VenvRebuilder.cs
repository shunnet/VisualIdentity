namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.IO;

/// <summary>虚拟环境重建：python -m venv 会复用残留目录，无法修复半个安装，必须先整目录删除。</summary>
public static class VenvRebuilder
{
    /// <summary>删除残留的 venv 目录；被占用或无权限时抛出带中文说明的异常。</summary>
    public static void DeleteExisting(string venvPath)
    {
        if (string.IsNullOrWhiteSpace(venvPath) || !Directory.Exists(venvPath)) { return; }
        try
        {
            Directory.Delete(venvPath, recursive: true);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("无法删除旧的虚拟环境目录（目录可能仍被进程占用）：" + venvPath + "，请关闭正在使用该环境的 python 进程或重启应用后重试。", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException("没有权限删除旧的虚拟环境目录：" + venvPath + "，请检查目录权限（Windows 下可以管理员身份运行）后重试。", error);
        }
    }

    /// <summary>清空并准备好 venv 目录，保证 python -m venv 从干净状态创建。</summary>
    public static void Reset(string venvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(venvPath);
        DeleteExisting(venvPath);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(venvPath));
        if (!string.IsNullOrEmpty(parent)) { Directory.CreateDirectory(parent); }
    }
}
