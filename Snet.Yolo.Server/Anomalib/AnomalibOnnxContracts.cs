namespace Snet.Yolo.Server.Anomalib;

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

/// <summary>
/// 已确认符合 Anomalib 语义的 ONNX 输出契约。
/// </summary>
public sealed class DiscoveredAnomalibOutputContract
{
    /// <summary>获取整图异常分数张量。</summary>
    public required OnnxTensorDescriptor PredictionScore { get; init; }

    /// <summary>获取整图异常标签张量。</summary>
    public required OnnxTensorDescriptor PredictionLabel { get; init; }

    /// <summary>获取像素级异常图张量。</summary>
    public required OnnxTensorDescriptor AnomalyMap { get; init; }

    /// <summary>获取像素级二值掩码张量。</summary>
    public required OnnxTensorDescriptor PredictionMask { get; init; }
}

/// <summary>
/// ONNX 输出契约不匹配时抛出的异常。
/// </summary>
public sealed class AnomalibOnnxContractException : Exception
{
    /// <summary>
    /// 使用中文错误消息创建输出契约异常。
    /// </summary>
    /// <param name="message">错误消息。</param>
    public AnomalibOnnxContractException(string message) : base(message)
    {
    }
}

/// <summary>
/// 按名称、形状和数据类型发现 Anomalib ONNX 输出，不依赖输出顺序。
/// </summary>
public static class AnomalibOutputContractDiscovery
{
    /// <summary>
    /// 根据清单名称绑定并校验四个 Anomalib 输出。
    /// </summary>
    /// <param name="manifest">已经通过校验的模型清单。</param>
    /// <param name="outputs">从 ONNX Session 读取到的输出元数据。</param>
    /// <returns>完成语义绑定的输出契约。</returns>
    /// <exception cref="AnomalibOnnxContractException">节点缺失、重名、类型或形状不匹配。</exception>
    public static DiscoveredAnomalibOutputContract Discover(
        AnomalibModelManifest manifest,
        IEnumerable<OnnxTensorDescriptor> outputs)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(outputs);
        AnomalibManifestSerializer.Validate(manifest);

