using SkiaSharp;
using Snet.Utility;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server.models.@enum;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Yolo.Api.Handler
{
    /// <summary>
    /// 图片处理
    /// </summary>
    public static class ImageHandler
    {
        /// <summary>单张图片允许的最大解码像素数，防止小体积压缩炸弹耗尽内存。</summary>
        public const long MaximumDecodedPixels = 100_000_000;

        /// <summary>
        /// 获取图片字节数组
        /// </summary>
        /// <param name="file">表单文件对象</param>
        /// <param name="token">取消令牌</param>
        /// <returns>字节数据</returns>
        public static async Task<byte[]> GetBytesAsync(this IFormFile file, CancellationToken token = default)
        {
            if (file.Length is < 0 or > int.MaxValue) { throw new InvalidDataException("文件过大，无法载入内存。"); }
            var bytes = new byte[(int)file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(bytes, token);
            return bytes;
        }

        /// <summary>验证上传内容确实是可解码图片，且解码尺寸位于安全上限内。</summary>
        /// <param name="bytes">上传图片的完整编码字节。</param>
        /// <returns>校验成功时返回 true。</returns>
        public static bool ValidateEncodedImage(byte[] bytes)
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null) { return false; }
            var info = codec.Info;
            return info.Width > 0 && info.Height > 0 && (long)info.Width * info.Height <= MaximumDecodedPixels;
        }


        /// <summary>
        /// 获取后缀名
        /// </summary>
        /// <param name="file">表单文件对象</param>
        /// <param name="containsSymbols">是否包含符号</param>
        /// <returns>后缀名</returns>
        public static string GetSuffix(this IFormFile file, bool containsSymbols = false)
        {
            string extension = Path.GetExtension(file.FileName);
            if (containsSymbols)
            {
                return extension;
            }
            return extension.TrimStart('.');
        }

        /// <summary>
        /// 获取图片字节
        /// </summary>
        /// <param name="sKBitmap">源</param>
        /// <param name="contentType">内容类型</param>
        /// <returns>图片字节</returns>
        public static byte[] GetImageByte(this SKBitmap sKBitmap, out string contentType)
        {
            using var resultImage = SKImage.FromBitmap(sKBitmap);
            using var encoded = resultImage.Encode(SKEncodedImageFormat.Jpeg, 100);
            contentType = "image/jpeg";
            return encoded.ToArray();
        }

        /// <summary>Transcodes an uploaded image to JPEG so stored bytes match the configured <c>.jpeg</c> name and MIME type.</summary>
        /// <param name="image">Decoded source image.</param>
        /// <returns>JPEG-encoded bytes.</returns>
        public static byte[] ToJpegBytes(this SKImage image)
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return encoded.ToArray();
        }

        /// <summary>
        /// 公共复用的 JsonSerializerOptions，避免每次 new（减少 GC 与开销）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        /// <summary>
        /// 保存图片（同步、高性能、跨平台路径拼接）
        /// </summary>
        /// <typeparam name="T">泛型数据结果对象</typeparam>
        /// <param name="result">结果图数据（字节数组）</param>
        /// <param name="origina">原图数据（字节数组）</param>
        /// <param name="data">结果数据对象，会序列化为 JSON</param>
        /// <param name="type">检查类型（用于目录命名）</param>
        /// <param name="configModel">配置（包含 BasePath、NameFormat、文件命名格式等）</param>
        /// <returns>返回生成的 name（基于 configModel.NameFormat）</returns>
        public static string SaveImage<T>(byte[] result, byte[] origina, T data, OnnxType type, ConfigModel configModel)
        {
            // 一次性捕获时间字符串，避免多次调用 DateTime.Now
            DateTime now = DateTime.Now;
            string datePart = now.ToString("yyyy-MM-dd");
            string name = now.ToString(configModel.NameFormat);

            // 跨平台安全的路径拼接
            string directory = Path.Combine(configModel.BasePath, datePart, type.ToString());
            Directory.CreateDirectory(directory); // CreateDirectory 对已存在目录安全

            // 使用 Path.Combine 构造文件路径（支持 Windows / Linux / macOS）
            string resultPath = Path.Combine(directory, string.Format(configModel.ResultImageNamingFormat, name));
            string detailsPath = Path.Combine(directory, string.Format(configModel.DetailsNamingFormat, name));
            string originalPath = Path.Combine(directory, string.Format(configModel.OriginalImageNamingFormat, name));

            // 使用 FileStream 写入（避免内部额外拷贝开销）
            using (var fs = File.Create(resultPath))
            {
                fs.Write(result, 0, result.Length);
            }

            using (var fs = File.Create(originalPath))
            {
                fs.Write(origina, 0, origina.Length);
            }

            // 序列化 JSON 并写入
            string json = data.ToJson(_jsonOptions);
            File.WriteAllText(detailsPath, json);

            return name;
        }

        /// <summary>
        /// 保存图片（异步并行写盘版本，适合高并发/高吞吐场景）
        /// </summary>
        /// <typeparam name="T">泛型数据结果对象</typeparam>
        /// <param name="result">结果图数据（字节数组）</param>
        /// <param name="origina">原图数据（字节数组）</param>
        /// <param name="data">结果数据对象，会序列化为 JSON</param>
        /// <param name="type">检查类型（用于目录命名）</param>
        /// <param name="configModel">配置（包含 BasePath、NameFormat、文件命名格式等）</param>
        /// <param name="cancellationToken">取消文件写入的令牌。</param>
        /// <returns>返回生成的 name（基于 configModel.NameFormat）</returns>
        public static async Task<string> SaveImageAsync<T>(byte[] result, byte[] origina, T data, OnnxType type, ConfigModel configModel, CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.Now;
            string datePart = now.ToString("yyyy-MM-dd");
            string name = now.ToString(configModel.NameFormat) + "-" + Guid.NewGuid().ToString("N")[..8];

            string directory = Path.Combine(configModel.BasePath, datePart, type.ToString());
            Directory.CreateDirectory(directory);

            string resultPath = Path.Combine(directory, string.Format(configModel.ResultImageNamingFormat, name));
            string detailsPath = Path.Combine(directory, string.Format(configModel.DetailsNamingFormat, name));
            string originalPath = Path.Combine(directory, string.Format(configModel.OriginalImageNamingFormat, name));

            string json = data.ToJson(_jsonOptions);
            var temporarySuffix = ".tmp-" + Guid.NewGuid().ToString("N");
            var temporaryResult = resultPath + temporarySuffix;
            var temporaryOriginal = originalPath + temporarySuffix;
            var temporaryDetails = detailsPath + temporarySuffix;
            try
            {
                await Task.WhenAll(
                    File.WriteAllBytesAsync(temporaryResult, result, cancellationToken),
                    File.WriteAllBytesAsync(temporaryOriginal, origina, cancellationToken),
                    File.WriteAllTextAsync(temporaryDetails, json, cancellationToken)).ConfigureAwait(false);
                File.Move(temporaryResult, resultPath);
                File.Move(temporaryOriginal, originalPath);
                File.Move(temporaryDetails, detailsPath);
                return name;
            }
            catch
            {
                foreach (var path in new[] { temporaryResult, temporaryOriginal, temporaryDetails, resultPath, originalPath, detailsPath })
                {
                    try { File.Delete(path); } catch { }
                }
                throw;
            }
        }
    }
}
