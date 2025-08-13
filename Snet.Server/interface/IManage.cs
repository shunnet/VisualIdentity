using Snet.Model.data;
using Snet.Server.models.@enum;

namespace Snet.Server.@interface
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
        /// <returns>结果</returns>
        Task<OperateResult> AddAsync(string file, string describe, OnnxType onnxType);

        /// <summary>
        /// 修改
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="describe">描述</param>
        /// <param name="onnxType">类型</param>
        /// <returns>结果</returns>
        Task<OperateResult> UpdateAsync(int index, string describe, OnnxType? onnxType = null);

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="index">下标</param>
        /// <param name="deleteFile">是否删除文件</param>
        /// <returns>结果</returns>
        Task<OperateResult> DeleteAsync(int index, bool deleteFile = true);

        /// <summary>
        /// 指定查询
        /// </summary>
        /// <param name="index">下标</param>
        /// <returns>结果</returns>
        Task<OperateResult> QueryAsync(int index);

        /// <summary>
        /// 查询所有
        /// </summary>
        /// <returns>结果</returns>
        Task<OperateResult> QueryAsync();
    }
}