        var byName = new Dictionary<string, OnnxTensorDescriptor>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (output is null || string.IsNullOrWhiteSpace(output.Name))
            {
                throw new AnomalibOnnxContractException("ONNX 输出包含空节点名称。");
            }
            if (!byName.TryAdd(output.Name, output))
            {
                throw new AnomalibOnnxContractException($"ONNX 输出节点名称重复：{output.Name}。");
            }
        }

        var score = Require(byName, manifest.Outputs.PredictionScore);
        RequireScalar(score, OnnxTensorElementType.Float32, "整图异常分数");

        var label = Require(byName, manifest.Outputs.PredictionLabel);
        RequireScalar(label, [OnnxTensorElementType.Boolean, OnnxTensorElementType.Int64, OnnxTensorElementType.Float32], "整图异常标签");

        var anomalyMap = Require(byName, manifest.Outputs.AnomalyMap);
        RequireSpatial(anomalyMap, [OnnxTensorElementType.Float32], "像素级异常图");

        var mask = Require(byName, manifest.Outputs.PredictionMask);
        RequireSpatial(mask, [OnnxTensorElementType.Boolean, OnnxTensorElementType.UInt8, OnnxTensorElementType.Int64, OnnxTensorElementType.Float32], "像素级异常掩码");

        if (!SpatialShapesCompatible(anomalyMap.Dimensions, mask.Dimensions))
        {
            throw new AnomalibOnnxContractException("异常图与异常掩码的空间尺寸不一致。");
        }

        return new DiscoveredAnomalibOutputContract
        {
            PredictionScore = score,
            PredictionLabel = label,
            AnomalyMap = anomalyMap,
            PredictionMask = mask
        };
    }

    /// <summary>
    /// 按区分大小写的节点名称获取输出。
    /// </summary>
    /// <param name="outputs">输出名称字典。</param>
    /// <param name="name">清单声明的节点名称。</param>
    /// <returns>匹配的张量描述。</returns>
    private static OnnxTensorDescriptor Require(IReadOnlyDictionary<string, OnnxTensorDescriptor> outputs, string name)
        => outputs.TryGetValue(name, out var descriptor)
            ? descriptor
            : throw new AnomalibOnnxContractException($"ONNX 缺少清单声明的输出节点：{name}。");

    /// <summary>
    /// 校验单一允许类型的标量输出。
    /// </summary>
    /// <param name="descriptor">张量描述。</param>
    /// <param name="elementType">允许的元素类型。</param>
    /// <param name="semanticName">输出语义名称。</param>
    private static void RequireScalar(OnnxTensorDescriptor descriptor, OnnxTensorElementType elementType, string semanticName)
        => RequireScalar(descriptor, [elementType], semanticName);

    /// <summary>
    /// 校验多种允许类型中的标量输出。
    /// </summary>
    /// <param name="descriptor">张量描述。</param>
    /// <param name="allowedTypes">允许的元素类型。</param>
    /// <param name="semanticName">输出语义名称。</param>
    private static void RequireScalar(OnnxTensorDescriptor descriptor, IReadOnlyCollection<OnnxTensorElementType> allowedTypes, string semanticName)
    {
        if (!allowedTypes.Contains(descriptor.ElementType))
        {
            throw TypeMismatch(descriptor, allowedTypes, semanticName);
        }
        if (descriptor.Dimensions.Count > 2
            || descriptor.Dimensions.Any(static dimension => dimension is < -1 or > 1))
        {
            throw new AnomalibOnnxContractException($"{semanticName}输出 {descriptor.Name} 必须是标量、[1] 或 [1,1]，实际为 {FormatShape(descriptor.Dimensions)}。");
        }
    }

    /// <summary>
    /// 校验像素级空间张量的类型和维度。
    /// </summary>
    /// <param name="descriptor">张量描述。</param>
    /// <param name="allowedTypes">允许的元素类型。</param>
    /// <param name="semanticName">输出语义名称。</param>
    private static void RequireSpatial(OnnxTensorDescriptor descriptor, IReadOnlyCollection<OnnxTensorElementType> allowedTypes, string semanticName)
    {
        if (!allowedTypes.Contains(descriptor.ElementType))
        {
            throw TypeMismatch(descriptor, allowedTypes, semanticName);
        }
        var dimensions = descriptor.Dimensions;
        if (dimensions.Count is not (3 or 4)
            || !IsSingleOrDynamic(dimensions[0])
            || (dimensions.Count == 4 && !IsSingleOrDynamic(dimensions[1]))
            || !IsPositiveOrDynamic(dimensions[^2])
            || !IsPositiveOrDynamic(dimensions[^1]))
        {
            throw new AnomalibOnnxContractException($"{semanticName}输出 {descriptor.Name} 必须是 [N,H,W] 或 [N,1,H,W]，实际为 {FormatShape(dimensions)}。");
        }
    }

    /// <summary>
    /// 判断两个空间输出的末两维是否兼容。
    /// </summary>
    /// <param name="left">第一个张量维度。</param>
    /// <param name="right">第二个张量维度。</param>
    /// <returns>静态维度相同或至少一侧为动态维度时返回 true。</returns>
    private static bool SpatialShapesCompatible(IReadOnlyList<long> left, IReadOnlyList<long> right)
        => DimensionsCompatible(left[^2], right[^2]) && DimensionsCompatible(left[^1], right[^1]);

    /// <summary>
    /// 判断两个维度是否相等或包含动态维度。
    /// </summary>
    /// <param name="left">左侧维度。</param>
    /// <param name="right">右侧维度。</param>
    /// <returns>维度兼容时返回 true。</returns>
    private static bool DimensionsCompatible(long left, long right) => left <= 0 || right <= 0 || left == right;

    /// <summary>
    /// 判断维度是否为单元素或动态维度。
    /// </summary>
    /// <param name="dimension">待判断维度。</param>
    /// <returns>维度为 1 或动态时返回 true。</returns>
    private static bool IsSingleOrDynamic(long dimension) => dimension is -1 or 0 or 1;

    /// <summary>
    /// 判断空间维度是否为正数或动态维度。
    /// </summary>
    /// <param name="dimension">待判断维度。</param>
    /// <returns>维度为正数、0 或 -1 时返回 true。</returns>
    private static bool IsPositiveOrDynamic(long dimension) => dimension >= -1;

    /// <summary>
    /// 创建包含期望与实际类型的异常。
    /// </summary>
    /// <param name="descriptor">张量描述。</param>
    /// <param name="allowedTypes">允许的类型。</param>
    /// <param name="semanticName">输出语义名称。</param>
    /// <returns>输出类型不匹配异常。</returns>
    private static AnomalibOnnxContractException TypeMismatch(
        OnnxTensorDescriptor descriptor,
        IEnumerable<OnnxTensorElementType> allowedTypes,
        string semanticName)
        => new($"{semanticName}输出 {descriptor.Name} 的类型必须为 {string.Join("/", allowedTypes)}，实际为 {descriptor.ElementType}。");

    /// <summary>
    /// 把张量维度格式化为便于诊断的文本。
    /// </summary>
    /// <param name="dimensions">张量维度。</param>
    /// <returns>方括号包裹的维度文本。</returns>
    private static string FormatShape(IEnumerable<long> dimensions) => $"[{string.Join(",", dimensions)}]";
}
