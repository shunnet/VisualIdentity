using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// ONNX 模型管理操作类，基于 SQLite 实现模型文件的增删改查管理。
    /// </summary>
    public class ManageOperate : CoreUnify<ManageOperate, string>, IManage, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ManageOperate() : this(PublicHandler.DefaultSN) { }

        /// <summary>
        /// 管理操作<br/>
        /// 有参构造函数
        /// </summary>
        /// <param name="data">基础数据</param>
        public ManageOperate(string data) : base(data) { }

        /// <inheritdoc/>
        protected override string CN => "轻量级数据库";

        /// <inheritdoc/>
        protected override string CD => "一个轻量级、嵌入式的关系型数据库";

        /// <summary>
        /// 初始化状态
        /// </summary>
        private OperateResult? _initResult = null;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private int _disposeState;

        /// <summary>
        /// 数据库路径
        /// </summary>
        private readonly string DbPath = Path.Combine(PublicHandler.DefaultPath, "db");

        /// <summary>
        /// 数据库操作对象
        /// </summary>
        private DBOperate operate => DBOperate.Instance(new DBData.Basics
        {
            SN = PublicHandler.DefaultSN,
            ConnectStr = $"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}",
            DBType = DBData.DBType.SQLite,
            HandlerType = DBData.DBHandlerType.Default
        });

        /// <summary>
        /// 初始化
        /// </summary>
        private async Task<OperateResult> InitAsync(CancellationToken token = default)
        {
            if (_initResult is not null) { return _initResult; }
            await _initLock.WaitAsync(token);
            try
            {
                if (_initResult is not null) { return _initResult; }
                if (!Directory.Exists(DbPath))
                {
                    Directory.CreateDirectory(DbPath);
                }
                var _st = await operate.GetStatusAsync(token);
                if (!_st.Status) { await operate.OnAsync(token); }
                if (!(await operate.ExistAsync<OnnxData>(token)).Status)
                {
                    await operate.CreateAsync<OnnxData>(token);
                }
                await EnsureOwnerColumnAsync(token);
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex)
            {
                return OperateResult.CreateFailureResult(ex.Message);
            }
            finally { _initLock.Release(); }
        }

        /// <inheritdoc/>
        public Task<OperateResult> AddAsync(string file, string describe, OnnxType onnxType, CancellationToken token = default)
            => AddAsync("snet", file, describe, onnxType, token);

        /// <summary>为指定用户添加模型。</summary>
        public async Task<OperateResult> AddAsync(string owner, string file, string describe, OnnxType onnxType, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            string path = Path.GetDirectoryName(file) ?? string.Empty;
            string name = Path.GetFileName(file);

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.owner == owner && c.path == path && c.name == name, token);
            if (!result.Status)
            {
                return await operate.InsertAsync<OnnxData>(new OnnxData
                {
                    owner = owner,
                    size = new FileInfo(file).Length.GetFileSize(),
                    path = path,
                    name = name,
                    onnxType = onnxType,
                    describe = describe
                }, token);
            }
            return OperateResult.CreateFailureResult($"{name}文件已存在");
        }

        /// <inheritdoc/>
        public Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null, CancellationToken token = default)
            => UpdateAsync("snet", index, describe, onnxType, token);

        /// <summary>更新指定用户的模型。</summary>
        public async Task<OperateResult> UpdateAsync(string owner, int index, string describe, OnnxType? onnxType = null, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.owner == owner && c.index == index, token);
            if (result != null && result.GetDetails(out List<OnnxData>? resultDatas) && resultDatas is { Count: > 0 })
            {
                OnnxData onnxData = resultDatas[0];
                if (!describe.IsNullOrWhiteSpace())
                {
                    onnxData.describe = describe;
                }
                if (onnxType != null)
                {
                    onnxData.onnxType = onnxType;
                }
                onnxData.updateTime = DateTime.Now;
                return await operate.UpdateAsync<OnnxData>(onnxData, u => new { u.describe, u.onnxType, u.updateTime }, c => c.owner == owner && c.index == index, token);
            }
            return result ?? OperateResult.CreateFailureResult("模型不存在");
        }

        /// <inheritdoc/>
        public Task<OperateResult> DeleteAsync(int index, bool deleteFile = true, CancellationToken token = default)
            => DeleteAsync("snet", index, deleteFile, token);

        /// <summary>删除指定用户的模型。</summary>
        public async Task<OperateResult> DeleteAsync(string owner, int index, bool deleteFile = true, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;
            string? originalPath = null;
            string? stagedPath = null;
            if (deleteFile)
            {
                OperateResult result = await operate.QueryAsync<OnnxData>(c => c.owner == owner && c.index == index, token);
                if (result.GetDetails(out List<OnnxData>? onnxData) && onnxData is { Count: > 0 })
                {
                    originalPath = Path.Combine(onnxData[0].path ?? string.Empty, onnxData[0].name ?? string.Empty);
                    if (File.Exists(originalPath))
                    {
                        stagedPath = originalPath + ".deleting-" + Guid.NewGuid().ToString("N");
                        File.Move(originalPath, stagedPath);
                    }
                }
                else
                {
                    return result;
                }
            }
            var deleted = await operate.DeleteAsync<OnnxData>(c => c.owner == owner && c.index == index, token);
            if (!deleted.Status && stagedPath is not null && originalPath is not null)
            {
                try { File.Move(stagedPath, originalPath); } catch { }
                return deleted;
            }
            if (deleted.Status && stagedPath is not null)
            {
                try { File.Delete(stagedPath); } catch { }
            }
            return deleted;
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryAsync(owner, index, token) to enforce tenant isolation.")]
        public Task<OperateResult> QueryAsync(int index, CancellationToken token = default)
            => QueryAsync("snet", index, token);

        /// <summary>初始化模型表并执行兼容迁移。</summary>
        public Task<OperateResult> InitializeAsync(CancellationToken token = default) => InitAsync(token);

        /// <summary>查询指定用户的模型。</summary>
        public async Task<OperateResult> QueryAsync(string owner, int index, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;
            return await operate.QueryAsync<OnnxData>(c => c.owner == owner && c.index == index, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> QueryAsync(CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            return await operate.QueryAsync<OnnxData>(token: token);
        }

        /// <summary>查询指定用户的全部模型。</summary>
        public async Task<OperateResult> QueryByOwnerAsync(string owner, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;
            return await operate.QueryAsync<OnnxData>(c => c.owner == owner, token);
        }

        private async Task EnsureOwnerColumnAsync(CancellationToken token)
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}");
            await connection.OpenAsync(token);
            await using var info = connection.CreateCommand();
            info.CommandText = "PRAGMA table_info([OnnxData])";
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
                alter.CommandText = "ALTER TABLE [OnnxData] ADD COLUMN [owner] TEXT NOT NULL DEFAULT 'snet'";
                await alter.ExecuteNonQueryAsync(token);
            }
            await using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE [OnnxData] SET [owner] = 'snet' WHERE [owner] IS NULL OR TRIM([owner]) = ''";
            await backfill.ExecuteNonQueryAsync(token);
            await using var index = connection.CreateCommand();
            index.CommandText = "CREATE INDEX IF NOT EXISTS [IX_OnnxData_owner] ON [OnnxData] ([owner])";
            await index.ExecuteNonQueryAsync(token);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            operate.Dispose();
            base.Dispose();
            _initLock.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            await operate.DisposeAsync();
            await base.DisposeAsync();
            _initLock.Dispose();
        }
    }
}
