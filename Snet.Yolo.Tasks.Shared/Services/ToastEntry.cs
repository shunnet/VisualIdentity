namespace Snet.Yolo.Tasks.Services;

/// <summary>一条 Toast 消息。</summary>
public sealed record ToastEntry(string Id, string Message, ToastType Type);
