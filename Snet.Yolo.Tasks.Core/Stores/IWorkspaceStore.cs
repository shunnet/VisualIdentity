using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Snet.Yolo.Tasks.Core.Stores;

/// <summary>
/// 工作区工程仓储接口（M1：SQLite 持久化；UI 在 M2 里程碑接入）。
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>保存（覆盖同名）工程 JSON。</summary>
    Task SaveAsync(string name, string json, CancellationToken cancellationToken = default);

    /// <summary>按名读取工程 JSON；不存在返回 null。</summary>
    Task<string?> LoadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>列出全部工程名。</summary>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>删除工程；不存在则忽略。</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
