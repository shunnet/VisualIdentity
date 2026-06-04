using SkiaSharp;
using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;

namespace Snet.Yolo.Test
{
    /// <summary>
    /// Integration test runner for Yolo inference.
    /// Set environment variables to configure:
    ///   YOLO_IMAGE_PATH — path to test image (e.g. "C:\test\image.jpg")
    ///   YOLO_MODEL_PATH — path to ONNX model file
    ///   YOLO_TYPE — OnnxType value (default: ObjectDetection)
    /// </summary>
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var imagePath = Environment.GetEnvironmentVariable("YOLO_IMAGE_PATH") ?? string.Empty;
            var onnxModel = Environment.GetEnvironmentVariable("YOLO_MODEL_PATH") ?? string.Empty;
            var onnxTypeStr = Environment.GetEnvironmentVariable("YOLO_TYPE") ?? nameof(OnnxType.ObjectDetection);

            if (!Enum.TryParse<OnnxType>(onnxTypeStr, out var onnxType))
            {
                Console.Error.WriteLine($"Invalid YOLO_TYPE: {onnxTypeStr}");
                return;
            }

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                Console.Error.WriteLine("Set YOLO_IMAGE_PATH to a valid image file path.");
                return;
            }
            if (string.IsNullOrEmpty(onnxModel) || !File.Exists(onnxModel))
            {
                Console.Error.WriteLine("Set YOLO_MODEL_PATH to a valid ONNX model file path.");
                return;
            }

            Console.WriteLine($"Running inference — Image: {imagePath}, Model: {onnxModel}, Type: {onnxType}");

            try
            {
                using SKImage image = SKImage.FromEncodedData(imagePath);

                OperateResult operateResult = await IdentityOperate.Instance(new IdentityData
                {
                    Hardware = new CpuExecutionProvider(onnxModel),
                    IdentifyType = onnxType,
                    SN = $"{onnxType}{onnxModel}"
                }).RunAsync(new ObjectDetectionData
                {
                    Confidence = 0.23,
                    Iou = 0.7,
                    File = image.Encode().ToArray()
                });

                var results = operateResult.GetObjectDetectionResult();
                if (results is { Count: > 0 })
                {
                    var detections = results.ToObjectDetection();
                    Console.WriteLine($"Detected {detections.Count} object(s):");
                    foreach (var d in detections)
                    {
                        Console.WriteLine($"  - {d.Label.Name} (confidence: {d.Confidence:P1}, box: {d.BoundingBox})");
                    }

                    using SKBitmap resultImage = image.Draw(detections);
                    Console.WriteLine("Result image rendered successfully.");
                }
                else
                {
                    Console.WriteLine("No objects detected.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Inference failed: {ex}");
            }
        }
    }
}
