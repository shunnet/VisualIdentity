namespace Snet.Yolo.Tasks.Components.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Snet.Yolo.Server.sam;
using Snet.Yolo.Tasks.Core.Config;

public partial class Editor
{
    [Inject] private SamOnnxRuntime Sam { get; set; } = default!;
    [Inject] private Snet.Yolo.Tasks.Services.CurrentUserContext SamUser { get; set; } = default!;
    private bool _samPreferencesLoaded, _samPreferencesLoading;
    private string? _samPreferencesKey;
    private sealed class SamPreferences
    {
        public SamPreferences() { }
        public int Model { get; set; }
        public bool Enabled { get; set; }
        public int? GpuId { get; set; }
    }

    private async Task RestoreSamPreferencesAsync()
    {
        _samPreferencesLoading = true;
        try
        {
            _samPreferencesKey = "snet.sam.preferences.v1:" + Uri.EscapeDataString(await SamUser.GetRequiredUserNameAsync());
            var saved = await _module!.InvokeAsync<SamPreferences?>("loadSamPreferences", _samPreferencesKey);
            if (_samDisposed) { return; }
            if (saved is not null && Enum.IsDefined((SamModelKind)saved.Model))
            {
                _samModel = (SamModelKind)saved.Model;
                _samGpuId = Sam.SupportsCuda && _samGpus.Any(g => g.Index == saved.GpuId) ? saved.GpuId : null;
                _samPreferencesLoaded = true;
                if (saved.Enabled) { await ToggleSamAsync(new ChangeEventArgs { Value = true }); }
            }
        }
        catch (JSException) { /* 浏览器存储不可用时沿用默认配置。 */ }
        finally
        {
            _samPreferencesLoaded = true; _samPreferencesLoading = false;
            if (!_samDisposed) { await RefreshAndSyncAsync(); }
        }
    }

    private async Task SaveSamPreferencesAsync()
    {
        if (!_samPreferencesLoaded || _samDisposed || _module is null || _samPreferencesKey is null) { return; }
        try { await _module.InvokeVoidAsync("saveSamPreferences", _samPreferencesKey, new SamPreferences { Model = (int)_samModel, Enabled = _samEnabled, GpuId = _samGpuId }); }
        catch (JSException) { /* 存储不可用不阻断标注。 */ }
    }
    [Inject] private Snet.Yolo.Tasks.Services.SystemMetrics SamMetrics { get; set; } = default!;
    private IReadOnlyList<Snet.Yolo.Tasks.Services.GpuMetrics> _samGpus = [];
    private int? _samGpuId;
    private bool _samDetecting;
    private bool _samGpuProbed;

    private async Task RefreshSamGpusAsync()
    {
        if (_samDetecting || _samBusy) { return; }
        _samDetecting = true;
        try
        {
            _samGpus = (await SamMetrics.RefreshGpuAsync()).Gpus;
            if (_samGpuId is { } id && !_samGpus.Any(g => g.Index == id))
            { ResetSam(imageChanged: true); _samGpuId = null; await SaveSamPreferencesAsync(); }
        }
        finally { _samDetecting = false; if (!_samDisposed) { await RefreshAndSyncAsync(); } }
    }

    private async Task SelectSamDeviceAsync(int? gpuId)
    {
        if (_samBusy || _samGpuId == gpuId || (gpuId is { } id && (!Sam.SupportsCuda || !_samGpus.Any(g => g.Index == id)))) { return; }
        ResetSam(imageChanged: true); _samGpuId = gpuId;
        await SaveSamPreferencesAsync();
        await RefreshAndSyncAsync();
    }
    private bool _samEnabled, _samBusy, _samDisposed;
    private long _samBusyNoticeAt;
    /// <summary>运算期间对重复操作给出提示；节流避免连续点击堆积提示。</summary>
    [JSInvokable]
    public void OnSamBusy()
    {
        if (_samDisposed) { return; }
        var now = Environment.TickCount64;
        if (_samBusyNoticeAt != 0 && now - _samBusyNoticeAt < 1500) { return; }
        _samBusyNoticeAt = now; Toast.ShowWarning(Language.Translate("SamBusyHint"));
    }
    private int _samVersion;
    private CancellationTokenSource? _samOperation;
    private SamImageContext? _samImage;
    private SamResult? _samPreview;
    private readonly List<SamPrompt> _samPrompts = [];
    private string? _samStatus;
    private SamModelKind _samModel = SamModelKind.MobileSam;
    private SamUpdateStatus? _samUpdateInfo;
    private string SamRevision => _samUpdateInfo is { } info ? info.InstalledRevision[..12] : Language.Translate("SamVersionPending");
    private string SamResourceKey => _samModel switch { SamModelKind.Sam21Tiny => "Sam21Resource", SamModelKind.SamVitB => "SamVitBResource", SamModelKind.SamVitL => "SamVitLResource", SamModelKind.SamVitH => "SamVitHResource", _ => "SamMobileResource" };

