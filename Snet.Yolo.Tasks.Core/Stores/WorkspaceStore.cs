namespace Snet.Yolo.Tasks.Core.Stores;

using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 基于 EF Core + SQLite 的工程仓储实现。
/// </summary>
public sealed class WorkspaceStore : IWorkspaceStore
{
    private readonly IDbContextFactory<WorkspaceDbContext> _contextFactory;

    /// <summary>构造（注入工厂，避免仓储长生命周期持有上下文）。</summary>
    public WorkspaceStore(IDbContextFactory<WorkspaceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string name, string json, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Workspaces.FirstOrDefaultAsync(row => row.Name == name, cancellationToken);
        if (existing is null)
        {
            context.Workspaces.Add(new WorkspaceRow { Name = name, Json = json, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Json = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Workspaces.Where(row => row.Name == name)
            .Select(row => row.Json)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Workspaces.OrderBy(row => row.Name).Select(row => row.Name).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Workspaces.FirstOrDefaultAsync(row => row.Name == name, cancellationToken);
        if (existing is not null)
        {
            context.Workspaces.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
