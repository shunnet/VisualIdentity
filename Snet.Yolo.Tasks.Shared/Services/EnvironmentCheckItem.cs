namespace Snet.Yolo.Tasks.Services;

/// <summary>环境自检的一项检查结果。</summary>
/// <param name="Key">检查项的资源键后缀，界面按 <c>EnvCheck&lt;Key&gt;</c> 翻译。</param>
/// <param name="Ok">该项是否通过。</param>
/// <param name="Detail">版本、设备名或失败原因等说明，保持原文。</param>
/// <param name="Required">该项失败是否意味着环境需要修复；不要求时仅作提示。</param>
public sealed record EnvironmentCheckItem(string Key, bool Ok, string Detail, bool Required = true);
