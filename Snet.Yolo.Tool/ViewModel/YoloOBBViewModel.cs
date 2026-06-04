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

public class YoloOBBViewModel : YoloDetectViewModel
{
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
                OperateResult operateResult = await YoloInit(OnnxType.ObbDetection).RunAsync(new ObbDetectionData
                {
                    Confidence = Confidence,
                    Iou = Iou,
                    File = image.Encode().ToArray()
                });
                List<OBBDetection> results = operateResult.GetOBBDetectionResult().ToObbDetection();
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
                using SKBitmap resultImage = image.Draw(results);
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
