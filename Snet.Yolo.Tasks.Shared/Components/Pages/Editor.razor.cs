
namespace Snet.Yolo.Tasks.Components.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;

/// <summary>
/// 标注编辑器页面代码后置：图像/文本/音频三种数据类型的标注逻辑、互操作回调、
/// 自动草稿(5s)、提交/跳过、区域与详情、快捷键映射。
/// </summary>
public partial class Editor : ComponentBase, IAsyncDisposable
{
    private string? _panelOpen;

    private void ToggleLeft() => TogglePanel("left");
    private void ToggleRight() => TogglePanel("right");
    private void TogglePanel(string side) => _panelOpen = _panelOpen == side ? null : side;

    private static readonly string[] TabKeys = { "Regions", "Details", "Settings" };
    private static readonly Dictionary<string, ControlTagKind> ToolKindMap = new(StringComparer.Ordinal)
    {
        ["rect"] = ControlTagKind.RectangleLabels,
        ["polygon"] = ControlTagKind.PolygonLabels,
        ["keypoint"] = ControlTagKind.KeyPointLabels,
        ["ellipse"] = ControlTagKind.EllipseLabels,
        ["brush"] = ControlTagKind.BrushLabels,
    };

    [Inject] private WorkspaceService Workspaces { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] public LanguageManager Language { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Parameter] public string ProjectId { get; set; } = string.Empty;
    [Parameter] public int TaskIndex { get; set; }

    /// <summary>当前任务下标（电路内切换用，初始来自路由参数）。</summary>
    private int _currentIndex;

    private WorkspaceProject? _project;
    private AnnotationTask? _currentTask;
    private LabelingSession? _session;
    private string? _imageUrl;
    private IJSObjectReference? _module;
    private IJSObjectReference? _audioModule;
    private string? _activeWaveCanvasId;
    private DotNetObjectReference<Editor>? _dotnetRef;
    private Timer? _saveTimer;
    private bool _navBusy;
    private readonly object _saveQueueLock = new();
    private Task _saveQueue = Task.CompletedTask;
    private Exception? _saveError;

    private string _activeTool = "select";
    private int _activeLabelIndex;
    private int _rightTab;
    private bool _loading = true;
    private string? _errorText;
    private string? _errorResourceKey;
    private string? _errorDetail;
    private double _detailX, _detailY, _detailW, _detailH, _detailRotation;
    private double _overlayOpacity = 0.25;
    private int _loadedIndex = -1;
    private string? _loadedProjectId;
    private bool _initBusy;

    // 文本模式
    private bool _textMode;
    private ControlTagInfo? _textControl;
    private string? _textValue;
    private List<(string Text, int Start, int End)> _tokens = new();
    private List<ResultRow> _textRows = new();
    private int? _anchorToken;
    private string? _selectedTextId;
    // 音频模式
    private bool _audioMode;
    private bool _classifyMode;
    private ControlTagInfo? _classifyControl;
    private string? _classifyValue;
    private ControlTagInfo? _audioLabelsControl;
    private ControlTagInfo? _audioTextControl;
    private string? _audioUrl;
    private double _segmentStart;
    private double _segmentEnd = 1;
    private string _audioTranscription = string.Empty;
    private List<ResultRow> _audioRows = new();
    private string WaveCanvasId => "ls-wave-" + ProjectId + "-" + _currentIndex;

    public LabelingSession? Session => _session;
    public string ActiveTool => _activeTool;
    public int RightTab { get => _rightTab; set => _rightTab = value; }
    public string CanvasId => "ls-canvas-" + ProjectId;
    public bool CanPrev => _currentIndex > 0;
    public bool CanNext => _project is not null && _currentIndex < _project.Tasks.Count - 1;
    public bool IsTextMode => _textMode;
    public bool IsAudioMode => _audioMode;
    public IReadOnlyList<LabelOptionInfo> TextLabels => (IReadOnlyList<LabelOptionInfo>?)_textControl?.Labels ?? Array.Empty<LabelOptionInfo>();
    public IReadOnlyList<(string Text, int Start, int End)> Tokens => _tokens;
    public IReadOnlyList<ResultRow> AudioRows => _audioRows;
    public IReadOnlyList<LabelOptionInfo> AudioLabels => (IReadOnlyList<LabelOptionInfo>?)_audioLabelsControl?.Labels ?? Array.Empty<LabelOptionInfo>();
    public string? AudioUrl => _audioUrl;
    public IReadOnlyList<LabelOptionInfo> RectangleLabels => (IReadOnlyList<LabelOptionInfo>?)_session?.RectangleControl?.Labels ?? Array.Empty<LabelOptionInfo>();
    public ControlTagKind ActiveToolKind => ToolKindMap.TryGetValue(_activeTool, out var kind) ? kind : ControlTagKind.RectangleLabels;
    public IReadOnlyList<LabelOptionInfo> ActiveLabels => _classifyMode && _classifyControl is not null
        ? (IReadOnlyList<LabelOptionInfo>)_classifyControl.Labels
        : (IReadOnlyList<LabelOptionInfo>?)_session?.GetControlLabels(ActiveToolKind) ?? Array.Empty<LabelOptionInfo>();
    public string? ClassifyValue => _classifyValue;
    public bool IsClassifyMode => _classifyMode;

