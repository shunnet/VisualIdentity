namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>解析 nvidia-smi --query-gpu ... --format=csv 输出（多行、多 GPU、旧驱动无 compute_cap）。</summary>
public static class NvidiaSmiParser
{
    /// <summary>Parses one or more GPUs from CSV output produced by <c>nvidia-smi</c>.</summary>
    public static IReadOnlyList<GpuInfo> ParseCsv(string output)
    {
        var list = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(output)) { return list; }

        string[]? header = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) { continue; }
            var cols = line.Split(',').Select(c => c.Trim()).ToArray();

            // 表头形如 name, compute_cap, driver_version, memory.total；
            // 降级查询则为 name, driver_version, memory.total（列数不同，必须按表头定位）。
            if (header is null && cols[0].Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                header = cols.Select(c => c.ToLowerInvariant()).ToArray();
                continue;
            }
            if (cols.Length < 2) { continue; }

            int nameIndex = 0, capIndex = -1, driverIndex = -1, memoryIndex = -1;
            if (header is null)
            {
                // 没有表头：按列数推断（>=4 视为含 compute_cap）
                if (cols.Length >= 4) { nameIndex = 0; capIndex = 1; driverIndex = 2; memoryIndex = 3; }
                else { nameIndex = 0; driverIndex = 1; memoryIndex = 2; }
            }
            else
            {
                nameIndex = IndexOf(header, "name");
                capIndex = IndexOf(header, "compute_cap");
                driverIndex = IndexOf(header, "driver_version");
                memoryIndex = IndexOf(header, "memory.total");
                if (nameIndex < 0) { nameIndex = 0; }
            }

            string Cell(int index) => index >= 0 && cols.Length > index ? cols[index] : string.Empty;
            var memoryText = Cell(memoryIndex).Replace("MiB", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var memory = long.TryParse(memoryText, out var parsedMemory) ? parsedMemory : (long?)null;
            list.Add(new GpuInfo(Cell(nameIndex), Cell(capIndex), Cell(driverIndex), memory, null));
        }
        return list;
    }

    private static int IndexOf(string[] header, string key)
        => Array.FindIndex(header, h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase));
}
