namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 上传意图：页面在渲染时声明“当前入口提交的文件要做什么”。
/// 只保存纯数据、不含页面引用，因此任务可以脱离页面继续执行。
/// </summary>
public sealed record UploadIntent(UploadKind Kind, string ScopeKey, string ProjectId = "", string ClassName = "", int ModelIndex = -1);
