using Snet.Yolo.Server.inference;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>原生常量模型验证公共推理核心的输出类型、输入校验及资源释放。</summary>
public sealed class UnifiedExecutionProviderTests
{
    /// <summary>CPU 和 CUDA 类的显式 CPU 路径必须有一致的原生 Float32 结果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatOutput_UsesOneSharedInferenceContract(bool cudaClass)
    {
        using OnnxExecutionProvider provider = cudaClass
            ? new CudaExecutionProvider(ExecutionProviderFixtures.Float, -1)
            : new CpuExecutionProvider(ExecutionProviderFixtures.Float);
        using var result = provider.Run(new float[48]);
        Assert.Equal(Enumerable.Range(0, 10).Select(value => (float)value), result.OrtSpan0.ToArray());
        Assert.Empty(result.OrtSpan1.ToArray());
    }

    /// <summary>半精度输出必须按有效长度转换为 Float32，而不是暴露池中的多余元素。</summary>
    [Fact]
    public void HalfOutput_ConvertsOnlyValidTensorElements()
    {
        using var provider = new CpuExecutionProvider(ExecutionProviderFixtures.Half);
        using var result = provider.Run(new ushort[48]);
        Assert.Equal(Enumerable.Range(0, 10).Select(value => (float)value), result.OrtSpan0.ToArray());
    }

    /// <summary>UInt8 原生输出通过字节视图读取。</summary>
    [Fact]
    public void ByteOutput_PreservesNativeByteTensor()
    {
        using var provider = new CpuExecutionProvider(ExecutionProviderFixtures.Byte);
        using var result = provider.Run(new float[48]);
        Assert.Equal(Enumerable.Range(0, 10).Select(value => (byte)value), result.OrtByteSpan0.ToArray());
    }

    /// <summary>错误元素类型、过短输入及释放后运行必须在读取原生内存前拒绝。</summary>
    [Fact]
    public void InvalidInputAndDisposedProvider_AreRejected()
    {
        var provider = new CpuExecutionProvider(ExecutionProviderFixtures.Float);
        try
        {
            Assert.Throws<ArgumentException>(() => { using var result = provider.Run(new ushort[48]); });
            Assert.Throws<ArgumentException>(() => { using var result = provider.Run(new float[47]); });
        }
        finally { provider.Dispose(); }
        provider.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { using var result = provider.Run(new float[48]); });
    }
}
