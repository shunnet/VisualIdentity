using Snet.Model.data;
using Snet.Yolo.Server.models.@enum;

namespace Snet.Yolo.Server.@interface
{
    /// <summary>
    /// 模型管理接口
    /// </summary>
    public interface IManage
    {
        /// <summary>
        /// 添加
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">模型类型</param>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> AddAsync(string file, string describe, OnnxType onnxType, CancellationToken token = default);

        /// <summary>
        /// 修改
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">类型</param>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null, CancellationToken token = default);

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="deleteFile">是否删除文件</param>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> DeleteAsync(int index, bool deleteFile = true, CancellationToken token = default);

        /// <summary>
        /// 指定查询
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> QueryAsync(int index, CancellationToken token = default);

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <param name="token">取消通知</param>
        /// <returns>结果</returns>
        Task<OperateResult> QueryAsync(CancellationToken token = default);
    }
}
