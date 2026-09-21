namespace Snet.Yolo.Tasks.Core.Editing;

/// <summary>
/// 简单字节 RLE：输出 [count, value] 对（count 为 1–255，值可为任意字节）。
/// 用于笔刷掩码的自洽存储；与 Label Studio @thi.ng/rle-pack 的逐字节对齐留待后续补强。
/// </summary>
public static class RleCodec
{
    /// <summary>Run-length encodes a byte mask as count/value pairs.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> mask)
    {
        var buffer = new System.Collections.Generic.List<byte>(mask.Length);
        var index = 0;
        while (index < mask.Length)
        {
            var value = mask[index];
            var run = 1;
            while (index + run < mask.Length && mask[index + run] == value && run < 255) { run++; }
            buffer.Add((byte)run);
            buffer.Add(value);
            index += run;
        }
        return buffer.ToArray();
    }

    /// <summary>Decodes count/value pairs into a mask with the requested length.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> encoded, int length)
    {
        var output = new byte[length];
        var outIndex = 0;
        var index = 0;
        while (index + 1 < encoded.Length && outIndex < length)
        {
            var run = encoded[index];
            var value = encoded[index + 1];
            index += 2;
            for (var i = 0; i < run && outIndex < length; i++) { output[outIndex++] = value; }
        }
        return output;
    }
}
