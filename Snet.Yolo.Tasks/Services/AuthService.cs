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
        if (result.Status)
        {
            var all = await _users.QueryAsync();
            if (all.GetDetails(out List<UserData>? list)) { CurrentUser = list?.FirstOrDefault(u => u.username == username); }
            return CurrentUser is not null;
        }
        return false;
    }

    /// <summary>
    /// 退出
    /// </summary>
    public void Logout() => CurrentUser = null;
}
