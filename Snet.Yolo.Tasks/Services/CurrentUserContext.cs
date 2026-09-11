using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为当前 Blazor 电路提供可信的登录用户标识。</summary>
public sealed class CurrentUserContext(AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>获取当前登录用户名；未登录时拒绝继续访问用户数据。</summary>
    public async ValueTask<string> GetRequiredUserNameAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userName = state.User.Identity?.Name;
        if (state.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("当前登录状态无效，请重新登录。");
        }
        return userName;
    }
}
