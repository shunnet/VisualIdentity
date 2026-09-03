namespace Snet.Yolo.Tasks.Core;

using Microsoft.EntityFrameworkCore;
using System;

/// <summary>
/// 单机工作区 SQLite 数据库上下文：按名称存取序列化的工程文件(.lsp JSON)。
/// </summary>
public sealed class WorkspaceDbContext : DbContext
{
    /// <summary>工程记录表。</summary>
    public DbSet<WorkspaceRow> Workspaces => Set<WorkspaceRow>();

    /// <summary>构造。</summary>
    public WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceRow>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasIndex(row => row.Name).IsUnique();
            entity.Property(row => row.Name).HasMaxLength(200);
        });
    }
}

/// <summary>工程存储行：名称 + 序列化 JSON。</summary>
public sealed class WorkspaceRow
{
    /// <summary>自增主键。</summary>
    public int Id { get; set; }

    /// <summary>工程名（唯一）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工程文档 JSON（WorkspaceDocument 序列化）。</summary>
    public string Json { get; set; } = string.Empty;

    /// <summary>最近更新时间。</summary>
    public DateTime UpdatedAt { get; set; }
}
