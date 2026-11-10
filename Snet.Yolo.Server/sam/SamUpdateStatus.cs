using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snet.Yolo.Server.sam;

/// <summary>版本检查结果；上游未验证的新权重不会被当成可安装更新。</summary>
/// <param name="InstalledRevision">当前已安装的实际提交。</param>
/// <param name="RecommendedRevision">最新兼容候选的提交。</param>
/// <param name="UpstreamRevision">本次查询的上游提交；查询失败或未查询时为 null。</param>
/// <param name="CanUpdate">兼容候选与当前权重不同。</param>
/// <param name="CanRollback">存在上一版本。</param>
/// <param name="UnverifiedUpstream">上游权重未进入兼容目录，不可直接安装。</param>
/// <param name="CheckFailed">无法完成上游检查，采用程序内推荐版本。</param>
public sealed record SamUpdateStatus(string InstalledRevision, string RecommendedRevision, string? UpstreamRevision,
    bool CanUpdate, bool CanRollback, bool UnverifiedUpstream, bool CheckFailed);
