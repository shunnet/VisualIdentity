using SkiaSharp;
using Snet.Core.extend;
using Snet.Model.data;
using Snet.Server.@interface;
using Snet.Server.models.data;
using Snet.Server.models.@enum;
using Snet.Unility;
using YoloDotNet.Enums;

namespace Snet.Server
{
    /// <summary>
    /// 识别操作
    /// </summary>
    public class IdentityOperate : CoreUnify<IdentityOperate, IdentityData>, IIdentity, IDisposable
    {
        /// <summary>
        /// 识别操作<br/>
        /// 无参构造函数
        /// </summary>
        public IdentityOperate() : base() { }

        /// <summary>
        /// 识别操作<br/>
        /// 有参构造函数
        /// </summary>
        /// <param name="data">基础数据</param>
        public IdentityOperate(IdentityData data) : base(data) { }

        /// <inheritdoc/>
        protected override string CN => "视觉识别";

        /// <inheritdoc/>
        protected override string CD => "一个速度极快、功能齐全的 C# 库，用于使用 YOLOv5u–v12、YOLO-World 和 YOLO-E 模型进行实时物体检测、OBB、分割、分类、位姿估计和跟踪";

        /// <summary>
        /// 生命周期
        /// </summary>
        private CancellationTokenSource tokenSource;

        /// <summary>
        /// yolo 对象<br/>
        /// https://github.com/NickSwardh/YoloDotNet
        /// </summary>
        private YoloDotNet.Yolo _yolo;

        /// <summary>
        /// 初始化
        /// </summary>
        private YoloDotNet.Yolo Init()
        {
            if (_yolo == null)
            {
                _yolo = new YoloDotNet.Yolo(new YoloDotNet.Models.YoloOptions()
                {
                    ExecutionProvider = basics.Hardware,
                    OnnxModel = basics.OnnxPath,
                    ImageResize = ImageResize.Proportional,
                });
            }
            return _yolo;
        }

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(IData data)
        {
            if (tokenSource == null)
            {
                tokenSource = new CancellationTokenSource();
            }
            switch (basics.IdentifyType)
            {
                case OnnxType.ObjectDetection:
                    return RunAsync(data.GetSource<ObjectDetectionData>(), tokenSource.Token);
                case OnnxType.Segmentation:
                    return RunAsync(data.GetSource<SegmentationData>(), tokenSource.Token);
                case OnnxType.Classification:
                    return RunAsync(data.GetSource<ClassificationData>(), tokenSource.Token);
                case OnnxType.PoseEstimation:
                    return RunAsync(data.GetSource<PoseEstimationData>(), tokenSource.Token);
                case OnnxType.ObbDetection:
                    return RunAsync(data.GetSource<ObbbDetectionData>(), tokenSource.Token);
            }
            return Task.FromResult(OperateResult.CreateFailureResult("识别类型错误"));
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(ClassificationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunClassification(image, data.Classes);
                List<ClassificationResultData> newResults = results.Select(s => new ClassificationResultData
                {
                    Label = s.Label,
                    Confidence = s.Confidence,
                }).ToList();
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(ObbbDetectionData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObbDetection(image, data.Confidence, data.Iou);
                List<ObbDetectionResultData> newResults = results.Select(s => new ObbDetectionResultData
                {
                    Label = s.Label,
                    Confidence = s.Confidence,
                    BoundingBox = s.BoundingBox,
                    OrientationAngle = s.OrientationAngle,
                }).ToList();
                return await EndOperateAsync(true, resultData: newResults, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(ObjectDetectionData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObjectDetection(image, data.Confidence, data.Iou);
                List<ObjectDetectionResultData> newResults = results.Select(s => new ObjectDetectionResultData
                {
                    Label = s.Label,
                    Confidence = s.Confidence,
                    BoundingBox = s.BoundingBox,
                }).ToList();
                return await EndOperateAsync(true, resultData: newResults, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(PoseEstimationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunPoseEstimation(image, data.Confidence, data.Iou);
                List<PoseEstimationResultData> newResults = results.Select(s => new PoseEstimationResultData
                {
                    Label = s.Label,
                    Confidence = s.Confidence,
                    BoundingBox = s.BoundingBox,
                    KeyPoints = s.KeyPoints,
                }).ToList();
                return await EndOperateAsync(true, resultData: newResults, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(SegmentationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunSegmentation(image, data.Confidence, data.PixelConfedence, data.Iou);
                List<SegmentationResultData> newResults = results.Select(s => new SegmentationResultData
                {
                    Label = s.Label,
                    Confidence = s.Confidence,
                    BoundingBox = s.BoundingBox,
                    BitPackedPixelMask = s.BitPackedPixelMask
                }).ToList();
                return await EndOperateAsync(true, resultData: newResults, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            if (tokenSource != null)
            {
                tokenSource.Cancel();
                tokenSource = null;
            }
            if (_yolo != null)
            {
                _yolo.Dispose();
                _yolo = null;
            }
            base.Dispose();
        }
    }
}
