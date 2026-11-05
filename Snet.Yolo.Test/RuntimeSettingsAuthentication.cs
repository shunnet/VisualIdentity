using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Yolo.Test;

/// <summary>硬件配置权限测试的固定可信身份。</summary>
/// <param name="role">管理员或普通用户角色。</param>
internal sealed class RuntimeSettingsAuthentication(string role) : AuthenticationStateProvider
{
    /// <summary>返回隔离测试用户，不读取真实登录状态。</summary>
    /// <returns>带指定角色的已认证身份。</returns>
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "hardware-test"), new(ClaimTypes.Role, role)], "test"))));
}