    public ResultRow? SelectedRow
    {
        get
        {
            if (_session?.SelectedRegionId is null) { return null; }
            return _session.CurrentAnnotation.Result.FirstOrDefault(row => row.Id == _session.SelectedRegionId);
        }
    }

    private ResultRow? SelectedTextRow => _textRows.FirstOrDefault(row => row.Id == _selectedTextId);
    private string? _selectedAudioId;
    private ResultRow? SelectedAudioRow => _audioRows.FirstOrDefault(row => row.Id == _selectedAudioId);

    protected override void OnInitialized()
    {
        Language.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (_errorResourceKey is not null)
        {
            _errorText = Language.Translate(_errorResourceKey) + (_errorDetail ?? string.Empty);
        }
        _ = InvokeAsync(StateHasChanged);
    }

    private void SetLocalizedError(string resourceKey, string? detail = null)
    {
        _errorResourceKey = resourceKey;
        _errorDetail = detail;
        _errorText = Language.Translate(resourceKey) + (detail ?? string.Empty);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedProjectId == ProjectId && _loadedIndex == TaskIndex && _session is not null) { return; }
        _currentIndex = TaskIndex;
        await ResetForTaskChangeAsync();
        await LoadAsync();
        _loadedIndex = TaskIndex;
        _loadedProjectId = ProjectId;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_audioMode && _audioUrl is not null && _activeWaveCanvasId != WaveCanvasId)
        {
            try
            {
                _dotnetRef ??= DotNetObjectReference.Create(this);
                _audioModule ??= await Js.InvokeAsync<IJSObjectReference>("import", "./js/ls-audio.js");
                await _audioModule.InvokeVoidAsync("initAudioWave", WaveCanvasId, _audioUrl, _dotnetRef);
                _activeWaveCanvasId = WaveCanvasId;
            }
            catch { }
        }

        if (_session is not null && _errorText is null && _module is null && !_textMode && !_audioMode && !_initBusy)
        {
            _initBusy = true;
            try
            {
                _dotnetRef ??= DotNetObjectReference.Create(this);
                _module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/ls-canvas.js");
                await _module.InvokeVoidAsync("init", CanvasId, _imageUrl ?? string.Empty, _dotnetRef);
                await _module.InvokeVoidAsync("setMode", CanvasId, _activeTool);
                await PreloadAdjacentImagesAsync();
                _loading = false;
                _initBusy = false;
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception error)
            {
                _initBusy = false;
                SetLocalizedError("CanvasLoadFailed", " (" + error.Message + ")");
                _loading = false;
                Toast.ShowError(_errorText!);
            }
        }
    }

    private async Task LoadAsync()
    {
        if (_project is null) { _project = await Workspaces.GetProjectAsync(ProjectId); } // 电路内切换复用已加载工程，不重查 Server
        _overlayOpacity = _project?.OverlayOpacity ?? 0.25;
        var task = _project?.Tasks.ElementAtOrDefault(_currentIndex);
        _currentTask = task;
        if (_project is null || task is null)
        {
            SetLocalizedError("NoProjectsTitle");
            _loading = false;
            Toast.ShowError(_errorText!);
            return;
        }

        _session = new LabelingSession(_project.LabelConfigXml, task);
        if (!_session.Validation.IsValid)
        {
            var first = _session.Validation.Issues.FirstOrDefault(issue => issue.Severity == ConfigIssueSeverity.Error);
            SetLocalizedError("ConfigErrors", "：" + (first?.Message ?? "unknown"));
            _loading = false;
            Toast.ShowError(_errorText!);
            return;
        }

        _imageUrl = ResolveImageUrl(_session.Config, task);
        TryLoadClassifyMode(_session.Config, task);
        if (_imageUrl is null)
        {
            if (TryLoadTextMode(_session.Config, task)) { _loading = false; EnsureSaveTimer(); return; }
            if (TryLoadAudioMode(_session.Config, task)) { _loading = false; EnsureSaveTimer(); return; }
            SetLocalizedError("NoImageHint");
            _loading = false;
            Toast.ShowError(_errorText!);
            return;
        }

        EnsureSaveTimer();
    }

    private void EnsureSaveTimer()
        => _saveTimer ??= new Timer(_ => _ = AutoSaveTickAsync(), null, Timeout.Infinite, Timeout.Infinite);

    private async Task ResetForTaskChangeAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("destroy", CanvasId); }
            catch (JSDisconnectedException) { }
        }
        if (_audioModule is not null && _activeWaveCanvasId is not null)
        {
            try { await _audioModule.InvokeVoidAsync("destroyAudioWave", _activeWaveCanvasId); }
            catch (JSDisconnectedException) { }
            _activeWaveCanvasId = null;
        }
        _session = null;
        _imageUrl = null;
        _currentTask = null;
        _errorText = null;
        _errorResourceKey = null;
        _errorDetail = null;
        _loading = true;
        _textMode = false;
        _audioMode = false;
        _textValue = null;
        _textControl = null;
        _textRows.Clear();
        _tokens.Clear();
        _anchorToken = null;
        _selectedTextId = null;
        _audioLabelsControl = null;
        _audioTextControl = null;
        _audioUrl = null;
        _audioRows.Clear();
        _selectedAudioId = null;
        _rightTab = 0;
        _activeLabelIndex = 0;
        _detailX = 0; _detailY = 0; _detailW = 0; _detailH = 0; _detailRotation = 0;
        // 电路内切换：保留画布模块（避免整页重载感），只清状态；由 ReinitCanvasAsync 换图
        _initBusy = false;
        _dotnetRef?.Dispose();
        _dotnetRef = null;
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>切换后仅更新画布图像（复用已导入的 JS 模块，不整页重载）。</summary>
    private async Task ReinitCanvasAsync()
    {
        if (_module is not null && !_textMode && !_audioMode && _imageUrl is not null)
        {
            _dotnetRef ??= DotNetObjectReference.Create(this);
            try
            {
                await _module.InvokeVoidAsync("init", CanvasId, _imageUrl, _dotnetRef);
                await _module.InvokeVoidAsync("setMode", CanvasId, _activeTool);
                if (_session is not null) { await _module.InvokeVoidAsync("pushState", CanvasId, new { regions = _session.BuildRegionViews(), overlayOpacity = _overlayOpacity }); }
                await PreloadAdjacentImagesAsync();
            }
            catch { _module = null; }
        }
    }

    private async Task PreloadAdjacentImagesAsync()
    {
        if (_module is null || _project is null || _session is null) { return; }
        var urls = new List<string>(2);
        foreach (var index in new[] { _currentIndex - 1, _currentIndex + 1 })
        {
            if (index < 0 || index >= _project.Tasks.Count) { continue; }
            var url = ResolveImageUrl(_session.Config, _project.Tasks[index]);
            if (!string.IsNullOrWhiteSpace(url)) { urls.Add(url); }
        }
        if (urls.Count > 0) { await _module.InvokeVoidAsync("preloadImages", urls); }
    }

    private async Task NavigateTaskAsync(int delta)
    {
        if (_navBusy || _project is null || _project.Tasks.Count == 0) { return; }
        var newIndex = _currentIndex + delta;
        if (newIndex < 0 || newIndex >= _project.Tasks.Count) { return; }

        _navBusy = true;
        try
        {
            CaptureDraftSync();
            QueueCurrentTaskSave();

            _currentIndex = newIndex;
            await ResetForTaskChangeAsync();
            _loadedIndex = newIndex;
            await LoadAsync();
            await ReinitCanvasAsync();
        }
        finally { _navBusy = false; }
    }

    private static string? ResolveImageUrl(LabelingConfigModel config, AnnotationTask task)
    {
        var imageObject = config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Image);
        var valueField = imageObject?.ValueField;
        if (imageObject is null || string.IsNullOrWhiteSpace(valueField) || task.Data is null) { return null; }
        var fieldName = valueField.StartsWith("$", StringComparison.Ordinal) ? valueField[1..] : valueField;
        return task.Data[fieldName]?.ToString();
    }

    private static string ResolveField(ObjectTagInfo? obj, string fallback)
    {
        var valueField = obj?.ValueField;
        var field = valueField?.TrimStart('$') ?? fallback;
        return field;
    }

    private bool TryLoadClassifyMode(LabelingConfigModel config, AnnotationTask task)
    {
        var imageObject = config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Image);
        if (imageObject is null) { return false; }
        var control = config.Controls.FirstOrDefault(c => (c.Kind == ControlTagKind.Choices || c.Kind == ControlTagKind.Labels) && c.ToName == imageObject.Name);
        if (control is null) { return false; }
        _classifyControl = control;
        _classifyMode = true;
        var row = (_session?.CurrentAnnotation?.Result ?? new List<ResultRow>()).FirstOrDefault(r => r.Type is RegionType.Choices or RegionType.Labels);
        if (row?.Value is not null)
        {
            var field = control.Kind == ControlTagKind.Labels ? "labels" : "choices";
            _classifyValue = ValueAccess.GetStringList(row.Value, field).FirstOrDefault();
        }
        return true;
    }

    private async Task AssignClassifyAsync(string value)
    {
        if (!_classifyMode || _session is null || _classifyControl is null) { return; }
        var existing = _session.CurrentAnnotation.Result.FirstOrDefault(r => r.Type is RegionType.Choices or RegionType.Labels);
        if (existing is null)
        {
            existing = new ResultRow
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Type = _classifyControl.Kind == ControlTagKind.Labels ? RegionType.Labels : RegionType.Choices,
                FromName = _classifyControl.Name,
                ToName = _classifyControl.ToName,
                Origin = "manual",
                Value = new System.Text.Json.Nodes.JsonObject(),
            };
            _session.CurrentAnnotation.Result.Add(existing);
        }
        var field = _classifyControl.Kind == ControlTagKind.Labels ? "labels" : "choices";
        existing.Value![field] = new System.Text.Json.Nodes.JsonArray(value);
        _classifyValue = value;
        _saveTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        await AfterEditAsync();
    }

    private bool TryLoadTextMode(LabelingConfigModel config, AnnotationTask task)
    {
        var textObject = config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Text);
        if (textObject is null || task.Data is null) { return false; }
        var field = ResolveField(textObject, "text");
        _textValue = task.Data[field]?.ToString();
        if (string.IsNullOrEmpty(_textValue)) { return false; }
        _textControl = config.Controls.FirstOrDefault(control => control.Kind == ControlTagKind.Labels && control.ToName == textObject.Name);
        if (_textControl is null) { return false; }
        _tokens = TextSpanUtil.Tokenize(_textValue).ToList();
        _textRows = (_session?.CurrentAnnotation?.Result ?? new List<ResultRow>()).Where(row => row.Type == RegionType.Labels).ToList();
        _textMode = true;
        return true;
    }

    private bool TryLoadAudioMode(LabelingConfigModel config, AnnotationTask task)
    {
        var audioObject = config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Audio);
        if (audioObject is null || task.Data is null) { return false; }
        var field = ResolveField(audioObject, "audio");
        _audioUrl = task.Data[field]?.ToString();
        if (string.IsNullOrEmpty(_audioUrl)) { return false; }
        _audioLabelsControl = config.Controls.FirstOrDefault(control => control.Kind == ControlTagKind.Labels && control.ToName == audioObject.Name);
        _audioTextControl = config.Controls.FirstOrDefault(control => control.Kind == ControlTagKind.TextArea && control.ToName == audioObject.Name);
        _audioRows = (_session?.CurrentAnnotation?.Result ?? new List<ResultRow>()).Where(row => row.Type == RegionType.Labels || row.Type == RegionType.TextArea).ToList();
        var existingText = _audioRows.FirstOrDefault(row => row.Type == RegionType.TextArea);
        if (existingText?.Value?["text"]?.AsArray()?.FirstOrDefault()?.GetValue<string>() is { } t0) { _audioTranscription = t0; }
        _audioMode = true;
        return true;
    }

    private void OnTokenClick(int index, Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        if (!_textMode || index < 0 || index >= _tokens.Count) { return; }
        if (_anchorToken is null) { _anchorToken = index; _selectedTextId = null; }
        else if (!args.ShiftKey && index == _anchorToken.Value) { _anchorToken = null; }
        else
        {
            var first = Math.Min(_anchorToken.Value, index);
            var last = Math.Max(_anchorToken.Value, index);
            var (start, end, text) = TextSpanUtil.BuildSpan(_textValue!, first, last);
            _anchorToken = null;
            AddTextSpan(start, end, text);
        }
    }

    private void AddTextSpan(int start, int end, string text)
    {
        if (_textControl is null || start >= end) { return; }
        var label = _activeLabelIndex >= 0 && _activeLabelIndex < TextLabels.Count ? TextLabels[_activeLabelIndex].Value : null;
        var row = new ResultRow
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Type = RegionType.Labels,
            FromName = _textControl.Name,
            ToName = _textControl.ToName,
            Origin = "manual",
            Value = new System.Text.Json.Nodes.JsonObject { ["start"] = start, ["end"] = end, ["text"] = text },
        };
        if (label is not null) { row.Value!["labels"] = new System.Text.Json.Nodes.JsonArray(label); }
        _textRows.Add(row);
        _selectedTextId = row.Id;
        _saveTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        StateHasChanged();
    }

    private bool TokenHighlighted(int index)
    {
        if (index < 0 || index >= _tokens.Count) { return false; }
        var token = _tokens[index];
        return _textRows.Any(row => row.Value is not null && row.Value["start"] is not null && row.Value["end"] is not null
            && ToInt(row.Value["start"]) <= token.Start && token.End <= ToInt(row.Value["end"]));
    }

    private static int ToInt(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is not System.Text.Json.Nodes.JsonValue v) { return 0; }
        if (v.TryGetValue<int>(out var i)) { return i; }
        if (v.TryGetValue<double>(out var d)) { return (int)d; }
        if (v.TryGetValue<long>(out var l)) { return (int)l; }
        return 0;
    }

    private string TextSpanLabel(ResultRow row) => row.Value?["labels"]?.AsArray()?.FirstOrDefault()?.GetValue<string>() ?? string.Empty;
    private string TextSpanColor(ResultRow row) => LabelPalette.ResolveColor(TextLabels, string.IsNullOrEmpty(TextSpanLabel(row)) ? null : TextSpanLabel(row), Math.Max(0, _textRows.IndexOf(row)));
    private void SelectTextSpan(string? id) => _selectedTextId = id;
    private async Task DeleteTextSpan(string id)
    {
        await RequestDeleteAsync(() =>
        {
            _textRows.RemoveAll(row => row.Id == id);
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    private void ToggleTextLabel(string id, string value)
    {
        var row = _textRows.FirstOrDefault(r => r.Id == id);
        if (row?.Value is null) { return; }
        var labels = ValueAccess.GetStringList(row.Value, "labels");
        if (labels.Contains(value)) { labels.Remove(value); } else { labels.Add(value); }
        ValueAccess.SetStringList(row.Value, "labels", labels);
        StateHasChanged();
    }

    // 音频操作
    private void AddAudioSegment()
    {
        if (!_audioMode || _audioLabelsControl is null || _segmentEnd <= _segmentStart || _segmentStart < 0) { return; }
        var label = _activeLabelIndex >= 0 && _activeLabelIndex < AudioLabels.Count ? AudioLabels[_activeLabelIndex].Value : null;
        var row = new ResultRow
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Type = RegionType.Labels,
            FromName = _audioLabelsControl.Name,
            ToName = _audioLabelsControl.ToName,
            Origin = "manual",
            Value = new System.Text.Json.Nodes.JsonObject { ["start"] = _segmentStart, ["end"] = _segmentEnd },
        };
        if (label is not null) { row.Value!["labels"] = new System.Text.Json.Nodes.JsonArray(label); }
        _audioRows.Add(row);
        _selectedAudioId = row.Id;
        _saveTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        StateHasChanged();
    }

    private void OnTranscriptionChanged(Microsoft.AspNetCore.Components.ChangeEventArgs args)
    {
        _audioTranscription = args.Value?.ToString() ?? string.Empty;
        if (!_audioMode || _audioTextControl is null || string.IsNullOrEmpty(_audioTranscription)) { return; }
        var existing = _audioRows.FirstOrDefault(row => row.Type == RegionType.TextArea);
        if (existing is null)
        {
            existing = new ResultRow { Id = Guid.NewGuid().ToString("N")[..8], Type = RegionType.TextArea, FromName = _audioTextControl.Name, ToName = _audioTextControl.ToName, Origin = "manual", Value = new System.Text.Json.Nodes.JsonObject() };
            _audioRows.Add(existing);
        }
        existing.Value!["text"] = new System.Text.Json.Nodes.JsonArray(_audioTranscription);
        _saveTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        StateHasChanged();
    }

    private string AudioSegmentLabel(ResultRow row) => row.Value?["labels"]?.AsArray()?.FirstOrDefault()?.GetValue<string>() ?? string.Empty;
    private async Task DeleteAudioRow(string id)
    {
        await RequestDeleteAsync(() =>
        {
            _audioRows.RemoveAll(row => row.Id == id);
            if (_selectedAudioId == id) { _selectedAudioId = null; }
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    [JSInvokable]
    public void OnWaveSelect(double start, double end)
    {
        if (_audioMode)
        {
            _segmentStart = start;
            _segmentEnd = Math.Max(end, start + 0.05);
            InvokeAsync(StateHasChanged);
        }
    }

    // ── JS 回调 ──
    [JSInvokable]
    public async Task OnImageLoaded(double width, double height)
    {
        try
        {
            if (_session is null) { return; }
            _session.SetImageOriginalSize(width, height);
            _loading = false;
            await RefreshAndSyncAsync();
        }
        catch { _loading = false; }
    }

    [JSInvokable]
    public void OnImageError()
    {
        SetLocalizedError("CanvasLoadFailed");
        _loading = false;
        Toast.ShowError(_errorText!);
        InvokeAsync(StateHasChanged);
    }

    private bool EnsureLabels()
    {
        if (ActiveLabels.Count == 0)
        {
            Toast.ShowWarning(Language.Translate("AddLabelsFirst"));
            _ = InvokeAsync(StateHasChanged);
            return false;
        }
        return true;
    }
    [JSInvokable]
    public async Task OnRectDrawn(double x1, double y1, double x2, double y2)
    {
        if (!EnsureLabels()) { return; }
        if (_session is null || !_session.CanDraw(ControlTagKind.RectangleLabels)) { return; }
        var left = Math.Min(x1, x2); var top = Math.Min(y1, y2);
        var w = Math.Abs(x2 - x1); var h = Math.Abs(y2 - y1);
        if (w < 3 || h < 3) { return; }
        _session.AddRectangle(ClampRect(left, top, w, h), ActiveLabelValue());
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnPolygonFinished(double[] xs, double[] ys)
    {
        if (!EnsureLabels()) { return; }
        if (_session is null || !_session.CanDraw(ControlTagKind.PolygonLabels) || xs.Length < 3 || ys.Length < 3) { return; }
        _session.AddPolygon(xs, ys, ActiveLabelValue());
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnKeyPoint(double x, double y)
    {
        if (!EnsureLabels()) { return; }
        if (_session is null || !_session.CanDraw(ControlTagKind.KeyPointLabels)) { return; }
        _session.AddKeyPoint(x, y, ActiveLabelValue());
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnEllipseDrawn(double cx, double cy, double rx, double ry)
    {
        if (!EnsureLabels()) { return; }
        if (_session is null || !_session.CanDraw(ControlTagKind.EllipseLabels) || rx < 3 || ry < 3) { return; }
        _session.AddEllipse(cx, cy, rx, ry, ActiveLabelValue());
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnBrushStroke(double[] xs, double[] ys, double size)
    {
        if (!EnsureLabels()) { return; }
        if (_session is null || !_session.CanDraw(ControlTagKind.BrushLabels) || xs.Length < 2) { return; }
        _session.AddBrushMask(xs, ys, Math.Max(4, size), ActiveLabelValue());
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnShapeMoved(string regionId, double deltaX, double deltaY)
    {
        if (_session is null) { return; }
        _session.MoveShape(regionId, deltaX, deltaY);
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnRegionResized(string regionId, double x, double y, double width, double height)
    {
        if (_session is null) { return; }
        _session.ResizeShape(regionId, x, y, width, height);
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnPolygonVertexMoved(string regionId, int index, double x, double y)
    {
        if (_session is null) { return; }
        _session.MovePolygonVertex(regionId, index, x, y);
        await AfterEditAsync();
    }

    [JSInvokable]
    public async Task OnRegionClicked(string? regionId)
    {
        if (_session is null) { return; }
        _session.SelectedRegionId = regionId;
        SyncDetailFields();
        await RefreshAndSyncAsync();
    }

    [JSInvokable]
    public async Task OnKey(string action, bool ctrl, bool shift, bool alt)
    {
        if (_session is null && !_textMode && !_audioMode) { return; }
        if (action.StartsWith("tool:", StringComparison.Ordinal)) { await SetToolAsync(action[5..]); }
        else if (action.StartsWith("label:", StringComparison.Ordinal) && int.TryParse(action.AsSpan(6), out var digit)) { SetActiveLabel(digit - 1); }
        else
        {
            switch (action)
            {
                case "delete": await DeleteSelectedAsync(); break;
                case "escape": if (_activeTool != "select") { await SetToolAsync("select"); } else if (!_textMode && !_audioMode && _session?.SelectedRegionId is not null) { await OnRegionClicked(null); } break;
                case "undo": await UndoAsync(); break;
                case "redo": await RedoAsync(); break;
                case "prev": await NavigateTaskAsync(-1); break;
                case "next": await NavigateTaskAsync(1); break;
            }
        }
    }

    // ── 界面命令 ──
    private async Task SetToolAsync(string tool)
    {
        _activeTool = (tool is "rect" or "polygon" or "keypoint" or "ellipse" or "brush" or "pan" or "select")
            && (tool is "pan" or "select" || (_session?.CanDraw(ToolKindMap[tool]) == true)) ? tool : "select";
        if (_module is not null) { await _module.InvokeVoidAsync("setMode", CanvasId, _activeTool); }
        await InvokeAsync(StateHasChanged);
    }

    private Task SelectToolAsync() => SetToolAsync("select");
    private Task RectToolAsync() => SetToolAsync("rect");
    private Task PolygonToolAsync() => SetToolAsync("polygon");
    private Task KeyPointToolAsync() => SetToolAsync("keypoint");
    private Task EllipseToolAsync() => SetToolAsync("ellipse");
    private Task BrushToolAsync() => SetToolAsync("brush");
    private Task PanToolAsync() => SetToolAsync("pan");
    private Task ZoomOutAsync() => ViewportAsync("zoomOut");
    private Task ZoomInAsync() => ViewportAsync("zoomIn");
    private Task ActualSizeAsync() => ViewportAsync("actual");
    private Task FitAsync() => ViewportAsync("fit");

    private void OnOverlayOpacityChange(Microsoft.AspNetCore.Components.ChangeEventArgs args)
    {
        if (double.TryParse(args.Value?.ToString(), out var v)) { _overlayOpacity = Math.Clamp(v / 100d, 0.05, 1); }
        _ = RefreshAndSyncAsync();
    }

    private async Task OnOverlayOpacityChanged(Microsoft.AspNetCore.Components.ChangeEventArgs args)
    {
        if (_project is not null) { _project.OverlayOpacity = _overlayOpacity; await Workspaces.SaveProjectAsync(_project); }
        OnOverlayOpacityChange(args);
    }

    private async Task ViewportAsync(string action)
    {
        if (_module is not null) { await _module.InvokeVoidAsync("viewportAction", CanvasId, action); }
    }

    private void SetActiveLabel(int index)
    {
        if (index >= 0 && index < ActiveLabels.Count) { _activeLabelIndex = index; StateHasChanged(); }
    }
    private async Task SetActiveLabelAsync(int index) { SetActiveLabel(index); await Task.CompletedTask; }
    private string? ActiveLabelValue() => _activeLabelIndex >= 0 && _activeLabelIndex < ActiveLabels.Count ? ActiveLabels[_activeLabelIndex].Value : null;

    private async Task SelectFromListAsync(string regionId) => await OnRegionClicked(regionId);

    private async Task DeleteSelectedAsync()
    {
        if (!_textMode && !_audioMode && _session?.SelectedRegionId is not null)
        {
            var regionId = _session.SelectedRegionId;
            await RequestDeleteAsync(async () =>
            {
                _session.RemoveRegion(regionId);
                await AfterEditAsync();
            });
        }
    }

    private async Task DeleteRegionAsync(string regionId)
    {
        if (_session is null) { return; }
        await RequestDeleteAsync(async () =>
        {
            _session.RemoveRegion(regionId);
            await AfterEditAsync();
        });
    }

    private Func<Task>? _pendingDelete;

    /// <summary>打开与应用视觉一致的删除确认弹窗，并暂存确认后执行的操作。</summary>
    private Task RequestDeleteAsync(Func<Task> action)
    {
        _pendingDelete = action;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>执行用户刚刚确认的删除操作。</summary>
    private async Task ConfirmPendingDeleteAsync()
    {
        var action = _pendingDelete;
        _pendingDelete = null;
        if (action is not null) { await action(); }
    }

    /// <summary>取消当前删除请求，不修改任何标注数据。</summary>
    private void CancelPendingDelete() => _pendingDelete = null;

    private async Task ToggleLabelAsync(string regionId, string labelValue)
    {
        if (_session is null) { return; }
        var row = _session.CurrentAnnotation.Result.FirstOrDefault(region => region.Id == regionId);
        if (row?.Value is null) { return; }
        var labels = RowLabels(row);
        if (labels.Contains(labelValue)) { labels.Remove(labelValue); } else { labels.Add(labelValue); }
        _session.SetRegionLabels(regionId, labels);
        await AfterEditAsync();
    }

    private bool RowHasParent(ResultRow row) => !string.IsNullOrEmpty(row.ParentId);

    private async Task UndoAsync()
    {
        if (_session?.CanUndo == true) { _session.Undo(); SyncDetailFields(); await AfterEditAsync(); }
    }
    private async Task RedoAsync()
    {
        if (_session?.CanRedo == true) { _session.Redo(); SyncDetailFields(); await AfterEditAsync(); }
    }

    private async Task AutoSaveTickAsync()
    {
        try { await InvokeAsync(() => SaveDraftSilentlyAsync()); }
        catch { /* 静默 */ }
    }

    private void CaptureDraftSync()
    {
        if (_project is null || _session is null) { return; }
        var annotation = _session.CurrentAnnotation;
        if (_textMode) { annotation.Result.Clear(); annotation.Result.AddRange(_textRows.Select(CloneTextRow)); }
        else if (_audioMode) { annotation.Result.Clear(); annotation.Result.AddRange(_audioRows.Select(CloneTextRow)); }
        annotation.UpdatedAt = DateTime.UtcNow;
        if (!_project.Tasks[_currentIndex].Annotations.Contains(annotation)) { _project.Tasks[_currentIndex].Annotations.Add(annotation); }
    }

    private void QueueCurrentTaskSave()
    {
        if (_project is null || _currentIndex < 0 || _currentIndex >= _project.Tasks.Count) { return; }
        var taskIndex = _currentIndex;
        var task = _project.Tasks[taskIndex];
        var taskId = task.Id;
        var snapshot = Workspaces.CreateTaskSnapshot(task);
        lock (_saveQueueLock)
        {
            _saveQueue = PersistTaskAfterAsync(_saveQueue, taskIndex, taskId, snapshot);
        }
    }

    private async Task PersistTaskAfterAsync(Task previous, int taskIndex, long? taskId, string snapshot)
    {
        try { await previous; } catch { /* 后续保存仍应继续 */ }
        try
        {
            await Workspaces.SaveTaskSnapshotAsync(ProjectId, taskIndex, taskId, snapshot);
            _saveError = null;
        }
        catch (Exception error) { _saveError = error; }
    }

    private async Task FlushSaveQueueAsync(bool reportSuccess)
    {
        Task pending;
        lock (_saveQueueLock) { pending = _saveQueue; }
        await pending;
        if (_saveError is not null)
        {
            Toast.ShowError(Language.Translate("SaveFailed") + ": " + _saveError.Message);
            return;
        }
        if (reportSuccess) { Toast.Show(Language.Translate("SaveStatus") + " " + DateTime.Now.ToLongTimeString()); }
    }

    private async Task SaveDraftSilentlyAsync()
    {
        if (_project is null || _session is null) { return; }
        CaptureDraftSync();
        QueueCurrentTaskSave();
        await FlushSaveQueueAsync(reportSuccess: true);
        await InvokeAsync(StateHasChanged);
    }

    private async Task GoBackAsync()
    {
        if (_project is not null && _session is not null)
        {
            CaptureDraftSync();
            QueueCurrentTaskSave();
            await FlushSaveQueueAsync(reportSuccess: false);
        }
        Navigation.NavigateTo($"/project/{ProjectId}", forceLoad: false);
    }

    private async Task AfterEditAsync()
    {
        _saveTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        SyncDetailFields();
        await RefreshAndSyncAsync();
    }

    private async Task RefreshAndSyncAsync()
    {
        if (_module is not null && _session is not null && !_textMode && !_audioMode)
        {
            await _module.InvokeVoidAsync("pushState", CanvasId, new { regions = _session.BuildRegionViews(), overlayOpacity = _overlayOpacity });
        }
        await InvokeAsync(StateHasChanged);
    }


    private PixelRect ClampRect(double x, double y, double width, double height)
    {
        var maxWidth = Math.Max(1, _session?.OriginalWidth ?? width);
        var maxHeight = Math.Max(1, _session?.OriginalHeight ?? height);
        x = Math.Clamp(x, 0, Math.Max(0, maxWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, maxHeight - 1));
        width = Math.Min(width, maxWidth - x);
        height = Math.Min(height, maxHeight - y);
        return new PixelRect(x, y, Math.Max(0.5, width), Math.Max(0.5, height));
    }

    private void SyncDetailFields()
    {
        var row = SelectedRow;
        if (row?.Value is null) { return; }
        _detailX = ReadDouble(row.Value, "x");
        _detailY = ReadDouble(row.Value, "y");
        _detailW = ReadDouble(row.Value, "width");
        _detailH = ReadDouble(row.Value, "height");
        _detailRotation = ReadDouble(row.Value, "rotation");
    }

    private async Task ApplyDetailGeometryAsync()
    {
        if (_session?.SelectedRegionId is null) { return; }
        var width = Math.Max(0.5, _detailW); var height = Math.Max(0.5, _detailH);
        _session.UpdateRectangleGeometry(_session.SelectedRegionId, ClampRect(_detailX, _detailY, width, height));
        var row = _session.CurrentAnnotation.Result.FirstOrDefault(r => r.Id == _session.SelectedRegionId);
        if (row?.Value is not null) { row.Value["rotation"] = _detailRotation; }
        await AfterEditAsync();
    }

    private static double ParseDouble(ChangeEventArgs args) => double.TryParse(args.Value?.ToString(), out var value) ? value : 0;

    private static double ReadDouble(System.Text.Json.Nodes.JsonObject value, string name)
    {
        if (value[name] is not System.Text.Json.Nodes.JsonValue v) { return 0; }
        if (v.TryGetValue<int>(out var i)) { return i; }
        if (v.TryGetValue<double>(out var d)) { return d; }
        if (v.TryGetValue<long>(out var l)) { return l; }
        return 0;
    }

    private List<string> RowLabels(ResultRow row)
    {
        var field = (row.Type == RegionType.PolygonLabels) ? "polygonlabels"
            : (row.Type == RegionType.KeyPointLabels) ? "keypointlabels"
            : (row.Type == RegionType.EllipseLabels) ? "ellipselabels"
            : (row.Type == RegionType.Labels) ? "labels" : (row.Type == RegionType.BrushLabels) ? "brushlabels" : "rectanglelabels";
        return ValueAccess.GetStringList(row.Value!, field);
    }

    private bool RowHasLabel(ResultRow row, string label) => RowLabels(row).Contains(label);
    private string? RowLabelsText(ResultRow row)
    {
        var labels = RowLabels(row);
        return labels.Count == 0 ? null : string.Join(", ", labels);
    }

    private string RegionColor(ResultRow row)
    {
        var kind = row.Type switch
        {
            RegionType.PolygonLabels => ControlTagKind.PolygonLabels,
            RegionType.KeyPointLabels => ControlTagKind.KeyPointLabels,
            RegionType.EllipseLabels => ControlTagKind.EllipseLabels,
            RegionType.BrushLabels => ControlTagKind.BrushLabels,
            _ => ControlTagKind.RectangleLabels,
        };
        var options = (IReadOnlyList<LabelOptionInfo>?)_session?.GetControlLabels(kind) ?? Array.Empty<LabelOptionInfo>();
        var index = _session!.GeometryRows.TakeWhile(candidate => candidate.Id != row.Id).Count();
        return LabelPalette.ResolveColor(options, RowLabels(row).FirstOrDefault(), index);
    }

    public string TypeIcon(ResultRow row) => row.Type switch
    {
        RegionType.PolygonLabels => "bi-vector-pen",
        RegionType.KeyPointLabels => "bi-dot",
        RegionType.EllipseLabels => "bi-circle",
        _ => "bi-bounding-box",
    };

    private static ResultRow CloneTextRow(ResultRow row)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(row);
        return System.Text.Json.JsonSerializer.Deserialize<ResultRow>(json)!;
    }

    public async ValueTask DisposeAsync()
    {
        Language.LanguageChanged -= OnLanguageChanged;
        if (_saveTimer is not null) { await _saveTimer.DisposeAsync(); }
        try
        {
            CaptureDraftSync();
            QueueCurrentTaskSave();
            await FlushSaveQueueAsync(reportSuccess: false);
        }
        catch { /* 电路已关闭时无法再提示，但仍尽力完成队列 */ }
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("destroy", CanvasId);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { /* 浏览器已断开 */ }
            catch (ObjectDisposedException) { /* 电路已释放 */ }
        }
        if (_audioModule is not null)
        {
            try
            {
                if (_activeWaveCanvasId is not null) { await _audioModule.InvokeVoidAsync("destroyAudioWave", _activeWaveCanvasId); }
                await _audioModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
        _dotnetRef?.Dispose();
    }
}
