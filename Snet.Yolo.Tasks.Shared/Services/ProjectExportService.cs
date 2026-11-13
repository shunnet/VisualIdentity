using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Serialization.Export;

namespace Snet.Yolo.Tasks.Services;

/// <summary>验证工程所有权并规划 YOLO 导出；原图由 ZIP 写入器逐个读取，不积累图片字节。</summary>
/// <param name="workspaces">用于按可信用户名读取持久化工程的工作区服务。</param>
public sealed class ProjectExportService(WorkspaceService workspaces)
{
    /// <summary>读取当前用户的 YOLO 工程，准备标注或含原图的流式 ZIP 清单。</summary>
    /// <param name="owner">HTTP 认证上下文中的可信用户名。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="format">界面允许的 YOLO 或 YOLO (with images)。</param>
    /// <param name="cancellationToken">下载请求取消令牌。</param>
    /// <returns>导出清单；工程不存在、类型不匹配或不属于当前用户时为空。</returns>
    public async Task<ExportResult?> PrepareAsync(string owner, string projectId, string format, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!ExportFormats.All.Contains(format, StringComparer.Ordinal)) { throw new ArgumentException("不支持的导出格式。", nameof(format)); }
        var project = await workspaces.GetProjectForOwnerAsync(owner, projectId, cancellationToken);
        if (project?.Kind != ProjectKind.Yolo) { return null; }
        var config = LabelingConfigParser.Parse(project.LabelConfigXml);
        if (format == ExportFormats.Yolo) { return ExportService.Yolo(project.Tasks, config); }
        var location = WorkspaceService.GetProjectUploadLocationForOwner(owner, projectId);
        var root = Path.GetFullPath(location.Directory);

        // 规划和实际打开时都重新验证路径，避免文件被替换为指向其他目录的链接。
        string ResolveImage(string imageRef)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Uri.UnescapeDataString(ExportService.ResolveFileName(imageRef));
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || fileName.Contains('/') || fileName.Contains('\\')
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("工程原图引用包含无效文件名。");
            }
            var path = Path.GetFullPath(Path.Combine(root, fileName));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidDataException("工程原图超出了允许的目录。");
            }
            if (!File.Exists(path)) { throw new FileNotFoundException("导出失败，工程原图不存在：" + fileName); }
            // 只允许工程目录中的普通文件；规划和实际打开时均检查，避免链接指向其他用户的数据。
            foreach (var item in new[] { Path.GetDirectoryName(root)!, root, path })
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("工程原图路径包含不允许的链接。"); }
            }
            return path;
        }

        return ExportService.YoloWithImageFiles(project.Tasks, config, ResolveImage);
    }
}
