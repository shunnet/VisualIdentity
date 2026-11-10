namespace Snet.Yolo.Tasks.Services;

using System.Text.Json;

/// <summary>虚拟环境探测：在环境自带的 Python 里运行只读脚本，输出一行 JSON，再解析成 <see cref="PythonEnvironmentFacts"/>。</summary>
public static class PythonEnvironmentProbe
{
    /// <summary>
    /// 探测脚本：导入 PyTorch 并在 GPU 上执行一个真实张量操作（确认内核可运行，而不只是 CUDA 可见），
    /// 再读取关键包版本；任何一步失败都写入 JSON，不让脚本崩溃。
    /// </summary>
    public const string Script = """
        import json, importlib.metadata as m
        out = {}
        try:
            import torch
            out["torch"] = torch.__version__
            out["torchCuda"] = torch.version.cuda or ""
            out["cudaAvailable"] = bool(torch.cuda.is_available())
            out["devices"] = [torch.cuda.get_device_name(i) for i in range(torch.cuda.device_count())]
            if out["cudaAvailable"]:
                torch.ones(1, device="cuda").sum().item()
                out["kernelOk"] = True
        except Exception as error:
            out["torchError"] = str(error)
        for name in ("ultralytics", "anomalib", "onnxruntime"):
            try:
                out[name] = m.version(name)
            except Exception:
                pass
        print(json.dumps(out))
        """;

    /// <summary>读取探测脚本的 JSON 输出（取最后一行 JSON，忽略前面的警告）。</summary>
    /// <param name="stdout">脚本的标准输出。</param>
    /// <returns>解析结果；没有可识别的 JSON 时为空。</returns>
    public static PythonEnvironmentFacts? Parse(string stdout)
    {
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(static text => text.StartsWith('{'));
        if (line is null) { return null; }
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return new PythonEnvironmentFacts
            {
                Torch = ReadString(root, "torch"),
                TorchCuda = ReadString(root, "torchCuda") ?? string.Empty,
                CudaAvailable = ReadBool(root, "cudaAvailable"),
                KernelOk = ReadBool(root, "kernelOk"),
                Devices = root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array
                    ? devices.EnumerateArray().Select(static device => device.GetString() ?? string.Empty).ToArray() : [],
                TorchError = ReadString(root, "torchError"),
                Ultralytics = ReadString(root, "ultralytics"),
                Anomalib = ReadString(root, "anomalib"),
                OnnxRuntime = ReadString(root, "onnxruntime"),
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>读取字符串字段。</summary>
    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>读取布尔字段，缺失按 false。</summary>
    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
