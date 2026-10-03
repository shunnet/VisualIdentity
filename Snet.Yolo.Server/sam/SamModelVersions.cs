using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snet.Yolo.Server.sam;

/// <summary>不可变的已安装版本定位；旧版本保留供回退与已有图片编码使用。</summary>
/// <param name="Definition">兼容目录中的模型及实际锁定的仓库提交。</param>
/// <param name="Directory">已校验文件的独立目录或首次安装目录。</param>
public sealed record SamInstalledModel(SamModelDefinition Definition, string Directory);

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

public sealed partial class SamModelStore
{
    private sealed record VersionState(SamInstalledModel Active, SamInstalledModel? Previous);
    private sealed record SavedModel(SamModelDefinition Definition, bool Legacy);
    private sealed record SavedState(SavedModel Active, SavedModel? Previous);
    private readonly ConcurrentDictionary<SamModelKind, VersionState> _states = new();

    /// <summary>获取已部署版本；不会查询网络或自动更新。</summary>
    public SamInstalledModel GetInstalled(SamModelKind kind) => GetState(kind).Active;

    /// <summary>获取本地版本及回退状态。</summary>
    public SamUpdateStatus GetVersionStatus(SamModelKind kind)
    {
        var state = GetState(kind); var latest = Recommended(kind);
        return new(state.Active.Definition.Revision, latest.Revision, null,
            !SameWeights(state.Active.Definition, latest), state.Previous is not null, false, false);
    }

    /// <summary>查询上游，但仅将本程序验证目录内的权重作为候选更新。</summary>
    public async Task<SamUpdateStatus> CheckForUpdatesAsync(SamModelKind kind, CancellationToken cancellationToken = default)
    {
        var latest = await ResolveLatestAsync(kind, cancellationToken); var state = GetState(kind);
        return new(state.Active.Definition.Revision, latest.Definition.Revision, latest.Upstream,
            !SameWeights(state.Active.Definition, latest.Definition), state.Previous is not null, latest.Unverified, latest.Failed);
    }

    /// <summary>下载到独立版本目录，校验及推理接口验证成功后才原子切换；失败不替换旧版本。</summary>
    public async Task UpdateAsync(SamModelKind kind, Func<SamInstalledModel, CancellationToken, Task> validate,
        Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validate);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = GetState(kind); var latest = await ResolveLatestAsync(kind, cancellationToken);
            if (SameWeights(state.Active.Definition, latest.Definition)) { progress?.Invoke(100); return; }
            var candidate = CreateInstalled(latest.Definition, legacy: false);
            await EnsureModelFilesAsync(candidate, n => progress?.Invoke(n * 90 / 100), cancellationToken);
            progress?.Invoke(95);
            await validate(candidate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await CommitStateAsync(kind, new(candidate, state.Active), cancellationToken);
            _verified.Add(Path.Combine(candidate.Directory, candidate.Definition.Encoder)); progress?.Invoke(100);
        }
        finally { _gate.Release(); }
    }

    /// <summary>校验并恢复前一版本；旧目录不删除，可再次切回。</summary>
    public async Task RollbackAsync(SamModelKind kind, Func<SamInstalledModel, CancellationToken, Task> validate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validate);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = GetState(kind);
            var previous = state.Previous ?? throw new InvalidOperationException("SAM 没有可回退的版本。");
            await EnsureModelFilesAsync(previous, null, cancellationToken);
            await validate(previous, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            await CommitStateAsync(kind, new(previous, state.Active), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private SamModelDefinition Recommended(SamModelKind kind)
        => _catalog.LastOrDefault(m => m.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));
    private static bool SameWeights(SamModelDefinition a, SamModelDefinition b)
        => a.Kind == b.Kind && a.EncoderHash == b.EncoderHash && a.DecoderHash == b.DecoderHash && a.EncoderDataHash == b.EncoderDataHash && a.DecoderDataHash == b.DecoderDataHash;
    private string StatePath(SamModelKind kind) => Path.Combine(DirectoryPath, Recommended(kind).Folder, "active-model.json");
    private SamInstalledModel CreateInstalled(SamModelDefinition definition, bool legacy)
    {
        if (definition.Revision is null || !Regex.IsMatch(definition.Revision, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant) ||
            !_catalog.Any(known => known == (definition with { Revision = known.Revision })))
        { throw new InvalidDataException("SAM 版本不在当前程序的兼容目录中。"); }
        var directory = Path.Combine(DirectoryPath, definition.Folder);
        return new(definition, legacy ? directory : Path.Combine(directory, ".versions", definition.Revision));
    }

    private VersionState GetState(SamModelKind kind) => _states.GetOrAdd(kind, key =>
    {
        var path = StatePath(key);
        if (!File.Exists(path)) { return new(CreateInstalled(Recommended(key), legacy: true), null); }
        var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path)) ?? throw new InvalidDataException("SAM 版本记录无效。");
        if (saved.Active?.Definition is null || saved.Active.Definition.Kind != key || (saved.Previous is not null && (saved.Previous.Definition is null || saved.Previous.Definition.Kind != key))) { throw new InvalidDataException("SAM 版本记录与模型类型不匹配。"); }
        return new(CreateInstalled(saved.Active.Definition, saved.Active.Legacy),
            saved.Previous is null ? null : CreateInstalled(saved.Previous.Definition, saved.Previous.Legacy));
    });

    private async Task CommitStateAsync(SamModelKind kind, VersionState state, CancellationToken token)
    {
        var path = StatePath(kind); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavedModel Save(SamInstalledModel model) => new(model.Definition, model.Directory == Path.Combine(DirectoryPath, model.Definition.Folder));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await JsonSerializer.SerializeAsync(output, new SavedState(Save(state.Active), state.Previous is null ? null : Save(state.Previous)), cancellationToken: token); await output.FlushAsync(token); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
            _states[kind] = state;
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private async Task<(SamModelDefinition Definition, string? Upstream, bool Unverified, bool Failed)> ResolveLatestAsync(SamModelKind kind, CancellationToken token)
    {
        var recommended = Recommended(kind);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await _client.GetAsync($"https://huggingface.co/api/models/{recommended.Repository}?blobs=true", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var revision = json.RootElement.GetProperty("sha").GetString();
            if (revision is null || !Regex.IsMatch(revision, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)) { throw new JsonException("SAM 上游版本标识无效。"); }
            var files = json.RootElement.GetProperty("siblings").EnumerateArray().ToArray();
            bool Match(string name, string hash) => files.Any(f => f.GetProperty("rfilename").GetString() == name && f.TryGetProperty("lfs", out var lfs) && lfs.GetProperty("sha256").GetString() == hash);
            bool Compatible(SamModelDefinition m) => m.Archive is not null
                ? Match(m.Archive, m.ArchiveHash!) : Match(m.Encoder, m.EncoderHash) && Match(m.Decoder, m.DecoderHash)
                    && (m.EncoderData is null || Match(m.EncoderData, m.EncoderDataHash!))
                    && (m.DecoderData is null || Match(m.DecoderData, m.DecoderDataHash!));
            // 相同文件的新仓库提交可锁定新 SHA；不同权重必须先通过维护者的兼容验证。
            return Compatible(recommended) ? (recommended with { Revision = revision }, revision, false, false)
                : (recommended, revision, !_catalog.Any(m => m.Kind == kind && Compatible(m)), false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return (recommended, null, false, true); }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return (recommended, null, false, true); }
    }
}
