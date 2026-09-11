using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using System.Data.Common;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 工程管理操作类（规范化：一个工程一行；任务/标注后续拆分表）
    /// </summary>
    public class ProjectOperate : CoreUnify<ProjectOperate, string>, IProject, IDisposable, IAsyncDisposable
    {
        public ProjectOperate() : this(PublicHandler.DefaultSN) { }
        public ProjectOperate(string data) : base(data) { }

        protected override string CN => "工程数据库";
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
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
            finally { _initLock.Release(); }
        }

        /// <summary>初始化工程表并执行兼容迁移。</summary>
        public Task<OperateResult> InitializeAsync(CancellationToken token = default) => InitAsync(token);

        public async Task<OperateResult> AddAsync(ProjectData project, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            project.updateTime = DateTime.Now;
            return await operate.InsertAsync(project, token);
        }

        public async Task<OperateResult> UpdateAsync(ProjectData project, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            project.updateTime = DateTime.Now;
            return await operate.UpdateAsync(project, u => new { u.name, u.describe, u.overlayOpacity, u.labelConfigXml, u.updateTime }, c => c.owner == project.owner && c.projectId == project.projectId, token);
        }

        public async Task<OperateResult> DeleteAsync(string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<ProjectData>(c => c.projectId == projectId, token);
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
            await using var transaction = dbConnection.BeginTransaction();
            try
            {
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

        public async Task<OperateResult> QueryAsync(string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.projectId == projectId, token);
        }

        /// <summary>查询指定用户的工程。</summary>
        public async Task<OperateResult> QueryAsync(string owner, string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.owner == owner && c.projectId == projectId, token);
        }

        public async Task<OperateResult> QueryAsync(CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(token: token);
        }

        /// <summary>查询指定用户的全部工程。</summary>
        public async Task<OperateResult> QueryByOwnerAsync(string owner, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.owner == owner, token);
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
            await using var index = connection.CreateCommand();
            index.CommandText = "CREATE INDEX IF NOT EXISTS [IX_project_owner_projectId] ON [project] ([owner], [projectId])";
            await index.ExecuteNonQueryAsync(token);
        }

        public override void Dispose() { base.Dispose(); }
        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
