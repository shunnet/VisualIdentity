using Snet.Model.data;

namespace Snet.Identify.@interface
{
    /// <summary>
    /// 操作接口
    /// </summary>
    public interface IOperate
    {
        /// <summary>
        /// 运行
        /// </summary>
        /// <param name="data">数据</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(IData data);
    }
}