    private async Task ChangeSamModelAsync(ChangeEventArgs args)
    {
        if (!Enum.TryParse<SamModelKind>(args.Value?.ToString(), out var kind) || !Enum.IsDefined(kind) || kind == _samModel) { return; }
        ResetSam(imageChanged: true); _samModel = kind; _samUpdateInfo = null;
        await SaveSamPreferencesAsync();
        if (_samEnabled) { await ToggleSamAsync(new ChangeEventArgs { Value = true }); }
        else { await RefreshAndSyncAsync(); }
    }
    private bool SamAvailable => !_textMode && !_audioMode && _session is not null &&
        (_session.CanDraw(ControlTagKind.RectangleLabels) || _session.CanDraw(ControlTagKind.PolygonLabels) || _session.CanDraw(ControlTagKind.BrushLabels));

    private void ResetSam(bool imageChanged = false)
    {
        _samVersion++; _samOperation?.Cancel(); _samOperation = null;
        _samBusy = false; _samPreview = null; _samPrompts.Clear(); _samStatus = null;
        if (imageChanged) { _samImage = null; }
    }

    private async Task ToggleSamAsync(ChangeEventArgs args)
    {
        ResetSam(); _samEnabled = args.Value is true;
        await SaveSamPreferencesAsync();
        if (!_samEnabled) { await RefreshAndSyncAsync(); return; }
        using var operation = new CancellationTokenSource(); _samOperation = operation;
        var version = _samVersion; _samBusy = true; _samStatus = Language.Translate("SamPreparing") + " 0%";
        await RefreshAndSyncAsync();
        try
        {
            await Sam.PrepareAsync(_samModel, progress => _ = InvokeAsync(() =>
            {
                if (_samDisposed || version != _samVersion) { return; }
                _samStatus = Language.Translate("SamPreparing") + " " + progress + "%"; StateHasChanged();
            }), operation.Token);
            if (version == _samVersion) { _samStatus = Language.Translate("SamReady"); _samUpdateInfo = Sam.GetVersionStatus(_samModel); }
        }
        catch (Exception error) when (operation.IsCancellationRequested) { _ = error; }
        catch (Exception error) { if (version == _samVersion) { _samEnabled = false; _samStatus = Language.Translate("SamFailed") + " " + error.GetBaseException().Message; } }
        finally
        {
            if (version == _samVersion && !_samDisposed) { _samBusy = false; _samOperation = null; await RefreshAndSyncAsync(); }
        }
    }

    /// <summary>检查不改变预览；更新/回退清空编码。所有动作可取消，失败由服务端保留原版本。</summary>
    private async Task RunSamVersionActionAsync(string action)
    {
        if (_samBusy || _samDisposed || !_samEnabled) { return; }
        if (action != "check") { ResetSam(imageChanged: true); }
        using var operation = new CancellationTokenSource(); _samOperation = operation;
        var version = _samVersion; _samBusy = true; _samStatus = Language.Translate("SamVersionWorking");
        await RefreshAndSyncAsync();
        try
        {
            if (action == "check")
            {
                var info = await Sam.CheckForUpdatesAsync(_samModel, operation.Token);
                if (version != _samVersion || _samDisposed) { return; }
                _samUpdateInfo = info;
                _samStatus = Language.Translate(info.CheckFailed ? "SamUpdateCheckFailed" : info.UnverifiedUpstream ? "SamUnverifiedUpdate" : info.CanUpdate ? "SamUpdateAvailable" : "SamUpToDate");
            }
            else
            {
                if (action == "update")
                {
                    await Sam.UpdateAsync(_samModel, _samGpuId, progress => _ = InvokeAsync(() =>
                    {
                        if (_samDisposed || version != _samVersion) { return; }
                        _samStatus = Language.Translate("SamVersionWorking") + " " + progress + "%"; StateHasChanged();
                    }), operation.Token);
                }
                else { await Sam.RollbackAsync(_samModel, _samGpuId, operation.Token); }
                if (version != _samVersion || _samDisposed) { return; }
                _samUpdateInfo = Sam.GetVersionStatus(_samModel); _samStatus = Language.Translate("SamVersionChanged");
            }
        }
        catch (Exception error) when (operation.IsCancellationRequested) { _ = error; }
        catch (Exception error) { if (version == _samVersion && !_samDisposed) { _samStatus = Language.Translate("SamFailed") + " " + error.GetBaseException().Message; } }
        finally
        {
            if (version == _samVersion && !_samDisposed) { _samBusy = false; _samOperation = null; await RefreshAndSyncAsync(); }
        }
    }

