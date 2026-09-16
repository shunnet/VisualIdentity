namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.IO;
using System.Text;

/// <summary>
/// ONNX 元数据修补。
///
/// 背景（实测复现）：YoloDotNet 会从 ONNX 的 <c>description</c> 元数据里解析模型型号，
/// 只认识到 YOLO11 / YOLOv8 / YOLOv5 等；遇到 “Ultralytics YOLO26n model trained on ...”
/// 会在 <c>RunObjectDetection</c> 内抛 <c>IndexOutOfRangeException</c>（验证页报“识别返回: Index was outside the bounds of the array”）。
/// YOLO26 的检测头与 YOLO11 同构（输出同为 4+类别数 通道），因此把 description 中的
/// “YOLO26” 规范成 “YOLO11” 即可让推理正常工作（NuGet 上最新 YoloDotNet 4.2.0 仍未支持 YOLO26）。
///
/// 替换是**等长**的（26 → 11），所以可以直接就地覆盖字节，不改变 protobuf 结构与文件大小。
/// </summary>
public static class OnnxMetadata
{
    /// <summary>把 description 中不被 YoloDotNet 识别的 YOLO26 型号名改成等长的 YOLO11。</summary>
    public static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) { return description ?? string.Empty; }
        return description.Replace("YOLO26", "YOLO11", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断 description 是否需要规范化。</summary>
    public static bool NeedsNormalization(string? description)
        => !string.IsNullOrEmpty(description)
           && description.Contains("YOLO26", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 就地规范化 ONNX 文件里 description 元数据的型号名（等长覆盖）。
    /// 返回被替换后的新描述；不需要或找不到时返回 null，文件保持不变。
    /// </summary>
    public static string? TryNormalizeDescriptionFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { return null; }

        const byte keyTag = 0x0A;        // StringStringEntryProto.key（字段 1）
        const byte valueTag = 0x12;      // StringStringEntryProto.value（字段 2）
        var key = Encoding.ASCII.GetBytes("description");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.RandomAccess);
        var buffer = new byte[Math.Min(stream.Length, 4L * 1024 * 1024)];
        long position = 0;
        while (position < stream.Length)
        {
            stream.Position = position;
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) { break; }

            for (var index = 0; index + key.Length + 2 < read; index++)
            {
                // 匹配 0x0A <len=11> "description"
                if (buffer[index] != keyTag || buffer[index + 1] != key.Length) { continue; }
                var matches = true;
                for (var k = 0; k < key.Length; k++)
                {
                    if (buffer[index + 2 + k] != key[k]) { matches = false; break; }
                }
                if (!matches) { continue; }

                var valueTagIndex = index + 2 + key.Length;
                if (valueTagIndex >= read || buffer[valueTagIndex] != valueTag) { continue; }
                var valueLengthIndex = valueTagIndex + 1;
                var valueLength = ReadVarint(buffer, read, ref valueLengthIndex);
                if (valueLength is null) { continue; }
                var valueStart = valueLengthIndex;
                if (valueStart + valueLength.Value > read) { continue; }

                var original = Encoding.UTF8.GetString(buffer, valueStart, valueLength.Value);
                if (!NeedsNormalization(original)) { return null; }

                var replacement = NormalizeDescription(original);
                var replacementBytes = Encoding.UTF8.GetBytes(replacement);
                if (replacementBytes.Length != valueLength.Value)
                {
                    // 等长是这套就地覆盖方案的前提；长度不一致时宁可不改，也不破坏文件
                    return null;
                }

                stream.Position = position + valueStart;
                stream.Write(replacementBytes, 0, replacementBytes.Length);
                stream.Flush(true);
                return replacement;
            }

            // 保留少量重叠，避免匹配串跨块
            if (read < buffer.Length) { break; }
            position += read - 64;
        }

        return null;
    }

    private static int? ReadVarint(byte[] buffer, int length, ref int index)
    {
        var value = 0;
        var shift = 0;
        while (index < length)
        {
            var current = buffer[index++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0) { return value; }
            shift += 7;
            if (shift > 28) { return null; }
        }
        return null;
    }
}
