using SkiaSharp;

namespace Snet.Yolo.Tasks.Services;

/// <summary>中文字体提供方（抽成接口便于单测安装流程）。</summary>
public interface ICjkFontProvider
{
    /// <summary>当前是否已有可用的中文字体（含中文字形）。</summary>
    bool HasCjkFont { get; }

    /// <summary>解析出的字体文件路径（系统字体匹配时可能为空）。</summary>
    string? ResolvedPath { get; }

    /// <summary>取用于绘制的字体；实在找不到时返回默认字体（此时中文会画成方框）。</summary>
    SKTypeface Resolve();

    /// <summary>记录字体文件（安装/发现后调用），并让下一次解析立刻生效。</summary>
    void Record(string? fontPath);

    /// <summary>清空缓存，重新解析。</summary>
    void Refresh();
}
