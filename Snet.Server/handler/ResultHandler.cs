using Snet.Model.data;
using Snet.Server.models.data;

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
        public static List<ClassificationResultData>? GetClassificationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<ClassificationResultData>? data))
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
        public static List<ObjectDetectionResultData>? GetOBBDetectionResult(this OperateResult result)
        {
            if (result.GetDetails(out List<ObjectDetectionResultData>? data))
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
        public static List<PoseEstimationResultData>? GetPoseEstimationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<PoseEstimationResultData>? data))
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
        public static List<SegmentationResultData>? GetSegmentationResult(this OperateResult result)
        {
            if (result.GetDetails(out List<SegmentationResultData>? data))
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
        public static List<ObjectDetectionResultData>? GetObjectDetectionResult(this OperateResult result)
        {
            if (result.GetDetails(out List<ObjectDetectionResultData>? data))
            {
                return data;
            }
            return data;
        }
    }
}
