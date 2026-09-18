namespace Snet.Yolo.Tasks.Core.Editing;

using System.Text.Json.Nodes;
using Models = Snet.Yolo.Tasks.Core.Models;
using Config = Snet.Yolo.Tasks.Core.Config;

/// <summary>标签同步的结果：改名的 region 数、删除的 region 数、受影响的图片数。</summary>
/// <param name="RenamedRegions">跟着改名更新的标注框数量。</param>
/// <param name="RemovedRegions">因为标签被删除（或引用了已不存在的标签）而一并删除的标注框数量。</param>
/// <param name="AffectedTasks">有改动的图片数量。</param>
public sealed record LabelSyncResult(int RenamedRegions, int RemovedRegions, int AffectedTasks)
{
    /// <summary>是否真的改动了数据。</summary>
    public bool Changed => RenamedRegions > 0 || RemovedRegions > 0;
}

/// <summary>
/// 把"编辑标签"的结果同步到已有标注。
///
/// 背景：保存标签编辑原本只重写了项目配置，标注里的 region 原封不动 ——
/// 于是删掉一个标签后，标注页照样把框画出来（颜色退化成调色板色），统计也照样计数，
/// 只有导出训练数据时被静默丢掉。三处口径不一致，使用者根本看不出问题。
///
/// 这里统一成一条规则：<b>region 上的标签必须存在于当前配置</b>。
///   · 改名（旧名→新名）→ region 上的旧名跟着改；
///   · 已删除（或历史遗留的未知标签）→ 连同该 region 一起删除。
/// </summary>
public static class LabelSync
{
    /// <summary>能在 region 上承载"标签"的行类型（<c>ResultRow.Type</c> 本身就是 Result.Value 里的字段名）。</summary>
    private static readonly HashSet<string> LabelRowTypes = new(StringComparer.Ordinal)
    {
        Models.RegionType.RectangleLabels,
        Models.RegionType.PolygonLabels,
        Models.RegionType.KeyPointLabels,
        Models.RegionType.EllipseLabels,
        Models.RegionType.BrushLabels,
        Models.RegionType.Labels,
        Models.RegionType.Choices,
    };

