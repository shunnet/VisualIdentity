using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Server.handler;
using Snet.Server.models.data;
using Snet.Server.models.@enum;
using Snet.Unility;

namespace Snet.Server
{
    /// <summary>
    /// 数据库操作
    /// </summary>
    public class OnnxOperate : CoreUnify<OnnxOperate, string>, IDisposable
    {
        public OnnxOperate() : base() { }
        public OnnxOperate(string data) : base(data) { }
        /// <inheritdoc/>
        protected override string CN => "轻量级数据库";
        /// <inheritdoc/>
        protected override string CD => "一个轻量级、嵌入式的关系型数据库";

        /// <summary>
        /// 初始化状态
        /// </summary>
        private OperateResult? _initResult = null;

        /// <summary>
        /// 初始化
        /// </summary>
        private async Task<OperateResult> Init()
        {
            try
            {
                if (!Directory.Exists(DbPath))
                {
                    Directory.CreateDirectory(DbPath);
                }
                return await operate.OnAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return OperateResult.CreateFailureResult(ex.Message);
            }
        }

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
            ConnectStr = $"Data Source={Path.Combine(DbPath, "onnx.db")}",
            DBType = DBData.DBType.SQLite,
            HandlerType = DBData.DBHandlerType.Default
        });

        /// <summary>
        /// 添加
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">模型类型</param>
        /// <returns>结果</returns>
        public async Task<OperateResult> AddAsync(string file, string describe, OnnxType onnxType)
        {
            _initResult ??= await Init();

            if (!_initResult.Status) return _initResult;

            string path = Path.GetDirectoryName(file);
            string name = Path.GetFileName(file);

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.path == path && c.name == name);
            if (!result.Status)
            {
                return await operate.InsertAsync<OnnxData>(new OnnxData
                {
                    size = ((long)File.ReadAllBytes(file).Length).GetFileSize(),
                    path = path,
                    name = name,
                    onnxType = onnxType,
                    describe = describe
                });
            }
            return OperateResult.CreateFailureResult($"{name}文件已存在");
        }

        /// <summary>
        /// 修改
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">类型</param>
        /// <returns>结果</returns>
        public async Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null)
        {
            _initResult ??= await Init();

            if (!_initResult.Status) return _initResult;

            OperateResult result = await operate.QueryAsync<OnnxData>(c => c.index == index);
            if (result != null && result.GetDetails(out List<OnnxData>? resultDatas))
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
                return await operate.UpdateAsync<OnnxData>(onnxData, u => new { u.describe, u.onnxType, u.updateTime }, c => c.index == index);
            }
            else
                return result;
        }


        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="deleteFile">是否删除文件</param>
        /// <returns>结果</returns>
        public async Task<OperateResult> DeleteAsync(int index, bool deleteFile = true)
        {
            _initResult ??= await Init();

            if (!_initResult.Status) return _initResult;

            if (deleteFile)
            {
                OperateResult result = await operate.QueryAsync<OnnxData>(c => c.index == index);
                if (result.GetDetails(out List<OnnxData>? onnxData))
                {
                    string path = Path.Combine(onnxData[0].path, onnxData[0].name);
                    File.Delete(path);
                }
                else
                {
                    return result;
                }
            }
            return await operate.DeleteAsync<OnnxData>(c => c.index == index);
        }

        /// <summary>
        /// 指定查询
        /// </summary>
        /// <param name="index">下标</param>
        /// <returns>结果</returns>
        public async Task<OperateResult> QueryAsync(int index)
        {
            _initResult ??= await Init();

            if (!_initResult.Status) return _initResult;

            return await operate.QueryAsync<OnnxData>(c => c.index == index);
        }

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <returns>结果</returns>
        public async Task<OperateResult> QueryAsync()
        {
            _initResult ??= await Init();

            if (!_initResult.Status) return _initResult;

            return await operate.QueryAsync<OnnxData>();
        }

    }
}
