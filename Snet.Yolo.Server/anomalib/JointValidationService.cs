namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>在 Server 内完成模型查询、YOLO 识别、Anomalib 定位及空间关联。</summary>
public sealed class JointValidationService(
    AnomalibModelRegistry anomalibModels,
    AnomalibOnnxInference anomalibInference,
    ManageOperate yoloModels,
    YoloValidationService yoloValidation)
{
    /// <summary>仅返回指定用户已注册且可识别的模型。</summary>
    public async Task<JointValidationModels> ListModelsAsync(string owner, CancellationToken cancellationToken = default)
    {
        var anomalib = await anomalibModels.ListAsync(owner, cancellationToken);
        var yolo = (await yoloModels.ListAvailableByOwnerAsync(owner, cancellationToken))
            .Where(model => model.onnxType is OnnxType.ObjectDetection or OnnxType.Segmentation).ToArray();
        return new JointValidationModels(anomalib, yolo);
    }

    /// <summary>每次识别前重新按用户校验模型归属。</summary>
    public async Task<JointValidationOutput> IdentifyAsync(
        string owner,
        string? projectId,
        string? runId,
        int? yoloModelIndex,
        string imagePath,
        JointValidationMode mode,
        Action<JointValidationStage>? onStage = null,
        CancellationToken cancellationToken = default,
        AnomalibInferenceOptions? anomalibOptions = null,
        string? yoloParametersJson = null)
    {
        var available = await ListModelsAsync(owner, cancellationToken);
        RegisteredAnomalibModel? anomalibModel = null;
        if (mode != JointValidationMode.YoloOnly)
        {
            anomalibModel = available.Anomalib.FirstOrDefault(model => model.ProjectId == projectId && model.RunId == runId)
                ?? throw new InvalidOperationException("异常检测模型不存在或无权访问。");
        }

        OnnxData? yoloModel = null;
        if (mode != JointValidationMode.AnomalibOnly)
        {
            yoloModel = available.Yolo.FirstOrDefault(model => model.index == yoloModelIndex)
                ?? throw new InvalidOperationException("请选择当前用户的目标检测或实例分割 YOLO 模型。");
        }

        var detections = new List<JointDetection>();
        var yoloMilliseconds = 0L;
        if (yoloModel is not null)
        {
            yoloMilliseconds = await IdentifyYoloAsync(owner, yoloModel, imagePath, yoloParametersJson, detections, onStage, cancellationToken);
        }

        // 联合模式即使 YOLO 没有任何检测框，也要执行 Anomalib。
        cancellationToken.ThrowIfCancellationRequested();
        AnomalibInferenceOutput? anomalib = null;
        if (anomalibModel is not null)
        {
            onStage?.Invoke(JointValidationStage.AnomalibStarted);
            anomalib = await anomalibInference.IdentifyAsync(anomalibModel.OnnxPath, anomalibModel.ManifestPath, imagePath, cancellationToken, options: anomalibOptions);
            onStage?.Invoke(JointValidationStage.AnomalibCompleted);
        }

        return new JointValidationOutput(anomalib, JointValidationMatcher.Match(anomalib?.Result.Regions ?? [], detections), mode, yoloModel is not null, yoloMilliseconds);
    }

    /// <summary>完成 YOLO 阶段并将目标框转换为原图坐标。</summary>
    private async Task<long> IdentifyYoloAsync(string owner, OnnxData yoloModel, string imagePath, string? parameters,
        List<JointDetection> detections, Action<JointValidationStage>? onStage, CancellationToken cancellationToken)
    {
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        onStage?.Invoke(JointValidationStage.YoloStarted);
        var timer = Stopwatch.StartNew();
        var result = parameters is null
            ? await yoloValidation.IdentifyAsync(owner, yoloModel.index, image, cancellationToken: cancellationToken)
            : await yoloValidation.IdentifyAsync(owner, yoloModel.index, image, parameters, cancellationToken);
        timer.Stop();
        if (!result.Status) { throw new InvalidOperationException("YOLO 识别失败：" + result.Message); }
        using var codec = SKCodec.Create(imagePath) ?? throw new InvalidDataException("无法解码待识别图片。");
        var imageWidth = codec.Info.Width;
        var imageHeight = codec.Info.Height;
        if (yoloModel.onnxType == OnnxType.ObjectDetection && result.GetDetails(out List<ObjectDetectionResultData>? boxes) && boxes is not null)
        {
            foreach (var item in boxes)
            {
                var detection = CreateDetection(item.Label.Name, item.Confidence, item.BoundingBox, imageWidth, imageHeight);
                if (detection is not null) { detections.Add(detection); }
            }
        }
        else if (yoloModel.onnxType == OnnxType.Segmentation && result.GetDetails(out List<SegmentationResultData>? segments) && segments is not null)
        {
            foreach (var item in segments)
            {
                var detection = CreateDetection(item.Label.Name, item.Confidence, item.BoundingBox, imageWidth, imageHeight);
                if (detection is not null) { detections.Add(detection); }
            }
        }

        onStage?.Invoke(JointValidationStage.YoloCompleted);
        return timer.ElapsedMilliseconds;
    }

    private static JointDetection? CreateDetection(string name, double confidence, SKRectI bounds, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(bounds.Left, 0, imageWidth);
        var top = Math.Clamp(bounds.Top, 0, imageHeight);
        var right = Math.Clamp(bounds.Right, 0, imageWidth);
        var bottom = Math.Clamp(bounds.Bottom, 0, imageHeight);
        return right > left && bottom > top
            ? new JointDetection(name, confidence, new PixelRectangle(left, top, right - left, bottom - top))
            : null;
    }
}
