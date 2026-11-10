namespace Snet.Yolo.Server.sam;

/// <summary>固定权重及其完整性信息；不接受用户输入的下载地址。</summary>
public sealed record SamModelDefinition(SamModelKind Kind, string Name, string Folder, string Repository, string Revision,
    string Encoder, long EncoderBytes, string EncoderHash, string Decoder, long DecoderBytes, string DecoderHash,
    string? Archive = null, long ArchiveBytes = 0, string? ArchiveHash = null,
    string? EncoderData = null, long EncoderDataBytes = 0, string? EncoderDataHash = null,
    string? DecoderData = null, long DecoderDataBytes = 0, string? DecoderDataHash = null)
{
    /// <summary>实际模型文件合计体积（MB），包含外部权重。</summary>
    public long ModelMegabytes => (EncoderBytes + DecoderBytes + EncoderDataBytes + DecoderDataBytes + 999_999) / 1_000_000;
}
