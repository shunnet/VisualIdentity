using SkiaSharp;
using Snet.Core.handler;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tool.Data;
using System.IO;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Tool.ViewModel;

public class YoloSegmentViewModel : YoloDetectViewModel
{
    /// <summary>
    /// 像素置信度
    /// </summary>
    public float PixelConfidence
    {
        get => pixelConfedence;
        set => SetProperty(ref pixelConfedence, value);
    }
    private float pixelConfedence = 0.65f;

    /// <summary>
    /// 验证图片
    /// </summary>
    public override async Task V_ImageAsync(ItemsControlBody item, CancellationToken token = default)
    {
        await Task.Run(async () =>
        {
            try
            {
                using SKImage image = SKImage.FromEncodedData(item.Path);
                time.StartRecord();
                OperateResult operateResult = await YoloInit(OnnxType.Segmentation).RunAsync(new SegmentationData
                {
                    Confidence = Confidence,
                    Iou = Iou,
                    PixelConfidence = PixelConfidence,
                    File = image.Encode().ToArray()
                });
                List<Segmentation> results = operateResult.GetSegmentationResult().ToSegmentation();
                string msg = $"\r\n{App.LanguageOperate.GetLanguageValue("验证")} : {Path.GetFileName(item.Path)}\r\n{App.LanguageOperate.GetLanguageValue("大小")} : {item.Description}\r\n{App.LanguageOperate.GetLanguageValue("用时")} : {time.StopRecord().milliseconds} ms";
                msg += $"\r\n{App.LanguageOperate.GetLanguageValue("目标")} : <{results.Count}> {App.LanguageOperate.GetLanguageValue("个")}";
                if (results.Count > 0)
                {
                    for (int i = 0; i < results.Count; i++)
                    {
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("标签")} : {results[i].Label.Name}";
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("准度")} : {results[i].Confidence}";
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("坐标")} : {results[i].BoundingBox.ToString()}";
                        if (Statistics)
                        {
                            Confidences.Add(results[i].Confidence);
                        }
                    }
                }
                msg += $"\r\n-------------------------------------------------------------------";
                using SKBitmap resultImage = image.Draw(results);   //绘制所有
                ResultImage = await ConvertSKImageToImageSourceAsync(resultImage);
                await msgShow(msg);
            }
            catch (Exception ex)
            {
                await msgShow($"{App.LanguageOperate.GetLanguageValue("验证图片异常")}:{ex.Message}");
            }
        }, token);
    }
}

