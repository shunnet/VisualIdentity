namespace Snet.Yolo.Tasks.Core.Serialization.Import;

/// <summary>把导入类别合并到项目标签配置后的结果。</summary>
/// <param name="Xml">合并且通过配置校验的 XML。</param>
/// <param name="ClassNames">每个 YOLO 类别索引最终对应的项目标签名称。</param>
public sealed record YoloLabelMergeResult(string Xml, IReadOnlyList<string> ClassNames);
