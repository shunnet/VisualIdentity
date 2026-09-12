using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Tasks.Services;

/// <summary>用户管理服务：经 Snet.Yolo.Server.UserOperate 走用户表。</summary>
public sealed class UserService
{
    private readonly UserOperate _users;
    public UserService(UserOperate users) => _users = users;

    public async Task<List<UserData>> ListAsync()
    {
        var r = await _users.QueryAsync();
        return r.GetDetails(out List<UserData>? list) ? (list ?? new()) : new();
    }
    public Task<OperateResult> AddAsync(string username, string password, string role) => _users.AddAsync(username, password, role);
    public Task<OperateResult> UpdateAsync(int index, string? password, string? role, bool? active) => _users.UpdateAsync(index, password, role, active);
    public Task<OperateResult> DeleteAsync(int index) => _users.DeleteAsync(index);
}
