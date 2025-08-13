using Snet.Model.data;
using Snet.Server.models.data;

namespace Snet.Server.@interface
{
    /// <summary>
    /// 操作接口
    /// </summary>
    public interface IIdentity
    {
        /// <summary>
        /// 运行
        /// </summary>
        /// <param name="data">数据<br/>
        /// ClassificationData(byte[] file, int classes = 1):分类数据<br/>
        /// OBBDetectionData(byte[] file, double confidence = 0.2, double iou = 0.7):定向检测数据<br/>
        /// ObjectDetectionData(byte[] file, double confidence = 0.2, double iou = 0.7):检测数据<br/>
        /// PoseEstimationData(byte[] file, double confidence = 0.2, double iou = 0.7):姿态识别数据<br/>
        /// SegmentationData(byte[] file, double confidence = 0.2, double pixelConfedence = 0.65, double iou = 0.7):分割数据
        /// </param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(IData data);

        /// <summary>
        /// 分类
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(ClassificationData data, CancellationToken token);

        /// <summary>
        /// 定向检测
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(ObbbDetectionData data, CancellationToken token);

        /// <summary>
        /// 检测
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(ObjectDetectionData data, CancellationToken token);

        /// <summary>
        /// 姿态
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(PoseEstimationData data, CancellationToken token);

        /// <summary>
        /// 分割
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        Task<OperateResult> RunAsync(SegmentationData data, CancellationToken token);
    }
}
