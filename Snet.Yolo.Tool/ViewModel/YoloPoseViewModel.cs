using SkiaSharp;
using Snet.Core.handler;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tool.Data;
using System.IO;
using System.Text.Json.Serialization;
using Snet.Yolo.Server.yolo.Extensions;
using Snet.Yolo.Server.yolo.Models;

namespace Snet.Yolo.Tool.ViewModel;

public class YoloPoseViewModel : YoloDetectViewModel
{
    FallDetector fallDetector = new FallDetector();



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
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                OperateResult operateResult = await RunIdentityAsync(OnnxType.PoseEstimation, new PoseEstimationData
                {
                    Confidence = Confidence,
                    Iou = Iou,
                    File = image.Encode().ToArray()
                }, token);
                List<PoseEstimation> results = operateResult.GetPoseEstimationResult()?.ToPoseEstimation() ?? new List<PoseEstimation>();
                List<PoseEstimation> newResults = new List<PoseEstimation>();
                string msg = $"\r\n{App.LanguageOperate.GetLanguageValue("验证")} : {Path.GetFileName(item.Path)}\r\n{App.LanguageOperate.GetLanguageValue("大小")} : {item.Description}\r\n{App.LanguageOperate.GetLanguageValue("用时")} : {stopwatch.ElapsedMilliseconds} ms";
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
                        bool fall = fallDetector.IsPersonFallen(FallDetector.ToBodyModelList(results[i].KeyPoints), image.Height);
                        msg += $"\r\n<{i + 1}> 状态 : {(fall ? "跌倒" : "未跌倒")}";
                        if (fall)
                        {
                            newResults.Add(results[i]);
                        }
                    }
                }
                msg += $"\r\n-------------------------------------------------------------------";
                using SKBitmap resultImage = image.Draw(results, new PoseDrawingOptions { KeyPointMarkers = new PoseEstimationCustomKeyPointColorHandler().GetKeyPoints(), PoseConfidence = Confidence, BorderThickness = 3 });   //绘制所有
                //using SKBitmap resultImage = image.Draw(newResults, new PoseDrawingOptions { KeyPointMarkers = CustomKeyPointColorMap.KeyPoints, PoseConfidence = Confidence });  //只绘制跌倒
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
