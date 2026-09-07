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
        public async Task<OperateResult> AddAsync(string file, string describe, OnnxType onnxType, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            string path = Path.GetDirectoryName(file) ?? string.Empty;
            string name = Path.GetFileName(file);

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.path == path && c.name == name, token);
            if (!result.Status)
            {
                return await operate.InsertAsync<OnnxData>(new OnnxData
                {
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
        public async Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.index == index, token);
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
                return await operate.UpdateAsync<OnnxData>(onnxData, u => new { u.describe, u.onnxType, u.updateTime }, c => c.index == index, token);
            }
            return result ?? OperateResult.CreateFailureResult("模型不存在");
        }

        /// <inheritdoc/>
        public async Task<OperateResult> DeleteAsync(int index, bool deleteFile = true, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            if (deleteFile)
            {
                OperateResult result = await operate.QueryAsync<OnnxData>(c => c.index == index, token);
                if (result.GetDetails(out List<OnnxData>? onnxData) && onnxData is { Count: > 0 })
                {
                    string path = Path.Combine(onnxData[0].path ?? string.Empty, onnxData[0].name ?? string.Empty);
                    if (File.Exists(path)) { File.Delete(path); }
                }
                else
                {
                    return result;
                }
            }
            return await operate.DeleteAsync<OnnxData>(c => c.index == index, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> QueryAsync(int index, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            return await operate.QueryAsync<OnnxData>(c => c.index == index, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> QueryAsync(CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) return init;

            return await operate.QueryAsync<OnnxData>(token: token);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            operate.Dispose();
            base.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await operate.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