    /// <summary>画布点选提示；只读取当前用户、当前项目上传目录中的图片。</summary>
    [JSInvokable]
    public async Task OnSamPoint(double x, double y, bool include)
    {
        if (_samBusy) { OnSamBusy(); return; }
        if (!_samEnabled || _samBusy || _samDisposed || _session is null || _imageUrl is null || _activeTool is not ("rect" or "polygon" or "brush") || !EnsureLabels()) { return; }
        if (_samPrompts.Count >= 64 || (_samPrompts.Count == 0 && !include)) { _samStatus = Language.Translate("SamHint"); await InvokeAsync(StateHasChanged); return; }
        if (x < 0 || y < 0 || !double.IsFinite(x) || !double.IsFinite(y) || x >= _session.OriginalWidth || y >= _session.OriginalHeight) { return; }
        using var operation = new CancellationTokenSource(); _samOperation = operation;
        var version = _samVersion; var session = _session; _samBusy = true; _samStatus = Language.Translate("SamProcessing");
        _samPrompts.Add(new(x, y, include));
        await RefreshAndSyncAsync();
        try
        {
            if (_samImage is null)
            {
                var location = await Workspaces.GetProjectUploadLocationAsync(ProjectId);
                if (!_imageUrl.StartsWith(location.UrlPrefix, StringComparison.Ordinal)) { throw new InvalidDataException("SAM 图片不在当前项目目录中。"); }
                var name = Uri.UnescapeDataString(_imageUrl[location.UrlPrefix.Length..]);
                if (name.Length == 0 || name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\') || name is "." or "..") { throw new InvalidDataException("SAM 图片路径无效。"); }
                var image = await Sam.EncodeAsync(Path.Combine(location.Directory, name), _samModel, _samGpuId, operation.Token);
                if (version != _samVersion) { return; }
                // 禁止画布尺寸与推理尺寸不一致（例如 EXIF 旋转或旧任务元数据）。
                if (image.Width != session.OriginalWidth || image.Height != session.OriginalHeight) { throw new InvalidDataException("SAM 图片尺寸与画布不一致，请重新加载图片。"); }
                _samImage = image;
            }
            var result = await Sam.SegmentAsync(_samImage, _samPrompts, operation.Token);
            if (version != _samVersion || !ReferenceEquals(session, _session)) { return; }
            _samPreview = result; _samStatus = Language.Translate("SamReady");
        }
        catch (Exception error) when (operation.IsCancellationRequested) { _ = error; }
        catch (Exception error)
        {
            if (version == _samVersion) { _samPrompts.RemoveAt(_samPrompts.Count - 1); _samStatus = Language.Translate("SamFailed") + " " + error.GetBaseException().Message; }
        }
        finally
        {
            if (version == _samVersion && !_samDisposed) { _samBusy = false; _samOperation = null; await RefreshAndSyncAsync(); }
        }
    }

    private async Task ConfirmSamAsync()
    {
        if (_samBusy || _samPreview is not { } result || _session is null || !EnsureLabels()) { return; }
        var label = ActiveLabelValue();
        if (_activeTool == "rect") { _session.AddRectangle(new(result.X, result.Y, result.BoxWidth, result.BoxHeight), label); }
        else if (_activeTool == "polygon") { _session.AddPolygon(result.PointsX, result.PointsY, label); }
        else if (_activeTool == "brush") { _session.AddFilledBrushMask(result.Mask, result.PointsX, result.PointsY, result.PreviewDataUrl, label); }
        else { return; }
        ResetSam(); await AfterEditAsync();
    }

    private async Task CancelSamAsync() { ResetSam(); await RefreshAndSyncAsync(); }
    private object? SamCanvasPreview => _samPreview is not { } r ? null : new { dataUrl = r.PreviewDataUrl, tool = _activeTool, x = r.X, y = r.Y, width = r.BoxWidth, height = r.BoxHeight, pointsX = r.PointsX, pointsY = r.PointsY };
}
