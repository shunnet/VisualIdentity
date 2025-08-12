using Snet.Model.data;

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
    }
}
