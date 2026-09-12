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
        public IdentityOperate() : this(new IdentityData()) { }

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
        private readonly CancellationTokenSource tokenSource = new();

        /// <summary>
        /// yolo 对象<br/>
        /// https://github.com/NickSwardh/YoloDotNet
        /// </summary>
        private YoloDotNet.Yolo? _yolo;
        private readonly SemaphoreSlim _runLock = new(1, 1);
        private int _disposeState;

        /// <summary>
        /// 初始化
        /// </summary>
        private YoloDotNet.Yolo Init()
        {
            if (_yolo == null)
            {
                _yolo = new YoloDotNet.Yolo(new YoloDotNet.Models.YoloOptions()
                {
                    ExecutionProvider = basics.Hardware ?? throw new InvalidOperationException("未配置模型执行提供程序。"),
                    ImageResize = ImageResize.Proportional,
                });
            }
            return _yolo;
        }

        /// <inheritdoc/>
        public async Task<OperateResult> RunAsync(IData data)
        {
            return basics.IdentifyType switch
            {
                OnnxType.ObjectDetection => await RunAsync(data.GetSource<ObjectDetectionData>(), tokenSource.Token),
                OnnxType.Segmentation => await RunAsync(data.GetSource<SegmentationData>(), tokenSource.Token),
                OnnxType.Classification => await RunAsync(data.GetSource<ClassificationData>(), tokenSource.Token),
                OnnxType.PoseEstimation => await RunAsync(data.GetSource<PoseEstimationData>(), tokenSource.Token),
                OnnxType.ObbDetection => await RunAsync(data.GetSource<ObbDetectionData>(), tokenSource.Token),
                _ => OperateResult.CreateFailureResult("识别类型错误")
            };
        }

        private async Task<OperateResult> RunSerializedAsync(Func<CancellationToken, Task<OperateResult>> action, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, tokenSource.Token);
            await _runLock.WaitAsync(linkedCancellation.Token);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
                return await action(linkedCancellation.Token);
            }
            finally { _runLock.Release(); }
        }

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(ClassificationData data, CancellationToken token) => RunSerializedAsync(async effectiveToken =>
        {
            await BegOperateAsync(effectiveToken);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunClassification(image, data.Classes);
                var resultData = results.ToClassificationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: effectiveToken);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: effectiveToken);
            }
        }, token);

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(ObbDetectionData data, CancellationToken token) => RunSerializedAsync(async effectiveToken =>
        {
            await BegOperateAsync(effectiveToken);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObbDetection(image, data.Confidence, data.Iou);
                var resultData = results.ToObbDetectionResultData();
                return await EndOperateAsync(true, resultData: resultData, token: effectiveToken);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: effectiveToken);
            }
        }, token);

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(ObjectDetectionData data, CancellationToken token) => RunSerializedAsync(async effectiveToken =>
        {
            await BegOperateAsync(effectiveToken);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObjectDetection(image, data.Confidence, data.Iou);
                var resultData = results.ToObjectDetectionResultData();
                return await EndOperateAsync(true, resultData: resultData, token: effectiveToken);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: effectiveToken);
            }
        }, token);

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(PoseEstimationData data, CancellationToken token) => RunSerializedAsync(async effectiveToken =>
        {
            await BegOperateAsync(effectiveToken);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunPoseEstimation(image, data.Confidence, data.Iou);
                var resultData = results.ToPoseEstimationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: effectiveToken);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: effectiveToken);
            }
        }, token);

        /// <inheritdoc/>
        public Task<OperateResult> RunAsync(SegmentationData data, CancellationToken token) => RunSerializedAsync(async effectiveToken =>
        {
            await BegOperateAsync(effectiveToken);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunSegmentation(image, data.Confidence, data.PixelConfidence, data.Iou);
                var resultData = results.ToSegmentationResultData();
                return await EndOperateAsync(true, resultData: resultData, token: effectiveToken);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: effectiveToken);
            }
        }, token);

        /// <inheritdoc/>
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            tokenSource.Cancel();
            _runLock.Wait();
            try
            {
                _yolo?.Dispose();
                _yolo = null;
            }
            finally { _runLock.Release(); }
            base.Dispose();
            tokenSource.Dispose();
            _runLock.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) { return; }
            await tokenSource.CancelAsync();
            await _runLock.WaitAsync();
            try
            {
                _yolo?.Dispose();
                _yolo = null;
            }
            finally { _runLock.Release(); }
            await base.DisposeAsync();
            tokenSource.Dispose();
            _runLock.Dispose();
        }
    }
}
