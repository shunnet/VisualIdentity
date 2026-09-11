using Xunit;

namespace Snet.Yolo.Test;

/// <summary>共享同一 SQLite 单例的集成测试必须串行执行。</summary>
[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection;
