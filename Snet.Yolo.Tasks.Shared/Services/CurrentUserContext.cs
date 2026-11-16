using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为当前 Blazor 电路提供可信的登录用户标识。</summary>
public sealed class CurrentUserContext(AuthenticationStateProvider authenticationStateProvider)
{
    private System.Security.Claims.ClaimsPrincipal? _transferPrincipal;

    /// <summary>后台传输作用域使用入口已经验证的身份快照，不依赖已销毁的 Blazor 电路。</summary>
    internal void SetTransferPrincipal(System.Security.Claims.ClaimsPrincipal principal)
        => _transferPrincipal = new System.Security.Claims.ClaimsPrincipal(principal);

    /// <summary>获取可用于创建独立传输任务的可信身份。</summary>
    internal async Task<System.Security.Claims.ClaimsPrincipal> GetPrincipalAsync()
        => _transferPrincipal ?? (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
    /// <summary>检查可信登录状态中的管理员角色；用于修改宿主全局配置。</summary>
    public async ValueTask<bool> IsAdministratorAsync()
    {
        var principal = await GetPrincipalAsync();
        return principal.Identity?.IsAuthenticated == true && principal.IsInRole("Admin");
    }

    /// <summary>获取当前登录用户名；未登录时拒绝继续访问用户数据。</summary>
    public async ValueTask<string> GetRequiredUserNameAsync()
    {
        var principal = await GetPrincipalAsync();
        var userName = principal.Identity?.Name;
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("当前登录状态无效，请重新登录。");
        }
        return userName;
    }
}
