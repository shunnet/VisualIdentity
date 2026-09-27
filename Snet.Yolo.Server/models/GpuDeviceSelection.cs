namespace Snet.Yolo.Server.models;

/// <summary>校验训练设备中的 GPU 索引，避免无效参数传给 Python 训练进程。</summary>
public static class GpuDeviceSelection
{
    /// <summary>解析 YOLO 或 Anomalib 的设备参数，auto/cpu 返回空数组。</summary>
    public static int[] Parse(string device, bool anomalib)
    {
        if (string.IsNullOrWhiteSpace(device)) { throw new ArgumentException("训练设备不能为空。", nameof(device)); }
        if (device is "auto" or "cpu" || (!anomalib && device == "mps")) { return []; }
        if (anomalib)
        {
            if (device == "cuda") { return [0]; }
            if (!device.StartsWith("cuda:", StringComparison.Ordinal)) { throw new ArgumentException("Anomalib 训练设备格式无效。", nameof(device)); }
            device = device[5..];
        }

        var parts = device.Split(',');
        if (parts.Length is < 1 or > 16) { throw new ArgumentException("GPU 设备数量无效。", nameof(device)); }
        var indices = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                || index is < 0 or > 63 || indices.Take(i).Contains(index))
            {
                throw new ArgumentException("GPU 索引必须是互不重复的非负整数。", nameof(device));
            }
            indices[i] = index;
        }
        return indices;
    }
}
