using SkiaSharp;
using Snet.Core.extend;
using Snet.Identify.@interface;
using Snet.Identify.models.data;
using Snet.Identify.models.@enum;
using Snet.Model.data;
using Snet.Unility;
using YoloDotNet.Enums;

namespace Snet.Identify
{
    /// <summary>
    /// 服务操作
    /// </summary>
    public class IdentityOperate : CoreUnify<IdentityOperate, IdentityData>, IOperate, IDisposable
    {
        public IdentityOperate() : base() { }
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
        public override void Dispose()
        {
            if (tokenSource != null)
            {
                tokenSource.Cancel();
                tokenSource = null;
            }
            _yolo.Dispose();
            _yolo = null;
            base.Dispose();
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
                case IdentifyType.ObjectDetection:
                    return RunAsync(data.GetSource<ObjectDetectionData>(), tokenSource.Token);
                case IdentifyType.Segmentation:
                    return RunAsync(data.GetSource<SegmentationData>(), tokenSource.Token);
                case IdentifyType.Classification:
                    return RunAsync(data.GetSource<ClassificationData>(), tokenSource.Token);
                case IdentifyType.PoseEstimation:
                    return RunAsync(data.GetSource<PoseEstimationData>(), tokenSource.Token);
                case IdentifyType.OBBDetection:
                    return RunAsync(data.GetSource<ObbDetectionData>(), tokenSource.Token);
            }
            return Task.FromResult(OperateResult.CreateFailureResult("识别类型错误"));
        }

        /// <summary>
        /// 分类
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        private async Task<OperateResult> RunAsync(ClassificationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunClassification(image, data.Classes);
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <summary>
        /// 定向检测
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        private async Task<OperateResult> RunAsync(ObbDetectionData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObbDetection(image, data.Confidence, data.Iou);
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <summary>
        /// 检测
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        private async Task<OperateResult> RunAsync(ObjectDetectionData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunObjectDetection(image, data.Confidence, data.Iou);
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <summary>
        /// 姿态
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        private async Task<OperateResult> RunAsync(PoseEstimationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunPoseEstimation(image, data.Confidence, data.Iou);
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }

        /// <summary>
        /// 分割
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="token">生命周期</param>
        /// <returns>结果</returns>
        private async Task<OperateResult> RunAsync(SegmentationData data, CancellationToken token)
        {
            await BegOperateAsync(token);
            try
            {
                using var image = SKImage.FromEncodedData(data.File);
                var results = Init().RunSegmentation(image, data.Confidence, data.PixelConfedence, data.Iou);
                return await EndOperateAsync(true, resultData: results, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, ex, token: token);
            }
        }
    }
}
