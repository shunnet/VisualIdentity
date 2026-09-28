namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>联合验证模式。</summary>
public enum JointValidationMode
{
    /// <summary>先定位异常，再识别已知缺陷。</summary>
    Joint,
    /// <summary>仅定位异常。</summary>
    AnomalibOnly,
    /// <summary>仅识别已知缺陷。</summary>
    YoloOnly,
}

/// <summary>供调用方展示进度的推理阶段。</summary>
public enum JointValidationStage
{
    /// <summary>开始异常定位。</summary>
    AnomalibStarted,
    /// <summary>完成异常定位。</summary>
    AnomalibCompleted,
    /// <summary>开始 YOLO 识别。</summary>
    YoloStarted,
    /// <summary>完成 YOLO 识别。</summary>
    YoloCompleted,
    /// <summary>正常图片跳过 YOLO。</summary>
    YoloSkippedNormal,
}

/// <summary>一张图片的联合验证结果。</summary>
public sealed record JointValidationOutput(
    AnomalibInferenceOutput? Anomalib,
    JointMatchResult Matches,
    JointValidationMode Mode,
    bool YoloExecuted,
    long YoloMilliseconds);

/// <summary>当前用户可用于联合验证的模型。</summary>
public sealed record JointValidationModels(IReadOnlyList<RegisteredAnomalibModel> Anomalib, IReadOnlyList<OnnxData> Yolo);

/// <summary>在 Server 内完成模型查询、Anomalib 定位、YOLO 识别及空间关联。</summary>
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

        if (anomalibModel is not null) { onStage?.Invoke(JointValidationStage.AnomalibStarted); }
        var anomalib = anomalibModel is null ? null : await anomalibInference.IdentifyAsync(anomalibModel.OnnxPath, anomalibModel.ManifestPath, imagePath, cancellationToken, options: anomalibOptions);
        if (anomalib is not null) { onStage?.Invoke(JointValidationStage.AnomalibCompleted); }
        if (mode == JointValidationMode.AnomalibOnly || (mode == JointValidationMode.Joint && anomalib is { Result.IsAnomalous: false }))
        {
            if (mode == JointValidationMode.Joint) { onStage?.Invoke(JointValidationStage.YoloSkippedNormal); }
            return new JointValidationOutput(anomalib, JointValidationMatcher.Match(anomalib!.Result.Regions, []), mode, false, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        onStage?.Invoke(JointValidationStage.YoloStarted);
        var timer = Stopwatch.StartNew();
        var result = yoloParametersJson is null
            ? await yoloValidation.IdentifyAsync(owner, yoloModel!.index, image, cancellationToken: cancellationToken)
            : await yoloValidation.IdentifyAsync(owner, yoloModel!.index, image, yoloParametersJson, cancellationToken);
        timer.Stop();
        if (!result.Status) { throw new InvalidOperationException("YOLO 识别失败：" + result.Message); }
        onStage?.Invoke(JointValidationStage.YoloCompleted);

        using var bitmap = anomalib is null ? SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("无法解码待识别图片。") : null;
        var imageWidth = anomalib?.Result.OriginalWidth ?? bitmap!.Width;
        var imageHeight = anomalib?.Result.OriginalHeight ?? bitmap!.Height;
        var detections = new List<JointDetection>();
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

        return new JointValidationOutput(anomalib, JointValidationMatcher.Match(anomalib?.Result.Regions ?? [], detections), mode, true, timer.ElapsedMilliseconds);
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
