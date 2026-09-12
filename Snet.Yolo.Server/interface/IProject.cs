using Snet.Model.data;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Server.@interface
{
    /// <summary>
    /// 工程操作接口
    /// </summary>
    public interface IProject
    {
        /// <summary>新增工程。</summary>
        Task<OperateResult> AddAsync(ProjectData project, CancellationToken token = default);

        /// <summary>更新工程。</summary>
        Task<OperateResult> UpdateAsync(ProjectData project, CancellationToken token = default);

        /// <summary>删除指定用户拥有的工程。</summary>
        Task<OperateResult> DeleteAsync(string owner, string projectId, CancellationToken token = default);

        /// <summary>查询指定用户拥有的工程。</summary>
        Task<OperateResult> QueryAsync(string owner, string projectId, CancellationToken token = default);

        /// <summary>查询指定用户的全部工程。</summary>
        Task<OperateResult> QueryByOwnerAsync(string owner, CancellationToken token = default);
    }
}
