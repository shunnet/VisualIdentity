using System.IO.Compression;
using System.Security.Cryptography;

namespace Snet.Yolo.Server.sam;

/// <summary>程序根目录 sam 下的固定版本 ONNX 模型；不执行下载的代码。</summary>
public sealed partial class SamModelStore
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _verified = [];
    private readonly IReadOnlyList<SamModelDefinition> _catalog;
    private readonly HttpClient _client;
    /// <summary>模型共享目录，默认是程序根目录下的 sam。</summary>
    public string DirectoryPath { get; }
    /// <summary>图像编码器路径。</summary>
    public string EncoderPath => GetEncoderPath(SamModelKind.MobileSam);
    /// <summary>掩膜解码器路径。</summary>
    public string DecoderPath => GetDecoderPath(SamModelKind.MobileSam);
    /// <summary>指定程序根目录；同一服务器的用户共用权重。</summary>
    public SamModelStore(string? applicationRoot = null) : this(applicationRoot, SamModels.CompatibleVersions, Client) { }
    internal SamModelStore(string? applicationRoot, IReadOnlyList<SamModelDefinition> catalog, HttpClient client)
    { DirectoryPath = Path.Combine(Path.GetFullPath(applicationRoot ?? AppContext.BaseDirectory), "sam"); _catalog = catalog; _client = client; }

    /// <summary>所选模型的编码器路径。</summary>
    public string GetEncoderPath(SamModelKind kind) { var installed = GetInstalled(kind); return Path.Combine(installed.Directory, installed.Definition.Encoder); }
    /// <summary>所选模型的解码器路径。</summary>
    public string GetDecoderPath(SamModelKind kind) { var installed = GetInstalled(kind); return Path.Combine(installed.Directory, installed.Definition.Decoder); }

    /// <summary>首次启用时下载并校验，临时文件通过 SHA-256 验证后才发布。</summary>
    public async Task EnsureReadyAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
        => await EnsureReadyAsync(SamModelKind.MobileSam, progress, cancellationToken);

    /// <summary>只下载所选模型；校验压缩包、ONNX 及可能存在的外部权重文件。</summary>
    public async Task EnsureReadyAsync(SamModelKind kind, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = GetState(kind);
            var installed = state.Active;
            var encoder = Path.Combine(installed.Directory, installed.Definition.Encoder);
            if (_verified.Contains(encoder)) { progress?.Invoke(100); return; }
            if (!File.Exists(StatePath(kind)) && !File.Exists(encoder) &&
                !File.Exists(Path.Combine(installed.Directory, installed.Definition.Decoder)) &&
                (installed.Definition.Archive is null || !File.Exists(Path.Combine(installed.Directory, installed.Definition.Archive))))
            {
                var latest = await ResolveLatestAsync(kind, cancellationToken);
                installed = CreateInstalled(latest.Definition, legacy: true);
            }
            await EnsureModelFilesAsync(installed, progress, cancellationToken);
            await CommitStateAsync(kind, new(installed, state.Previous), cancellationToken);
            _verified.Add(Path.Combine(installed.Directory, installed.Definition.Encoder));
            progress?.Invoke(100);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureModelFilesAsync(SamInstalledModel installed, Action<int>? progress, CancellationToken cancellationToken)
    {
        var model = installed.Definition;
        var encoder = Path.Combine(installed.Directory, model.Encoder); var decoder = Path.Combine(installed.Directory, model.Decoder);
        var encoderData = model.EncoderData is null ? null : Path.Combine(installed.Directory, model.EncoderData);
        var decoderData = model.DecoderData is null ? null : Path.Combine(installed.Directory, model.DecoderData);
        Directory.CreateDirectory(Path.GetDirectoryName(encoder)!);
        Directory.CreateDirectory(Path.GetDirectoryName(decoder)!);
        var baseUrl = $"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/";
        if (model.Archive is null)
        {
            // 外部权重先就绪，最后发布引用它的图；四个文件都验证成功才提交版本记录。
            var files = new List<(string Name, long Size, string Hash)>();
            if (model.EncoderData is not null) { files.Add((model.EncoderData, model.EncoderDataBytes, model.EncoderDataHash!)); }
            if (model.DecoderData is not null) { files.Add((model.DecoderData, model.DecoderDataBytes, model.DecoderDataHash!)); }
            files.Add((model.Encoder, model.EncoderBytes, model.EncoderHash));
            files.Add((model.Decoder, model.DecoderBytes, model.DecoderHash));
            var total = files.Sum(f => f.Size); long complete = 0;
            foreach (var file in files)
            {
                var path = Path.Combine(installed.Directory, file.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await EnsureFileAsync(path, baseUrl + file.Name, file.Size, file.Hash,
                    n => progress?.Invoke((int)((complete + file.Size * n / 100) * 100 / total)), cancellationToken);
                complete += file.Size;
            }
        }
        else
        {
            await CheckExistingAsync(encoder, model.EncoderBytes, model.EncoderHash, cancellationToken);
            await CheckExistingAsync(decoder, model.DecoderBytes, model.DecoderHash, cancellationToken);
            if (encoderData is not null) { await CheckExistingAsync(encoderData, model.EncoderDataBytes, model.EncoderDataHash!, cancellationToken); }
            if (decoderData is not null) { await CheckExistingAsync(decoderData, model.DecoderDataBytes, model.DecoderDataHash!, cancellationToken); }
            if (!File.Exists(encoder) || !File.Exists(decoder) || (encoderData is not null && !File.Exists(encoderData)) || (decoderData is not null && !File.Exists(decoderData)))
            {
                var archive = Path.Combine(installed.Directory, model.Archive);
                var suppliedArchive = File.Exists(archive);
                await EnsureFileAsync(archive, baseUrl + model.Archive, model.ArchiveBytes, model.ArchiveHash!, n => progress?.Invoke(n * 90 / 100), cancellationToken);
                using (var zip = ZipFile.OpenRead(archive))
                {
                    // 大模型先发布完整的外部权重，最后发布引用它的 ONNX；每个文件均独立校验。
                    if (encoderData is not null) { await ExtractAsync(zip, encoderData, model.EncoderDataBytes, model.EncoderDataHash!, cancellationToken); }
                    if (decoderData is not null) { await ExtractAsync(zip, decoderData, model.DecoderDataBytes, model.DecoderDataHash!, cancellationToken); }
                    await ExtractAsync(zip, encoder, model.EncoderBytes, model.EncoderHash, cancellationToken);
                    progress?.Invoke(95);
                    await ExtractAsync(zip, decoder, model.DecoderBytes, model.DecoderHash, cancellationToken);
                }
                if (!suppliedArchive) { File.Delete(archive); } // 仅清理本次下载且已成功解压的包，保留用户离线提供的文件。
            }
        }
    }

    private static async Task CheckExistingAsync(string path, long size, string digest, CancellationToken token)
    {
        if (File.Exists(path) && !await ValidAsync(path, size, digest, token)) { throw new InvalidDataException($"SAM 模型校验失败：{Path.GetFileName(path)}。原文件未修改，请移走不匹配文件后重试。"); }
    }

    private static async Task ExtractAsync(ZipArchive zip, string path, long size, string digest, CancellationToken token)
    {
        if (File.Exists(path)) { return; }
        // 只读目录定义的固定名称，不使用条目路径作为输出路径，不执行 config.yaml。
        var entry = zip.GetEntry(Path.GetFileName(path)) ?? throw new InvalidDataException("SAM 权重包缺少 ONNX 文件。");
        if (entry.Length != size) { throw new InvalidDataException("SAM 解压文件大小不匹配。"); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = entry.Open())
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) { await input.CopyToAsync(output, token); }
            if (!await ValidAsync(temporary, size, digest, token)) { throw new InvalidDataException("SAM 解压文件 SHA-256 校验失败。"); }
            File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static async Task<bool> ValidAsync(string path, long size, string digest, CancellationToken token)
    {
        if (new FileInfo(path).Length != size) { return false; }
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }
}
