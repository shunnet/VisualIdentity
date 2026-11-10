namespace Snet.Yolo.Tasks.Core.Config;

using System;

/// <summary>配置结构错误（XML 无法解析 / 根不是 View）。</summary>
public sealed class ConfigFormatException : Exception
{
    /// <summary>构造。</summary>
    public ConfigFormatException(string message) : base(message)
    {
    }
}
