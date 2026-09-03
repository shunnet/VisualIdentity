using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Model.data;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 登录认证服务（对接 Snet.Yolo.Server 的 UserOperate）。
/// </summary>
public sealed class AuthService
{
    private readonly UserOperate _users;

    /// <summary>
    /// 当前登录用户
    /// </summary>
    public UserData? CurrentUser { get; private set; }

    /// <summary>
    /// 是否已登录
    /// </summary>
    public bool IsAuthenticated => CurrentUser is not null;

    /// <summary>
    /// 角色
    /// </summary>
    public string Role => CurrentUser?.role ?? "User";

    /// <summary>
    /// 构造
    /// </summary>
    public AuthService(UserOperate users)
    {
        _users = users;
    }

    /// <summary>
    /// 登录
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password)
    {
        var result = await _users.VerifyAsync(username, password);
        if (result.Status && result.GetDetails(out dynamic? data) && data is not null)
        {
            var idx = (int?)data?.index ?? -1;
            var q = await _users.QueryAsync(idx);
            if (q.GetDetails(out List<UserData>? list) && list is { Count: > 0 }) { CurrentUser = list[0]; return true; }
        }
        return false;
    }

    /// <summary>
    /// 退出
    /// </summary>
    public void Logout() => CurrentUser = null;
}
