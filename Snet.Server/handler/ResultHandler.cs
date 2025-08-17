using Snet.Model.data;
using Snet.Server.models.data;
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

        /// <summary>
        /// 转换成分类结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<Classification> ToClassification(this List<ClassificationResultData> resultDatas)
        {
            return resultDatas.Select(s => new Classification
            {
                Label = s.Label,
                Confidence = s.Confidence
            }).ToList();
        }

        /// <summary>
        /// 转换成定向检测结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<OBBDetection> ToObbDetection(this List<ObbDetectionResultData> resultDatas)
        {
            return resultDatas.Select(s => new OBBDetection
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                OrientationAngle = s.OrientationAngle,
            }).ToList();
        }

        /// <summary>
        /// 转换成检测结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<ObjectDetection> ToObjectDetection(this List<ObjectDetectionResultData> resultDatas)
        {
            return resultDatas.Select(s => new ObjectDetection
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
            }).ToList();
        }

        /// <summary>
        /// 转换成姿态结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<PoseEstimation> ToPoseEstimation(this List<PoseEstimationResultData> resultDatas)
        {
            return resultDatas.Select(s => new PoseEstimation
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                KeyPoints = s.KeyPoints,
            }).ToList();
        }

        /// <summary>
        /// 转换成分割结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<Segmentation> ToSegmentation(this List<SegmentationResultData> resultDatas)
        {
            return resultDatas.Select(s => new Segmentation
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                BitPackedPixelMask = s.BitPackedPixelMask
            }).ToList();
        }

        /// <summary>
        /// 转换成分类结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<ClassificationResultData> ToClassificationResultData(this List<Classification> resultDatas)
        {
            return resultDatas.Select(s => new ClassificationResultData
            {
                Label = s.Label,
                Confidence = s.Confidence
            }).ToList();
        }

        /// <summary>
        /// 转换成定向检测结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<ObbDetectionResultData> ToObbDetectionResultData(this List<OBBDetection> resultDatas)
        {
            return resultDatas.Select(s => new ObbDetectionResultData
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                OrientationAngle = s.OrientationAngle,
            }).ToList();
        }

        /// <summary>
        /// 转换成检测结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<ObjectDetectionResultData> ToObjectDetectionResultData(this List<ObjectDetection> resultDatas)
        {
            return resultDatas.Select(s => new ObjectDetectionResultData
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
            }).ToList();
        }

        /// <summary>
        /// 转换成姿态结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<PoseEstimationResultData> ToPoseEstimationResultData(this List<PoseEstimation> resultDatas)
        {
            return resultDatas.Select(s => new PoseEstimationResultData
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                KeyPoints = s.KeyPoints,
            }).ToList();
        }

        /// <summary>
        /// 转换成分割结果
        /// </summary>
        /// <param name="resultDatas">结果数据</param>
        /// <returns>数据集合</returns>
        public static List<SegmentationResultData> ToSegmentationResultData(this List<Segmentation> resultDatas)
        {
            return resultDatas.Select(s => new SegmentationResultData
            {
                Label = s.Label,
                Confidence = s.Confidence,
                BoundingBox = s.BoundingBox,
                BitPackedPixelMask = s.BitPackedPixelMask
            }).ToList();
        }
    }
}
