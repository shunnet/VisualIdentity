using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 工程管理操作类（规范化：一个工程一行；任务/标注后续拆分表）
    /// </summary>
    public class ProjectOperate : CoreUnify<ProjectOperate, string>, IProject, IDisposable, IAsyncDisposable
    {
        public ProjectOperate() : base() { }
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
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
            finally { _initLock.Release(); }
        }

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
            return await operate.UpdateAsync(project, u => new { u.name, u.describe, u.overlayOpacity, u.labelConfigXml, u.updateTime }, c => c.projectId == project.projectId, token);
        }

        public async Task<OperateResult> DeleteAsync(string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<ProjectData>(c => c.projectId == projectId, token);
        }

        public async Task<OperateResult> QueryAsync(string projectId, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(c => c.projectId == projectId, token);
        }

        public async Task<OperateResult> QueryAsync(CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.QueryAsync<ProjectData>(token: token);
        }

        public override void Dispose() { base.Dispose(); }
        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
