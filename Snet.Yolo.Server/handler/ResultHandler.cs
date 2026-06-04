using Snet.Model.data;
using Snet.Yolo.Server.models.data;
using YoloDotNet.Models;

namespace Snet.Yolo.Server.handler
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
            => result.GetDetails(out List<ClassificationResultData>? data) ? data : null;

        /// <summary>
        /// 获取定向检测结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<ObbDetectionResultData>? GetOBBDetectionResult(this OperateResult result)
            => result.GetDetails(out List<ObbDetectionResultData>? data) ? data : null;

        /// <summary>
        /// 获取姿态结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<PoseEstimationResultData>? GetPoseEstimationResult(this OperateResult result)
            => result.GetDetails(out List<PoseEstimationResultData>? data) ? data : null;

        /// <summary>
        /// 获取分割结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<SegmentationResultData>? GetSegmentationResult(this OperateResult result)
            => result.GetDetails(out List<SegmentationResultData>? data) ? data : null;

        /// <summary>
        /// 获取检测结果
        /// </summary>
        /// <param name="result">统一结果</param>
        /// <returns>指定结果</returns>
        public static List<ObjectDetectionResultData>? GetObjectDetectionResult(this OperateResult result)
            => result.GetDetails(out List<ObjectDetectionResultData>? data) ? data : null;

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
                BitPackedPixelMask = s.BitPackedPixelMask,
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
                Position = s.BoundingBox.ToString()
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
                Position = s.BoundingBox.ToString()
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
                Position = s.BoundingBox.ToString()
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
                BitPackedPixelMask = s.BitPackedPixelMask,
                Position = s.BoundingBox.ToString()
            }).ToList();
        }
    }
}
