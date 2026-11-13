namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 上传意图：页面在渲染时声明“当前入口提交的文件要做什么”。
/// 只保存纯数据、不含页面引用，因此任务可以脱离页面继续执行。
/// </summary>
/// <param name="Kind">上传任务类型。</param>
/// <param name="ScopeKey">进度和取消对应的稳定任务范围。</param>
/// <param name="ProjectId">目标工程标识。</param>
/// <param name="ClassName">分类图片所属标签。</param>
/// <param name="ModelIndex">YOLO 验证目标模型编号。</param>
/// <param name="RunId">Anomalib 验证目标模型的训练运行标识。</param>
public sealed record UploadIntent(UploadKind Kind, string ScopeKey, string ProjectId = "", string ClassName = "", int ModelIndex = -1, string RunId = "");
