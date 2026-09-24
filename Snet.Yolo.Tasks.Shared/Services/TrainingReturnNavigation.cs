namespace Snet.Yolo.Tasks.Services;

/// <summary>在训练页之间传递站内入口，并限制返回地址为当前类型的项目页。</summary>
public static class TrainingReturnNavigation
{
    /// <summary>为训练路由附加当前页面的站内路径和查询参数。</summary>
    public static string WithSource(string trainPath, string currentUri)
    {
        var source = new Uri(currentUri, UriKind.Absolute).PathAndQuery;
        return trainPath + "?returnTo=" + Uri.EscapeDataString(source);
    }

    /// <summary>选择合法的来源页；直接打开训练链接或非法地址时返回相应项目列表。</summary>
    public static string Resolve(string? returnTo, string projectId, bool anomalib)
    {
        var list = anomalib ? "/anomalib-projects" : "/projects";
        var detail = (anomalib ? "/anomalib-project/" : "/project/") + Uri.EscapeDataString(projectId);
        if (string.IsNullOrWhiteSpace(returnTo) || returnTo.Contains('\\') || returnTo.Contains('#')) { return list; }
        var path = returnTo.Split('?', 2)[0];
        return path == list || path == detail ? returnTo : list;
    }
}
