using SkiaSharp;
using Snet.Core.extend;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.Enums;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// YOLO 视觉识别操作类，基于 YoloDotNet 实现对象检测、图像分割、分类、姿态估计、定向检测等 AI 视觉任务。
    /// </summary>
    public class IdentityOperate : CoreUnify<IdentityOperate, IdentityData>, IIdentity, IDisposable, IAsyncDisposable
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
        protected override string CD => "一个速度极快、功能齐全的 C# 库，用于使用 YOLOv5u–v26、YOLO-World 和 YOLO-E 模型进行实时物体检测、OBB、分割、分类、位姿估计和跟踪";

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
                    return RunAsync(data.GetSource<ObbDetectionData>(), tokenSource.Token);
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
                var resultData = results.ToClassificationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(ObbDetectionData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObbDetection(image, data.Confidence, data.Iou);
                var resultData = results.ToObbDetectionResultData();
                return await EndOperateAsync(true, resultData: resultData, token: token);
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
                var resultData = results.ToObjectDetectionResultData();
                return await EndOperateAsync(true, resultData: resultData, token: token);
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
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunPoseEstimation(image, data.Confidence, data.Iou);
                var resultData = results.ToPoseEstimationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: token);
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
                var results = Init().RunSegmentation(image, data.Confidence, data.PixelConfidence, data.Iou);
                var resultData = results.ToSegmentationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: token);
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
                tokenSource.Dispose();
                tokenSource = null;
            }
            if (_yolo != null)
            {
                _yolo.Dispose();
                _yolo = null;
            }
            base.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (tokenSource != null)
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
                tokenSource = null;
            }
            if (_yolo != null)
            {
                _yolo.Dispose();
                _yolo = null;
            }
            await base.DisposeAsync();
        }
    }
}
