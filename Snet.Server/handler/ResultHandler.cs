using Snet.Model.data;
using YoloDotNet.Models;

namespace Snet.Server.handler
{
    /// <summary>
    /// 结果处理
    /// </summary>
    public static class ResultHandler
    {
        /// <summary>
        /// 获取分类结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<Classification>? GetClassificationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<Classification>? data))
            {
                return data;
            }
            return data;
        }

        /// <summary>
        /// 获取定向检测结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<ObjectDetection>? GetOBBDetectionResult(this OperateResult result)
        {
            if (result.GetDetails(out List<ObjectDetection>? data))
            {
                return data;
            }
            return data;
        }

        /// <summary>
        /// 获取姿态结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<PoseEstimation>? GetPoseEstimationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<PoseEstimation>? data))
            {
                return data;
            }
            return data;
        }

        /// <summary>
        /// 获取分割结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<Segmentation>? GetSegmentationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<Segmentation>? data))
            {
                return data;
            }
            return data;
        }

        /// <summary>
        /// 获取检测结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<ObjectDetection>? GetObjectDetectionResult(this OperateResult result)
        {
            if (result.GetDetails(out List<ObjectDetection>? data))
            {
                return data;
            }
            return data;
        }
    }
}
