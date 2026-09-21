
namespace Snet.Yolo.Tasks.Core.Editing;

using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// 最小 RGBA PNG 编码器（无外部依赖）：输入单通道 mask（0/255），输出 RGBA（alpha=mask, rgb=0）。
/// 用于 Brush→PNG 掩码导出。
/// </summary>
public static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes an 8-bit grayscale mask as PNG.</summary>
    public static byte[] EncodeMask(ReadOnlySpan<byte> mask, int width, int height)
    {
        var raw = new byte[height * (1 + width * 4)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0; // filter None
            for (var x = 0; x < width; x++)
            {
                var a = mask[y * width + x];
                raw[offset++] = 0;
                raw[offset++] = 0;
                raw[offset++] = 0;
                raw[offset++] = a;
            }
        }

        using var idatStream = new MemoryStream();
        using (var zlib = new ZLibStream(idatStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }
        var idat = idatStream.ToArray();

        using var output = new MemoryStream();
        void WriteChunk(string type, byte[] data)
        {
            output.Write(ToBytes((uint)data.Length), 0, 4);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, data.Length);
            var crc = Crc(typeBytes, data);
            output.Write(ToBytes(crc), 0, 4);
        }

        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var ihdr = new byte[13];
        ToBytes((uint)width).CopyTo(ihdr, 0);
        ToBytes((uint)height).CopyTo(ihdr, 4);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk("IHDR", ihdr);
        WriteChunk("IDAT", idat);
        WriteChunk("IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static uint Crc(byte[] typeBytes, byte[] data)
    {
        var bytes = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(bytes, 0);
        data.CopyTo(bytes, typeBytes.Length);
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes) { crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8); }
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) { c = ((c & 1) != 0) ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
            table[n] = c;
        }
        return table;
    }

    private static byte[] ToBytes(uint value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
}
