namespace Snet.Yolo.Tasks.Services;

/// <summary>指定模型的验证图片列表和当前选中图片。</summary>
public sealed record ValidationModelState(
    IReadOnlyList<ValidationImageState> Images,
    Guid? SelectedImageId);
