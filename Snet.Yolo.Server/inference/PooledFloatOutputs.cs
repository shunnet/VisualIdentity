// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
using System.Buffers;
using Microsoft.ML.OnnxRuntime;

namespace Snet.Yolo.Server.inference;

/// <summary>持有从半精度输出转换而来的池化缓冲区。</summary>
internal sealed class PooledFloatOutputs : IDisposable
{
    private float[]? _buffer0;
    private float[]? _buffer1;

    /// <summary>转换后的第一输出；释放后不再有效。</summary>
    public ReadOnlySpan<float> Output0 => _buffer0.AsSpan(0, Length0);

    /// <summary>转换后的可选第二输出；释放后不再有效。</summary>
    public ReadOnlySpan<float> Output1 => _buffer1.AsSpan(0, Length1);

    /// <summary>第一输出的有效元素数。</summary>
    private int Length0 { get; init; }
    /// <summary>第二输出的有效元素数。</summary>
    private int Length1 { get; init; }

    /// <summary>将 Float16 输出转换到租用的 Float32 缓冲区。</summary>
    /// <param name="source0">第一输出的半精度数据。</param>
    /// <param name="source1">可选第二输出的半精度数据。</param>
    /// <returns>由推理结果持有并释放的缓冲区所有者。</returns>
    public static PooledFloatOutputs Create(ReadOnlySpan<Float16> source0, ReadOnlySpan<Float16> source1)
    {
        var owner = new PooledFloatOutputs
        {
            Length0 = source0.Length,
            Length1 = source1.Length
        };

        try
        {
            owner._buffer0 = ArrayPool<float>.Shared.Rent(source0.Length);
            owner._buffer1 = source1.IsEmpty ? null : ArrayPool<float>.Shared.Rent(source1.Length);
            for (var i = 0; i < source0.Length; i++) { owner._buffer0[i] = (float)source0[i]; }
            for (var i = 0; i < source1.Length; i++) { owner._buffer1![i] = (float)source1[i]; }
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>幂等归还缓冲区，避免重复归还共享池。</summary>
    public void Dispose()
    {
        var buffer0 = Interlocked.Exchange(ref _buffer0, null);
        var buffer1 = Interlocked.Exchange(ref _buffer1, null);

        if (buffer0 is not null)
            ArrayPool<float>.Shared.Return(buffer0);
        if (buffer1 is not null)
            ArrayPool<float>.Shared.Return(buffer1);
    }
}
