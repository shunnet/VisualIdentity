using Snet.Model.data;

namespace Snet.Yolo.Server.@interface
{
    /// <summary>
    /// 用户操作接口
    /// </summary>
    public interface IUser
    {
        /// <summary>
        /// 添加用户
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <param name="role">角色</param>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> AddAsync(string username, string password, string role, CancellationToken token = default);

        /// <summary>
        /// 修改用户
        /// </summary>
        Task<OperateResult> UpdateAsync(int index, string? password, string? role, bool? active, CancellationToken token = default);

        /// <summary>
        /// 删除用户
        /// </summary>
        Task<OperateResult> DeleteAsync(int index, CancellationToken token = default);

        /// <summary>
        /// 查询单用户
        /// </summary>
        Task<OperateResult> QueryAsync(int index, CancellationToken token = default);

        /// <summary>
        /// 查询全部
        /// </summary>
        Task<OperateResult> QueryAsync(CancellationToken token = default);

        /// <summary>
        /// 校验登录
        /// </summary>
        Task<OperateResult> VerifyAsync(string username, string password, CancellationToken token = default);
    }
}