    /// <summary>收集配置里全部标签名（矩形/多边形/关键点…以及分类用的 Choices）。</summary>
    public static HashSet<string> CollectNames(Config.LabelingConfigModel config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in config.Controls)
        {
            foreach (var label in control.Labels) { if (!string.IsNullOrWhiteSpace(label.Value)) { names.Add(label.Value.Trim()); } }
            foreach (var choice in control.Choices) { if (!string.IsNullOrWhiteSpace(choice.Value)) { names.Add(choice.Value.Trim()); } }
        }
        return names;
    }

    /// <summary>
    /// 推断"改名"：同一个颜色在编辑前后对应了不同的名字，就认为这个标签是被改名而不是被删掉+新建
    /// （标签颜色在编辑器里是唯一的，用户改名时颜色保持不变，正好可以当身份用）。
    /// </summary>
    public static Dictionary<string, string> DetectRenames(Config.LabelingConfigModel before, Config.LabelingConfigModel after)
    {
        var afterNames = CollectNames(after);
        var beforeByColor = MapByColor(before);
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in MapByColor(after))
        {
            if (!beforeByColor.TryGetValue(item.Key, out var oldName)) { continue; }
            if (string.Equals(oldName, item.Value, StringComparison.OrdinalIgnoreCase)) { continue; }
            if (afterNames.Contains(oldName)) { continue; }   // 旧名还在：属于新增而不是改名
            renames[oldName] = item.Value;
        }

        // 兜底：用户改名时顺手把颜色也改了，颜色就对不上号了。此时若同一控件里
        // "旧名少了 1 个、新名多了 1 个"，一对一替换也足以判定是改名，避免误判成删除而丢掉标注。
        for (var index = 0; index < Math.Min(before.Controls.Count, after.Controls.Count); index++)
        {
            var oldNames = Names(before.Controls[index]);
            var newNames = Names(after.Controls[index]);
            var removedNames = oldNames.Where(name => !newNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
            var addedNames = newNames.Where(name => !oldNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
            if (removedNames.Count != 1 || addedNames.Count != 1) { continue; }
            if (afterNames.Contains(removedNames[0])) { continue; }   // 旧名在别处还在用
            if (renames.ContainsKey(removedNames[0])) { continue; }   // 颜色已经判定过
            renames[removedNames[0]] = addedNames[0];
        }
        return renames;
    }

    /// <summary>取某个控件下的标签名（含分类用的 Choices）。</summary>
    private static List<string> Names(Config.ControlTagInfo control)
        => control.Labels.Select(label => label.Value)
            .Concat(control.Choices.Select(choice => choice.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

    /// <summary>颜色 → 标签名（颜色缺失或重复时跳过，避免误判改名）。</summary>
    private static Dictionary<string, string> MapByColor(Config.LabelingConfigModel config)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in config.Controls)
        {
            foreach (var (name, color) in control.Labels.Select(label => (label.Value, label.Background))
                         .Concat(control.Choices.Select(choice => (choice.Value, choice.Background))))
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(color)) { continue; }
                if (!map.TryAdd(color.Trim(), name.Trim())) { ambiguous.Add(color.Trim()); }
            }
        }
        foreach (var color in ambiguous) { map.Remove(color); }
        return map;
    }

    /// <summary>
    /// 对一批标注任务执行同步：先按 <paramref name="renames"/> 改名，再把标签不在
    /// <paramref name="validNames"/> 里的 region 整条删除。
    /// </summary>
    /// <param name="tasks">工程里的标注任务（就地修改）。</param>
    /// <param name="renames">旧名 → 新名。</param>
    /// <param name="validNames">当前配置里仍然存在的标签名。</param>
    public static LabelSyncResult Apply(IEnumerable<Models.AnnotationTask> tasks, IReadOnlyDictionary<string, string> renames, IReadOnlySet<string> validNames)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var renamed = 0;
        var removed = 0;
        var affected = 0;
        foreach (var task in tasks)
        {
            var taskChanged = false;
            foreach (var annotation in task.Annotations)
            {
                if (annotation.WasCancelled == true || annotation.Result.Count == 0) { continue; }
                var kept = new List<Models.ResultRow>(annotation.Result.Count);
                foreach (var row in annotation.Result)
                {
                    if (row.Value is null || !LabelRowTypes.Contains(row.Type)) { kept.Add(row); continue; }
                    var labels = ValueAccess.GetStringList(row.Value, row.Type);
                    if (labels.Count == 0) { kept.Add(row); continue; }

                    var mapped = new List<string>(labels.Count);
                    foreach (var label in labels)
                    {
                        var name = renames.TryGetValue(label, out var target) ? target : label;
                        if (!validNames.Contains(name)) { continue; }                     // 标签已不存在 → 丢掉
                        if (!mapped.Contains(name, StringComparer.Ordinal)) { mapped.Add(name); }
                    }
                    if (mapped.Count == 0)
                    {
                        removed++;                                                        // 所有标签都没了 → 整条 region 删除
                        taskChanged = true;
                        continue;
                    }
                    if (!mapped.SequenceEqual(labels, StringComparer.Ordinal))
                    {
                        renamed++;
                        taskChanged = true;
                        ValueAccess.SetStringList(row.Value, row.Type, mapped);
                    }
                    kept.Add(row);
                }
                if (kept.Count != annotation.Result.Count)
                {
                    annotation.Result.Clear();
                    annotation.Result.AddRange(kept);
                    annotation.ResultCount = kept.Count;
                }
            }
            if (taskChanged) { affected++; }
        }
        return new LabelSyncResult(renamed, removed, affected);
    }
}
