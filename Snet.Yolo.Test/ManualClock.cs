namespace Snet.Yolo.Test;

/// <summary>可向前推进单调时间的测试时钟。</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _timestamp;

    /// <summary>时钟以 TimeSpan 刻度记录。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>读取模拟的单调时间。</summary>
    public override long GetTimestamp() => _timestamp;

    /// <summary>向前推进测试时钟，由测试显式检查定时任务。</summary>
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}
