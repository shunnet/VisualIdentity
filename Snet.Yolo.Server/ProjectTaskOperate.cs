using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 任务/标注/结果规范化操作。
    /// </summary>
    public class ProjectTaskOperate : CoreUnify<ProjectTaskOperate, string>, IDisposable, IAsyncDisposable
    {
        public ProjectTaskOperate() : base() { }
        public ProjectTaskOperate(string data) : base(data) { }

        protected override string CN => "任务数据库";
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

        private async Task<OperateResult> InitAsync(CancellationToken token = default)
        {
            if (_initResult is not null) { return _initResult; }
            try
            {
                if (!Directory.Exists(DbPath)) { Directory.CreateDirectory(DbPath); }
                await operate.OnAsync(token); // 忽略已连接(Status=False)
                if (!(await operate.ExistAsync<TaskData>(token)).Status) { await operate.CreateAsync<TaskData>(token); }
                if (!(await operate.ExistAsync<AnnotationData>(token)).Status) { await operate.CreateAsync<AnnotationData>(token); }
                if (!(await operate.ExistAsync<ResultData>(token)).Status) { await operate.CreateAsync<ResultData>(token); }
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
        }

        public async Task<OperateResult> DeleteTaskAsync(int taskId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<TaskData>(c => c.id == taskId, token);
        }
        public async Task<OperateResult> SaveTaskAsync(TaskData task, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.InsertAsync(task, token);
        }
        public async Task<OperateResult> QueryTasksAsync(int projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<TaskData>(c => c.projectId == projectId, token);
        }

        public override void Dispose() { base.Dispose(); }
        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
