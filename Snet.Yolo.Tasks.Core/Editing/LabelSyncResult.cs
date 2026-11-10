namespace Snet.Yolo.Tasks.Core.Editing;

/// <summary>标签同步的结果：改名的 region 数、删除的 region 数、受影响的图片数。</summary>
/// <param name="RenamedRegions">跟着改名更新的标注框数量。</param>
/// <param name="RemovedRegions">因为标签被删除（或引用了已不存在的标签）而一并删除的标注框数量。</param>
/// <param name="AffectedTasks">有改动的图片数量。</param>
public sealed record LabelSyncResult(int RenamedRegions, int RemovedRegions, int AffectedTasks)
{
    /// <summary>是否真的改动了数据。</summary>
    public bool Changed => RenamedRegions > 0 || RemovedRegions > 0;
}
