namespace Snet.Yolo.Tasks.Services;

/// <summary>单条验证检测结果的进程内快照。</summary>
public sealed record ValidationDetection(string Name, string Confidence, string Position);

/// <summary>视频中单个采样帧的时间点及完整识别结果。</summary>
public sealed record ValidationVideoFrame(double TimeSeconds, string ResultJson);

/// <summary>与指定模型关联的验证文件及其最后一次识别结果。</summary>
public sealed record ValidationImageState(
    Guid Id,
    string Name,
    string Url,
    string? ResultJson,
    IReadOnlyList<ValidationDetection> Detections,
    bool IsVideo = false,
    string ContentType = "",
    IReadOnlyList<ValidationVideoFrame>? VideoFrames = null,
    string? ResultUrl = null);

/// <summary>指定模型的验证图片列表和当前选中图片。</summary>
public sealed record ValidationModelState(
    IReadOnlyList<ValidationImageState> Images,
    Guid? SelectedImageId);

/// <summary>
/// 在当前应用进程内保存各登录用户、各模型的验证图片状态。
/// 页面刷新会继续使用同一实例；应用进程重启后状态自然清空。
/// </summary>
public sealed class ValidationState
{
    private const int MaxFilesPerModel = 500;
    private readonly object _gate = new();
    private readonly Dictionary<string, UserState> _users = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>记录用户最后选中的模型。</summary>
    public void SelectModel(string userName, int modelIndex)
    {
        lock (_gate) { GetOrCreateUser(userName).SelectedModelIndex = modelIndex; }
    }

    /// <summary>获取用户最后选中的模型；进程内尚无记录时返回空。</summary>
    public int? GetSelectedModel(string userName)
    {
        lock (_gate) { return GetOrCreateUser(userName).SelectedModelIndex; }
    }

    /// <summary>把图片或视频加入指定模型的队列，并将其设为当前文件。</summary>
    public ValidationImageState AddImage(string userName, int modelIndex, string name, string url, bool isVideo = false, string contentType = "")
    {
        lock (_gate)
        {
            var model = GetOrCreateModel(userName, modelIndex);
            if (model.Images.Count >= MaxFilesPerModel)
            {
                throw new InvalidOperationException($"每个模型最多保留 {MaxFilesPerModel} 个验证文件，请先删除不需要的文件。");
            }
            var image = new MutableImage(Guid.NewGuid(), name, url, isVideo, contentType);
            model.Images.Add(image);
            model.SelectedImageId = image.Id;
            return Snapshot(image);
        }
    }

    /// <summary>将指定图片设为其模型的当前图片。</summary>
    public bool SelectImage(string userName, int modelIndex, Guid imageId)
    {
        lock (_gate)
        {
            var model = GetOrCreateModel(userName, modelIndex);
            if (model.Images.All(image => image.Id != imageId)) { return false; }
            model.SelectedImageId = imageId;
            return true;
        }
    }

    /// <summary>从指定模型的验证文件队列中删除文件并修正当前选中项。</summary>
    public bool RemoveImage(string userName, int modelIndex, Guid imageId)
    {
        lock (_gate)
        {
            var model = GetOrCreateModel(userName, modelIndex);
            var removed = model.Images.RemoveAll(image => image.Id == imageId) > 0;
            if (model.SelectedImageId == imageId) { model.SelectedImageId = model.Images.LastOrDefault()?.Id; }
            return removed;
        }
    }

    /// <summary>获取指定模型的图片列表及当前选择的不可变快照。</summary>
    public ValidationModelState GetModel(string userName, int modelIndex)
    {
        lock (_gate)
        {
            var model = GetOrCreateModel(userName, modelIndex);
            return new ValidationModelState(model.Images.Select(Snapshot).ToArray(), model.SelectedImageId);
        }
    }

    /// <summary>保存指定图片最后一次识别产生的 JSON 和检测摘要。</summary>
    public void SetResult(
        string userName,
        int modelIndex,
        Guid imageId,
        string? resultJson,
        IEnumerable<ValidationDetection> detections)
    {
        lock (_gate)
        {
            var image = GetOrCreateModel(userName, modelIndex).Images.FirstOrDefault(item => item.Id == imageId);
            if (image is null) { return; }
            image.ResultJson = resultJson;
            image.Detections = detections.ToArray();
        }
    }

    /// <summary>保存视频的汇总检测结果和按时间排列的逐帧结果。</summary>
    public void SetVideoResult(
        string userName,
        int modelIndex,
        Guid imageId,
        string? resultJson,
        IEnumerable<ValidationDetection> detections,
        string resultUrl)
    {
        lock (_gate)
        {
            var image = GetOrCreateModel(userName, modelIndex).Images.FirstOrDefault(item => item.Id == imageId);
            if (image is null) { return; }
            image.ResultJson = resultJson;
            image.Detections = detections.ToArray();
            image.VideoFrames = Array.Empty<ValidationVideoFrame>();
            image.ResultUrl = resultUrl;
        }
    }

    /// <summary>删除所有用户下指定模型的进程内状态，并返回需要清理的临时图片地址。</summary>
    public IReadOnlyList<string> RemoveModel(string userName, int modelIndex)
    {
        lock (_gate)
        {
            var urls = new List<string>();
            var user = GetOrCreateUser(userName);
            if (user.Models.Remove(modelIndex, out var model))
            {
                urls.AddRange(model.Images.SelectMany(image => new[] { image.Url, image.ResultUrl }).OfType<string>());
            }
            if (user.SelectedModelIndex == modelIndex) { user.SelectedModelIndex = null; }
            return urls;
        }
    }

    /// <summary>获取用户状态；调用方已持有同步锁。</summary>
    private UserState GetOrCreateUser(string userName)
    {
        var key = string.IsNullOrWhiteSpace(userName) ? "anonymous" : userName;
        if (!_users.TryGetValue(key, out var user))
        {
            user = new UserState();
            _users.Add(key, user);
        }
        return user;
    }

    /// <summary>获取用户下与模型一一对应的状态；调用方已持有同步锁。</summary>
    private ModelState GetOrCreateModel(string userName, int modelIndex)
    {
        var user = GetOrCreateUser(userName);
        if (!user.Models.TryGetValue(modelIndex, out var model))
        {
            model = new ModelState();
            user.Models.Add(modelIndex, model);
        }
        return model;
    }

    /// <summary>复制可变图片状态，避免组件在锁外修改共享数据。</summary>
    private static ValidationImageState Snapshot(MutableImage image)
        => new(image.Id, image.Name, image.Url, image.ResultJson, image.Detections.ToArray(), image.IsVideo, image.ContentType, image.VideoFrames.ToArray(), image.ResultUrl);

    private sealed class UserState
    {
        public int? SelectedModelIndex { get; set; }
        public Dictionary<int, ModelState> Models { get; } = new();
    }

    private sealed class ModelState
    {
        public List<MutableImage> Images { get; } = new();
        public Guid? SelectedImageId { get; set; }
    }

    private sealed class MutableImage(Guid id, string name, string url, bool isVideo, string contentType)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public string Url { get; } = url;
        public bool IsVideo { get; } = isVideo;
        public string ContentType { get; } = contentType;
        public string? ResultJson { get; set; }
        public IReadOnlyList<ValidationDetection> Detections { get; set; } = Array.Empty<ValidationDetection>();
        public IReadOnlyList<ValidationVideoFrame> VideoFrames { get; set; } = Array.Empty<ValidationVideoFrame>();
        public string? ResultUrl { get; set; }
    }
}
