using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 识别结果的序列化契约。
///
/// 现场事故：识别失败时 ResultData 里带的是异常对象（Exception.TargetSite 是 MethodBase），
/// 验证页直接 JsonSerializer.Serialize 结果会抛
/// “Serialization and deserialization of 'System.Reflection.MethodBase' instances is not supported.
///  Path: $.ResultData.TargetSite.”，于是用户只看到一句序列化失败，
/// 模型/运行时真正报的错被完全吞掉。
/// </summary>
public sealed class ValidationResultSerializationTests
{
    [Fact]
    public void TrySerialize_ReturnsJsonForOrdinaryResults()
    {
        var json = ValidationService.TrySerialize(new { Status = true, RunTime = 12.5 });

        Assert.NotNull(json);
        Assert.Contains("\"Status\":true", json);
        Assert.Contains("12.5", json);
    }

    [Fact]
    public void TrySerialize_ReturnsNullForUnsupportedGraphsInsteadOfThrowing()
    {
        // 必须用“真正抛出来过”的异常：只有抛过的异常才有 TargetSite（MethodBase），
        // 这才是现场报错的原因（$.ResultData.TargetSite）。
        static Exception Thrown()
        {
            try { throw new InvalidOperationException("识别内部出错"); }
            catch (Exception error) { return error; }
        }

        var broken = ValidationService.TrySerialize(new
        {
            Status = false,
            ResultData = new object[] { Thrown() },
        });

        Assert.Null(broken);
    }

    [Fact]
    public void TrySerialize_SupportsIndentedOutput()
    {
        var json = ValidationService.TrySerialize(new { Status = true }, indented: true);

        Assert.NotNull(json);
        Assert.Contains(Environment.NewLine, json);
    }
}
