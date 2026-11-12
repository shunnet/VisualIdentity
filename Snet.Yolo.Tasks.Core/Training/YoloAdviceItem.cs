namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>一条推荐值的依据：界面用资源键本地化，参数按位置代入。</summary>
/// <param name="Field">说明的是哪一项设置。</param>
/// <param name="ResourceKey">本地化资源键。</param>
/// <param name="Arguments">格式化参数。</param>
public sealed record YoloAdviceItem(YoloAdviceField Field, string ResourceKey, IReadOnlyList<object> Arguments);
