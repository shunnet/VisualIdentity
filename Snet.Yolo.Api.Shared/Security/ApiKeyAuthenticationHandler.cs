namespace Snet.Yolo.Api.Security;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>使用请求头中的 API Key 对服务调用方进行身份认证。</summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>API Key 认证方案名称。</summary>
    public const string SchemeName = "ApiKey";

    private readonly IConfiguration _configuration;

    /// <summary>初始化 API Key 认证处理器。</summary>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    /// <summary>验证 X-Api-Key 或 Bearer 请求头。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = Environment.GetEnvironmentVariable("SNET_YOLO_API_KEY");
        if (string.IsNullOrWhiteSpace(expected)) { expected = _configuration["ApiSecurity:ApiKey"]; }
        var supplied = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied))
        {
            var authorization = Request.Headers.Authorization.FirstOrDefault();
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                supplied = authorization["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(supplied))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(supplied, expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "api-client"), new Claim(ClaimTypes.Name, "api-client")],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <summary>以固定时间比较两个 UTF-8 字符串，降低密钥时序泄露风险。</summary>
    private static bool FixedTimeEquals(string supplied, string expected)
    {
        var suppliedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(supplied));
        var expectedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
