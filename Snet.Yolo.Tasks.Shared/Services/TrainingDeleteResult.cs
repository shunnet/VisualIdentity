namespace Snet.Yolo.Tasks.Services;

/// <summary>删除一次训练记录及其产物的结果。</summary>
public enum TrainingDeleteResult
{
    /// <summary>记录和模型产物都已删除。</summary>
    Deleted = 0,

    /// <summary>记录不存在或不属于当前用户的工程。</summary>
    NotFound = 1,

    /// <summary>产物被占用或没有权限，已删除的部分不会回滚。</summary>
    Failed = 2,
}
