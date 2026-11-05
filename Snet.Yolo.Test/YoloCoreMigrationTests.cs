using Snet.Yolo.Server;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>YOLO 核心必须内置于 Server 程序集，部署不再依赖独立 YoloDotNet。</summary>
public sealed class YoloCoreMigrationTests
{
    /// <summary>主入口、配置与执行接口应属于 Server，并使用新的 YOLO 命名空间。</summary>
    [Fact]
    public void CoreTypes_BelongToServerAssemblyAndNamespace()
    {
        var assembly = typeof(ManageOperate).Assembly;
        Assert.Equal(assembly, typeof(Snet.Yolo.Server.yolo.Yolo).Assembly);
        Assert.Equal(assembly, typeof(Snet.Yolo.Server.yolo.Models.YoloOptions).Assembly);
        Assert.Equal(assembly, typeof(Snet.Yolo.Server.yolo.Models.Interfaces.IExecutionProvider).Assembly);
        Assert.Equal("Snet.Yolo.Server.yolo", typeof(Snet.Yolo.Server.yolo.Yolo).Namespace);
    }

    /// <summary>Server 的引用闭包不能残留已删除的独立 YOLO 程序集。</summary>
    [Fact]
    public void Server_DoesNotReferenceLegacyYoloAssembly()
        => Assert.DoesNotContain(typeof(ManageOperate).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "YoloDotNet");
}
