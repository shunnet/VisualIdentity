namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>
/// pip 安装步骤的代理重试策略：第 1 次尝试完全尊重用户环境（可附带应用配置的代理），
/// 失败后第 2 次尝试对该子进程显式禁用代理（--proxy "" 且清空代理环境变量 + NO_PROXY=*）。
/// </summary>
public static class PipProxyPolicy
{
    /// <summary>最大尝试次数（1 = 按计划执行；2 = 禁用代理重试）。</summary>
    public const int MaxAttempts = 2;

    /// <summary>禁用代理时需要从子进程环境中移除的变量（同时覆盖大写与 Linux 常用小写写法）。</summary>
    public static readonly IReadOnlyList<string> ProxyVariables = new[]
    {
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy",
    };

    /// <summary>禁用代理时需要写入的 NO_PROXY 变量名。</summary>
    public static readonly IReadOnlyList<string> NoProxyVariables = new[] { "NO_PROXY", "no_proxy" };

    /// <summary>禁用代理时 NO_PROXY 的取值。</summary>
    public const string NoProxyValue = "*";

    /// <summary>生成第 <paramref name="attempt"/> 次尝试（从 1 开始）的 pip 参数列表。</summary>
    /// <param name="baseArguments">规划器给出的原始 pip 参数。</param>
    /// <param name="attempt">尝试序号，从 1 开始。</param>
    /// <param name="configuredProxy">应用配置（Training:Proxy）的代理地址，可为空。</param>
    public static IReadOnlyList<string> BuildArguments(IReadOnlyList<string> baseArguments, int attempt, string? configuredProxy)
    {
        var args = new List<string>(baseArguments.Count + 2);
        args.AddRange(baseArguments);
        if (attempt <= 1)
        {
            // 第 1 次：完全按计划执行，仅在应用显式配置代理时追加 --proxy
            if (!string.IsNullOrWhiteSpace(configuredProxy)) { args.Add("--proxy"); args.Add(configuredProxy.Trim()); }
            return args;
        }
        // 第 2 次：显式空代理，覆盖失效的 http_proxy/https_proxy/all_proxy 以及 pip.conf 中的 proxy
        args.Add("--proxy");
        args.Add(string.Empty);
        return args;
    }

    /// <summary>生成第 <paramref name="attempt"/> 次尝试的子进程环境覆盖；返回 null 表示完全继承当前进程环境。</summary>
    /// <param name="attempt">尝试序号，从 1 开始。</param>
    public static IReadOnlyDictionary<string, string?>? BuildEnvironmentOverrides(int attempt)
    {
        if (attempt <= 1) { return null; }
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in ProxyVariables) { overrides[name] = null; }
        foreach (var name in NoProxyVariables) { overrides[name] = NoProxyValue; }
        return overrides;
    }

    /// <summary>第 1 次尝试的人类可读说明。</summary>
    public static string DescribeAttempt(int attempt) => attempt <= 1
        ? "按计划安装（沿用当前系统代理设置）"
        : "安装失败，改为禁用代理直连重试（--proxy \"\"，NO_PROXY=*）";

    /// <summary>所有尝试都失败时的可执行中文提示（点名代理/网络原因与处理办法）。</summary>
    public static string FailureMessage(string stepDescription)
        => stepDescription + "失败：通常是代理或网络不可达导致的（环境变量 http_proxy/https_proxy/all_proxy 或 pip.conf 中的失效代理会让 pip 报 “Cannot connect to proxy” / “No matching distribution found”）。"
            + "请修正或取消这些代理设置后重试；若企业网络必须走代理，请在 appsettings.json 配置 Training:Proxy 指向可用代理；"
            + "也可以先在本机用相同版本的 Python 手动执行 pip install 预装依赖（torch/torchvision/torchaudio/ultralytics），再重新开始训练。";
}
