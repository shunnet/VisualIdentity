namespace Snet.Yolo.Tasks.Core.Localization;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;

/// <summary>
/// 界面语言管理器：持有当前文化，按 key 取本地化文本，并通知订阅者语言变更。
/// Blazor Server 中应注册为 Scoped（每电路独立），保证多用户互不影响。
/// </summary>
public sealed class LanguageManager
{
    private const string ResourceBaseName = "Snet.Yolo.Tasks.Core.Localization.AppResource";

    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(LanguageManager).Assembly);

    private CultureInfo currentCulture = CultureInfo.GetCultureInfo(DefaultLanguageCode);

    /// <summary>默认语言代码（应用首次加载使用中文）。</summary>
    public const string DefaultLanguageCode = "zh-CN";

    /// <summary>
    /// 受支持的语言选项（代码 / 显示名）。
    /// </summary>
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        new[] { new LanguageOption("zh-CN", "中文"), new LanguageOption("en-US", "English") };

    /// <summary>当前语言代码（如 zh-CN、en-US）。</summary>
    public string CurrentLanguageCode { get; private set; } = DefaultLanguageCode;

    /// <summary>语言切换后触发；订阅方应刷新界面。</summary>
    public event Action? LanguageChanged;

    /// <summary>切换语言并通知订阅者；未知代码将被忽略。</summary>
    /// <param name="cultureCode">IETF 语言代码。</param>
    public void SetLanguage(string cultureCode)
    {
        if (SupportedLanguages.All(l => l.Code != cultureCode))
        {
            return;
        }

        CurrentLanguageCode = cultureCode;
        currentCulture = CultureInfo.GetCultureInfo(cultureCode);
        CultureInfo.DefaultThreadCurrentCulture = currentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = currentCulture;
        LanguageChanged?.Invoke();
    }

    /// <summary>按当前语言取 key 对应的文本；缺失时原样返回 key。</summary>
    /// <param name="key">资源键（见 AppResource.resx）。</param>
    /// <returns>本地化文本。</returns>
    public string Translate(string key) => ResourceManager.GetString(key, currentCulture) ?? key;

    /// <summary>全部资源键（默认文化英文为准）。</summary>
    public static IEnumerable<string> SupportedKeys
    {
        get
        {
            // 说明：ResourceManager 为静态实例，会内部缓存已创建的 ResourceSet，
            // 此处不可 Dispose，否则后续 Translate 读取同一集合将抛出 ObjectDisposedException。
            var set = ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            if (set is null)
            {
                yield break;
            }

            foreach (var key in set)
            {
                if (key is string name)
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// 语言选项值对象。
    /// </summary>
    /// <param name="Code">IETF 语言代码。</param>
    /// <param name="DisplayName">界面上展示的语言名。</param>
    public sealed record LanguageOption(string Code, string DisplayName);
}
