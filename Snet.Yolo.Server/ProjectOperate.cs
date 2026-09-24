using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models;
using System.Data.Common;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 工程管理操作类（规范化：一个工程一行；任务/标注后续拆分表）
    /// </summary>
    public class ProjectOperate : CoreUnify<ProjectOperate, string>, IProject, IDisposable, IAsyncDisposable
    {
        /// <summary>使用默认序列号创建工程存储。</summary>
        public ProjectOperate() : this(PublicHandler.DefaultSN) { }
        /// <summary>使用指定序列号创建工程存储。</summary>
        /// <param name="data">存储实例序列号。</param>
        public ProjectOperate(string data) : base(data) { }

        /// <inheritdoc/>
        protected override string CN => "工程数据库";
        /// <inheritdoc/>
        protected override string CD => "工程与任务";

        private readonly string DbPath = Path.Combine(PublicHandler.DefaultPath, "db");
        private DBOperate operate => DBOperate.Instance(new DBData.Basics
        {
            SN = PublicHandler.DefaultSN,
            ConnectStr = $"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}",
            DBType = DBData.DBType.SQLite,
            HandlerType = DBData.DBHandlerType.Default
        });
        private OperateResult? _initResult;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private int _disposeState;

        private async Task<OperateResult> InitAsync(CancellationToken token = default)
        {
            if (_initResult is not null) { return _initResult; }
            await _initLock.WaitAsync(token);
            try
            {
                if (_initResult is not null) { return _initResult; }
                if (!Directory.Exists(DbPath)) { Directory.CreateDirectory(DbPath); }
                var _st = await operate.GetStatusAsync(token);
                if (!_st.Status) { await operate.OnAsync(token); }
                if (!(await operate.ExistAsync<ProjectData>(token)).Status) { await operate.CreateAsync<ProjectData>(token); }
                await EnsureOwnerColumnAsync(token);
                await EnsureProjectKindColumnAsync(token);
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
            finally { _initLock.Release(); }
        }

        /// <summary>初始化工程表并执行兼容迁移。</summary>
        public Task<OperateResult> InitializeAsync(CancellationToken token = default) => InitAsync(token);

        /// <inheritdoc/>
        public async Task<OperateResult> AddAsync(ProjectData project, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            project.updateTime = DateTime.Now;
            return await operate.InsertAsync(project, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> UpdateAsync(ProjectData project, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            project.updateTime = DateTime.Now;
            return await operate.UpdateAsync(project, u => new { u.name, u.describe, u.kind, u.overlayOpacity, u.labelConfigXml, u.updateTime }, c => c.owner == project.owner && c.projectId == project.projectId, token);
        }

        /// <summary>删除默认管理员工作区中的工程；新代码应使用包含 owner 的重载。</summary>
        [Obsolete("Use DeleteAsync(owner, projectId, token) to enforce tenant isolation.")]
        public Task<OperateResult> DeleteAsync(string projectId, CancellationToken token = default)
            => DeleteAsync("snet", projectId, token);

        /// <summary>删除指定用户拥有的工程。</summary>
        public async Task<OperateResult> DeleteAsync(string owner, string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<ProjectData>(c => c.owner == owner && c.projectId == projectId, token);
        }

        /// <summary>在同一事务中删除工程及其任务，避免留下半删除状态。</summary>
        public Task<OperateResult> DeleteAggregateAsync(int storageProjectId, string projectId, CancellationToken token = default)
            => DeleteAggregateAsync(storageProjectId, "snet", projectId, token);

        /// <summary>在同一事务中删除指定用户的工程及其任务。</summary>
        public async Task<OperateResult> DeleteAggregateAsync(int storageProjectId, string owner, string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            await using var dbConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}");
            await dbConnection.OpenAsync(token);
            await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await dbConnection.BeginTransactionAsync(token);
            try
            {
                await using (var verifyProject = dbConnection.CreateCommand())
                {
                    verifyProject.Transaction = transaction;
                    verifyProject.CommandText = "SELECT COUNT(1) FROM [project] WHERE [id] = @storageProjectId AND [owner] = @owner AND [projectId] = @projectId";
                    AddParameter(verifyProject, "@storageProjectId", storageProjectId);
                    AddParameter(verifyProject, "@owner", owner);
                    AddParameter(verifyProject, "@projectId", projectId);
                    var count = Convert.ToInt32(await verifyProject.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
                    if (count != 1)
                    {
                        await transaction.RollbackAsync(token);
                        return OperateResult.CreateFailureResult("工程不存在或无权访问。");
                    }
                }
                await using (var deleteTasks = dbConnection.CreateCommand())
                {
                    deleteTasks.Transaction = transaction;
                    deleteTasks.CommandText = "DELETE FROM [task] WHERE [projectId] = @storageProjectId";
                    AddParameter(deleteTasks, "@storageProjectId", storageProjectId);
                    await deleteTasks.ExecuteNonQueryAsync(token);
                }
                await using (var deleteProject = dbConnection.CreateCommand())
                {
                    deleteProject.Transaction = transaction;
                    deleteProject.CommandText = "DELETE FROM [project] WHERE [owner] = @owner AND [projectId] = @projectId";
                    AddParameter(deleteProject, "@owner", owner);
                    AddParameter(deleteProject, "@projectId", projectId);
                    await deleteProject.ExecuteNonQueryAsync(token);
                }
                await transaction.CommitAsync(token);
                return OperateResult.CreateSuccessResult("ok");
            }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                return OperateResult.CreateFailureResult(ex.Message);
            }
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        /// <summary>查询默认管理员工作区中的工程；新代码应使用包含 owner 的重载。</summary>
        [Obsolete("Use QueryAsync(owner, projectId, token) to enforce tenant isolation.")]
        public Task<OperateResult> QueryAsync(string projectId, CancellationToken token = default)
            => QueryAsync("snet", projectId, token);

        /// <summary>查询指定用户的工程。</summary>
        public async Task<OperateResult> QueryAsync(string owner, string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.owner == owner && c.projectId == projectId, token);
        }

        /// <summary>查询默认管理员工作区中的全部工程。</summary>
        [Obsolete("Use QueryByOwnerAsync(owner, token) to enforce tenant isolation.")]
        public Task<OperateResult> QueryAsync(CancellationToken token = default)
            => QueryByOwnerAsync("snet", token);

        /// <summary>查询指定用户的全部工程。</summary>
        public async Task<OperateResult> QueryByOwnerAsync(string owner, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.owner == owner, token);
        }

        /// <summary>查询指定用户且工程类型匹配的全部工程。</summary>
        /// <param name="owner">工程所属用户名。</param>
        /// <param name="kind">需要查询的工程类型。</param>
        /// <param name="token">取消令牌。</param>
        public async Task<OperateResult> QueryByOwnerAsync(string owner, ProjectKind kind, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.owner == owner && c.kind == kind, token);
        }

        /// <summary>为旧工程表增加类型字段，并把历史数据稳定回填为 YOLO。</summary>
        /// <param name="token">取消令牌。</param>
        private async Task EnsureProjectKindColumnAsync(CancellationToken token)
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}");
            await connection.OpenAsync(token);
            await using var info = connection.CreateCommand();
            info.CommandText = "PRAGMA table_info([project])";
            await using var reader = await info.ExecuteReaderAsync(token);
            var hasKind = false;
            var hasDefault = false;
            while (await reader.ReadAsync(token))
            {
                if (!string.Equals(reader.GetString(1), "kind", StringComparison.OrdinalIgnoreCase)) { continue; }
                hasKind = true;
                hasDefault = !reader.IsDBNull(4) && reader.GetString(4).Trim('(', ')', '\'', '"') == "0";
                break;
            }
            await reader.DisposeAsync();
            if (!hasKind)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE [project] ADD COLUMN [kind] INTEGER NOT NULL DEFAULT 0";
                await alter.ExecuteNonQueryAsync(token);
            }
            else if (!hasDefault)
            {
                // ORM 新建的表可能已含 kind 列但无数据库默认值；保留既有数据并修复列定义。
                await using var transaction = await connection.BeginTransactionAsync(token);
                foreach (var sql in new[]
                {
                    "DROP INDEX IF EXISTS [IX_project_owner_kind]",
                    "ALTER TABLE [project] ADD COLUMN [kind_with_default] INTEGER NOT NULL DEFAULT 0",
                    "UPDATE [project] SET [kind_with_default] = CASE WHEN [kind] IN (0, 1) THEN [kind] ELSE 0 END",
                    "ALTER TABLE [project] DROP COLUMN [kind]",
                    "ALTER TABLE [project] RENAME COLUMN [kind_with_default] TO [kind]",
                })
                {
                    await using var change = connection.CreateCommand();
                    change.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                    change.CommandText = sql;
                    await change.ExecuteNonQueryAsync(token);
                }
                await transaction.CommitAsync(token);
            }
            await using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE [project] SET [kind] = 0 WHERE [kind] IS NULL OR [kind] NOT IN (0, 1)";
            await backfill.ExecuteNonQueryAsync(token);
            await using var index = connection.CreateCommand();
            index.CommandText = "CREATE INDEX IF NOT EXISTS [IX_project_owner_kind] ON [project] ([owner], [kind])";
            await index.ExecuteNonQueryAsync(token);
        }

        private async Task EnsureOwnerColumnAsync(CancellationToken token)
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}");
            await connection.OpenAsync(token);
            await using var info = connection.CreateCommand();
            info.CommandText = "PRAGMA table_info([project])";
            await using var reader = await info.ExecuteReaderAsync(token);
            var hasOwner = false;
            while (await reader.ReadAsync(token))
            {
                if (string.Equals(reader.GetString(1), "owner", StringComparison.OrdinalIgnoreCase)) { hasOwner = true; break; }
            }
            await reader.DisposeAsync();
            if (!hasOwner)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE [project] ADD COLUMN [owner] TEXT NOT NULL DEFAULT 'snet'";
                await alter.ExecuteNonQueryAsync(token);
            }
            await using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE [project] SET [owner] = 'snet' WHERE [owner] IS NULL OR TRIM([owner]) = ''";
            await backfill.ExecuteNonQueryAsync(token);
            await using var dropIndex = connection.CreateCommand();
            dropIndex.CommandText = "DROP INDEX IF EXISTS [IX_project_owner_projectId]";
            await dropIndex.ExecuteNonQueryAsync(token);
            await using var uniqueIndex = connection.CreateCommand();
            uniqueIndex.CommandText = "CREATE UNIQUE INDEX [IX_project_owner_projectId] ON [project] ([owner], [projectId])";
            await uniqueIndex.ExecuteNonQueryAsync(token);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            base.Dispose();
            _initLock.Dispose();
        }
        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            await base.DisposeAsync();
            _initLock.Dispose();
        }
    }
}
