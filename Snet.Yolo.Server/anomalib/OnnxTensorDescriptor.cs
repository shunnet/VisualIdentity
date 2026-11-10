namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// ONNX 张量元数据描述。
/// </summary>
public sealed class OnnxTensorDescriptor
{
    /// <summary>
    /// 创建不可变的 ONNX 张量描述。
    /// </summary>
    /// <param name="name">节点名称。</param>
    /// <param name="elementType">张量元素类型。</param>
    /// <param name="dimensions">张量维度；动态维度可使用小于等于零的值。</param>
    public OnnxTensorDescriptor(string name, OnnxTensorElementType elementType, IEnumerable<long> dimensions)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ElementType = elementType;
        Dimensions = dimensions?.ToArray() ?? throw new ArgumentNullException(nameof(dimensions));
    }

    /// <summary>获取节点名称。</summary>
    public string Name { get; }

    /// <summary>获取张量元素类型。</summary>
    public OnnxTensorElementType ElementType { get; }

    /// <summary>获取张量维度的只读副本。</summary>
    public IReadOnlyList<long> Dimensions { get; }
}
