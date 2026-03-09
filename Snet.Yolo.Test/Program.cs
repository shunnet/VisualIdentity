using SkiaSharp;
using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //????? 为对应数据

            // 原始图片路径
            string imagePath = "?????";

            //模型路径
            string onnxModel = "?????";

            //识别类型
            OnnxType onnxType = OnnxType.ObjectDetection;

            //直接调用库来进行本地识别操作
            using SKImage image2 = SKImage.FromEncodedData(imagePath);

            // 调用识别
            OperateResult operateResult = await IdentityOperate.Instance(new Yolo.Server.models.data.IdentityData
            {
                Hardware = new CpuExecutionProvider(onnxModel),
                IdentifyType = onnxType,
                SN = $"{onnxType}{onnxModel}"
            }).RunAsync(new ObjectDetectionData
            {
                Confidence = 0.23,
                Iou = 0.7,
                File = image2.Encode().ToArray()
            });

            // 转换结果
            List<ObjectDetection> results2 = operateResult.GetObjectDetectionResult().ToObjectDetection();

            //绘制结果
            using SKBitmap resultImage2 = image2.Draw(results2);

        }
    }
}
