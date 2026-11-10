namespace Snet.Yolo.Tasks.Core.History;

using Snet.Yolo.Server;
using Snet.Yolo.Server.models;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 训练记录的文件存储：每次训练一个 JSON 文件，位于该工程训练目录的 <c>history</c> 子目录，
/// 随工程一起删除；写入使用临时文件加原子替换，读取时忽略损坏的文件。
/// </summary>
public sealed class TrainingHistoryStore
{
    /// <summary>JSON 读写选项：驼峰字段、枚举写成字符串，便于现场排查。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>训练根目录，默认是程序目录下的 <c>train</c>。</summary>
    private readonly string _trainingRoot;

    /// <summary>创建记录存储。</summary>
    /// <param name="trainingRoot">训练根目录；省略时使用程序目录下的 <c>train</c>。</param>
    public TrainingHistoryStore(string? trainingRoot = null)
    {
        _trainingRoot = Path.GetFullPath(trainingRoot ?? Path.Combine(AppContext.BaseDirectory, "train"));
    }

    /// <summary>返回某工程的训练目录：<c>train/{yolo|anomalib}/{用户}/{工程}</c>。</summary>
    /// <param name="kind">算法族。</param>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <returns>规范化的绝对路径。</returns>
    public string ProjectDirectory(ProjectKind kind, string owner, string projectId)
    {
        RequireSafeSegment(projectId, nameof(projectId));
        var algorithmRoot = Path.Combine(_trainingRoot, kind == ProjectKind.Anomalib ? "anomalib" : "yolo");
        return Path.GetFullPath(Path.Combine(TrainingStoragePath.OwnerDirectory(algorithmRoot, owner), projectId));
    }

    /// <summary>把工程训练目录内的相对路径解析为绝对路径，拒绝越出该目录的路径。</summary>
    /// <param name="kind">算法族。</param>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="relativePath">记录中保存的相对路径。</param>
    /// <returns>位于工程训练目录内的绝对路径。</returns>
    /// <exception cref="InvalidDataException">路径越出了工程训练目录。</exception>
    public string ResolveInProject(ProjectKind kind, string owner, string projectId, string relativePath)
    {
        var root = ProjectDirectory(kind, owner, projectId);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)) { throw new InvalidDataException("训练产物路径越出了工程目录。"); }
        return full;
    }

    /// <summary>
    /// 解析 YOLO 记录里的权重路径：必须位于该工程的 <c>runs</c> 目录之下且扩展名为 <c>.pt</c>。
    /// 记录来自磁盘，不能因为路径在工程目录内就当成可信的权重文件去下载，或交给 yolo 导出。
    /// </summary>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="record">训练记录。</param>
    /// <returns>权重文件的绝对路径。</returns>
    /// <exception cref="InvalidDataException">路径不在 runs 目录之下，或不是 .pt 文件。</exception>
    public string ResolveYoloModel(string owner, string projectId, TrainingRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var full = ResolveInProject(ProjectKind.Yolo, owner, projectId, record.ModelPath);
        var runsRoot = Path.Combine(ProjectDirectory(ProjectKind.Yolo, owner, projectId), "runs");
        if (!full.StartsWith(runsRoot + Path.DirectorySeparatorChar, PathComparison)
            || !string.Equals(Path.GetExtension(full), ".pt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("权重文件不在允许的位置。");
        }
        return full;
    }

    /// <summary>保存一次训练的记录；同一标识再次保存会替换原文件。</summary>
    /// <param name="owner">工程所有者。</param>
    /// <param name="record">训练记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(string owner, TrainingRunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireSafeSegment(record.Id, nameof(record));
        var directory = HistoryDirectory(record.Kind, owner, record.ProjectId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, record.Id + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, JsonOptions), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    /// <summary>读取一个工程的全部训练记录，按结束时间从新到旧排列。</summary>
    /// <param name="kind">算法族。</param>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录列表；没有记录或目录不存在时为空列表。</returns>
    public async Task<IReadOnlyList<TrainingRunRecord>> ListAsync(ProjectKind kind, string owner, string projectId, CancellationToken cancellationToken = default)
    {
        var directory = HistoryDirectory(kind, owner, projectId);
        if (!Directory.Exists(directory)) { return []; }
        var records = new List<TrainingRunRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await TryReadAsync(file, cancellationToken);
            if (record is not null && record.Kind == kind && string.Equals(record.ProjectId, projectId, StringComparison.Ordinal)) { records.Add(record); }
        }
        return records.OrderByDescending(static record => record.FinishedAt).ThenByDescending(static record => record.StartedAt).ToArray();
    }

    /// <summary>按标识读取一条记录。</summary>
    /// <param name="kind">算法族。</param>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="runId">训练运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录；不存在或标识不合法时为空。</returns>
    public async Task<TrainingRunRecord?> FindAsync(ProjectKind kind, string owner, string projectId, string runId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeSegment(runId) || !IsSafeSegment(projectId)) { return null; }
        var path = Path.Combine(HistoryDirectory(kind, owner, projectId), runId + ".json");
        return File.Exists(path) ? await TryReadAsync(path, cancellationToken) : null;
    }

    /// <summary>删除一条记录文件；模型和输出目录由调用方负责清理。</summary>
    /// <param name="kind">算法族。</param>
    /// <param name="owner">工程所有者。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="runId">训练运行标识。</param>
    /// <returns>确实删除了文件时为 true。</returns>
    public bool DeleteRecord(ProjectKind kind, string owner, string projectId, string runId)
    {
        if (!IsSafeSegment(runId) || !IsSafeSegment(projectId)) { return false; }
        var path = Path.Combine(HistoryDirectory(kind, owner, projectId), runId + ".json");
        if (!File.Exists(path)) { return false; }
        File.Delete(path);
        return true;
    }

    /// <summary>某工程的记录目录。</summary>
    private string HistoryDirectory(ProjectKind kind, string owner, string projectId)
        => Path.Combine(ProjectDirectory(kind, owner, projectId), "history");

    /// <summary>读取并反序列化一个记录文件；损坏或读取失败时返回空，不影响其他记录。</summary>
    private static async Task<TrainingRunRecord?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try { return JsonSerializer.Deserialize<TrainingRunRecord>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions); }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>要求目录片段不含分隔符或相对路径符号。</summary>
    private static void RequireSafeSegment(string value, string name)
    {
        if (!IsSafeSegment(value)) { throw new ArgumentException("标识无效。", name); }
    }

    /// <summary>检查单个目录片段不会越出父目录。</summary>
    private static bool IsSafeSegment(string value)
        => !string.IsNullOrWhiteSpace(value) && value == Path.GetFileName(value) && value is not "." and not "..";

    /// <summary>按当前操作系统比较规范化路径。</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
