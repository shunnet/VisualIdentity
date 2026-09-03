using Snet.Model.data;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Server.@interface
{
    /// <summary>
    /// 工程操作接口
    /// </summary>
    public interface IProject
    {
        Task<OperateResult> AddAsync(ProjectData project, CancellationToken token = default);
        Task<OperateResult> UpdateAsync(ProjectData project, CancellationToken token = default);
        Task<OperateResult> DeleteAsync(string projectId, CancellationToken token = default);
        Task<OperateResult> QueryAsync(string projectId, CancellationToken token = default);
        Task<OperateResult> QueryAsync(CancellationToken token = default);
    }
}
