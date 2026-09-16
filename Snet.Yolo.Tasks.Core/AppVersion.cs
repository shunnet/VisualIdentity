using System.Reflection;

namespace Snet.Yolo.Tasks.Core;

/// <summary>程序版本号（取宿主程序集的 Version，例如 1.0.1.1），用于界面左上角品牌区展示。</summary>
public static class AppVersion
{
    /// <summary>当前程序版本；宿主程序集拿不到版本信息时返回空串。</summary>
    public static string Current { get; } = Resolve(Assembly.GetEntryAssembly());

    /// <summary>读取指定程序集的版本号；为空时返回空串（调用方自行决定是否隐藏）。</summary>
    public static string Resolve(Assembly? assembly)
    {
        if (assembly is null) { return string.Empty; }
        try
        {
            // 优先用 InformationalVersion：<Version>x.y.z</Version> 会同时写入它与 AssemblyVersion，
            // 而持续集成里 InformationalVersion 可能带 "+commit" 后缀。
            var informational = Format(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return informational.Length > 0 ? informational : Format(assembly.GetName().Version?.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>规整版本文本：去空白、去掉 SourceLink 追加的 "+commit" 后缀。</summary>
    public static string Format(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion)) { return string.Empty; }
        var text = rawVersion.Trim();
        var plus = text.IndexOf('+');
        return plus > 0 ? text[..plus].Trim() : text;
    }
}
