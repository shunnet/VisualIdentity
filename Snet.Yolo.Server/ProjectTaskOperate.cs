using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using System.Data.Common;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 任务/标注/结果规范化操作。
    /// </summary>
    public class ProjectTaskOperate : CoreUnify<ProjectTaskOperate, string>, IDisposable, IAsyncDisposable
    {
        /// <summary>使用默认序列号创建任务存储。</summary>
        public ProjectTaskOperate() : this(PublicHandler.DefaultSN) { }
        /// <summary>使用指定序列号创建任务存储。</summary>
        /// <param name="data">存储实例序列号。</param>
        public ProjectTaskOperate(string data) : base(data) { }

        /// <inheritdoc/>
        protected override string CN => "任务数据库";
        /// <inheritdoc/>
        protected override string CD => "任务与标注";

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
                if (!(await operate.ExistAsync<TaskData>(token)).Status) { await operate.CreateAsync<TaskData>(token); }
                if (!(await operate.ExistAsync<AnnotationData>(token)).Status) { await operate.CreateAsync<AnnotationData>(token); }
                if (!(await operate.ExistAsync<ResultData>(token)).Status) { await operate.CreateAsync<ResultData>(token); }
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
            finally { _initLock.Release(); }
        }

        /// <summary>按数据库主键删除单个任务。</summary>
        public async Task<OperateResult> DeleteTaskAsync(int taskId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<TaskData>(c => c.id == taskId, token);
        }
        /// <summary>按工程批量删除全部任务（单次调用）。</summary>
        public async Task<OperateResult> DeleteTasksByProjectAsync(int projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<TaskData>(c => c.projectId == projectId, token);
        }
        /// <summary>批量插入任务（单次调用）。</summary>
        public async Task<OperateResult> SaveTasksAsync(List<TaskData> tasks, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            if (tasks.Count == 0) { return OperateResult.CreateSuccessResult("ok"); }
            return await operate.InsertAsync<TaskData>(tasks, token);
        }

        /// <summary>在同一事务中替换工程的全部任务，失败时保留原有任务。</summary>
        /// <param name="projectId">工程数据库主键。</param>
        /// <param name="tasks">新的完整任务集合。</param>
        /// <param name="token">取消令牌。</param>
        /// <param name="labelConfigXml">可选标签配置，与任务同步提交。</param>
        /// <param name="metadata">可选工程元数据；提供时同时核对所属用户与工程标识。</param>
        public async Task<OperateResult> ReplaceTasksAsync(int projectId, List<TaskData> tasks, CancellationToken token = default, string? labelConfigXml = null, ProjectData? metadata = null)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            await using var dbConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}");
            await dbConnection.OpenAsync(token);
            await using var transaction = dbConnection.BeginTransaction();
            try
            {
                if (labelConfigXml is not null || metadata is not null)
                {
                    await using var update = dbConnection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = metadata is null
                        ? "UPDATE [project] SET [labelConfigXml] = @config, [updateTime] = @time WHERE [id] = @projectId"
                        : "UPDATE [project] SET [name] = @name, [describe] = @description, [kind] = @kind, [overlayOpacity] = @opacity, [labelConfigXml] = @config, [updateTime] = @time WHERE [id] = @projectId AND [owner] = @owner AND [projectId] = @key";
                    AddParameter(update, "@config", metadata?.labelConfigXml ?? labelConfigXml);
                    var updatedAt = DateTime.Now;
                    AddParameter(update, "@time", updatedAt);
                    if (metadata is not null)
                    {
                        metadata.updateTime = updatedAt;
                        AddParameter(update, "@name", metadata.name);
                        AddParameter(update, "@description", metadata.describe);
                        AddParameter(update, "@kind", (int)metadata.kind);
                        AddParameter(update, "@opacity", metadata.overlayOpacity);
                        AddParameter(update, "@owner", metadata.owner);
                        AddParameter(update, "@key", metadata.projectId);
                    }
                    AddParameter(update, "@projectId", projectId);
                    if (await update.ExecuteNonQueryAsync(token) != 1) { throw new InvalidOperationException("工程已删除，无法提交导入。"); }
                }
                await using (var delete = dbConnection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM [task] WHERE [projectId] = @projectId";
                    AddParameter(delete, "@projectId", projectId);
                    await delete.ExecuteNonQueryAsync(token);
                }

                foreach (var task in tasks)
                {
                    await using var insert = dbConnection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO [task] ([projectId], [taskIndex], [dataJson], [createTime]) VALUES (@projectId, @taskIndex, @dataJson, @createTime)";
                    AddParameter(insert, "@projectId", projectId);
                    AddParameter(insert, "@taskIndex", task.taskIndex);
                    AddParameter(insert, "@dataJson", task.dataJson);
                    AddParameter(insert, "@createTime", task.createTime);
                    await insert.ExecuteNonQueryAsync(token);
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

        private static void AddParameter(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        /// <summary>新增单个任务快照。</summary>
        public async Task<OperateResult> SaveTaskAsync(TaskData task, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.InsertAsync(task, token);
        }
        /// <summary>按工程与任务序号更新单个任务，避免标注翻页时重写整个工程。</summary>
        public async Task<OperateResult> UpdateTaskAsync(TaskData task, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.UpdateAsync<TaskData>(
                task,
                row => new { row.dataJson },
                row => row.projectId == task.projectId && row.taskIndex == task.taskIndex,
                token);
        }
        /// <summary>查询指定项目的全部任务快照。</summary>
        public async Task<OperateResult> QueryTasksAsync(int projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<TaskData>(c => c.projectId == projectId, token);
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
