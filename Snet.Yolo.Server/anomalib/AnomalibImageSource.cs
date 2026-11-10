namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>一张已经通过上传校验的正常图片。</summary>
/// <param name="SourcePath">源图片绝对路径。</param>
/// <param name="ContentSha256">图片内容的 SHA-256 小写十六进制摘要。</param>
public sealed record AnomalibImageSource(string SourcePath, string ContentSha256);
