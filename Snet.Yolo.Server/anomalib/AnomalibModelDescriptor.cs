namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Anomalib 模型的稳定描述。</summary>
/// <param name="Kind">应用侧模型类型。</param>
/// <param name="PythonName">传给 Python 流水线的稳定名称。</param>
/// <param name="DisplayName">界面显示名称。</param>
/// <param name="Experimental">是否属于实验模型。</param>
public sealed record AnomalibModelDescriptor(
    AnomalibModelKind Kind,
    string PythonName,
    string DisplayName,
    bool Experimental);
