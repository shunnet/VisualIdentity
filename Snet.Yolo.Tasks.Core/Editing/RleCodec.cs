namespace Snet.Yolo.Tasks.Core.Editing;

/// <summary>
/// 简单字节 RLE：输出 [count, value] 对（count 为 1–255，值可为任意字节）。
/// 用于笔刷掩码的自洽存储；与 Label Studio @thi.ng/rle-pack 的逐字节对齐留待后续补强。
/// </summary>
public static class RleCodec
{
    /// <summary>将工作掩码最近邻映射为原图 RLE，直接编码游程，不分配原图像素数组。</summary>
    public static byte[] EncodeScaled(ReadOnlySpan<byte> mask, int width, int height, int originalWidth, int originalHeight)
    {
        if (width <= 0 || height <= 0 || (long)width * height != mask.Length || originalWidth <= 0 || originalHeight <= 0 || (long)originalWidth * originalHeight > int.MaxValue)
        { throw new ArgumentException("工作掩码或原图尺寸无效。"); }
        var buffer = new List<byte>(); var pending = 0; byte previous = 0;
        for (var y = 0; y < originalHeight; y++)
        {
            var row = (int)((long)y * height / originalHeight) * width;
            var x = 0;
            while (x < width)
            {
                var start = x; var value = mask[row + x++];
                while (x < width && mask[row + x] == value) { x++; }
                // 源像素区间 [start,x) 映射成目标区间 [ceil(start*s),ceil(x*s))。
                var count = (int)(((long)x * originalWidth + width - 1) / width - ((long)start * originalWidth + width - 1) / width);
                Append(value, count);
            }
        }
        if (pending > 0) { buffer.Add((byte)pending); buffer.Add(previous); }
        return buffer.ToArray();

        void Append(byte value, int count)
        {
            if (count == 0) { return; }
            if (pending > 0 && previous != value) { buffer.Add((byte)pending); buffer.Add(previous); pending = 0; }
            previous = value;
            while (count > 0)
            {
                var take = Math.Min(255 - pending, count); pending += take; count -= take;
                if (pending == 255) { buffer.Add(255); buffer.Add(value); pending = 0; }
            }
        }
    }

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
