using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Yolo.Test;

/// <summary>为上传回归提供独立的可信用户，避免测试共享真实用户目录。</summary>
internal sealed class UploadTestAuthentication(string owner) : AuthenticationStateProvider
{
    /// <summary>返回固定的已认证测试身份。</summary>
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, owner)], "upload-test"))));
}
