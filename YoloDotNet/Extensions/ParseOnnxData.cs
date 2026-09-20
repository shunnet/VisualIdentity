// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Extensions
{
    /// <summary>Parses runtime ONNX metadata into YoloDotNet model metadata.</summary>
    public static class ParseOnnxData
    {
        /// <summary>Parses inputs, outputs, labels, type, task, and version from an ONNX session.</summary>
        /// <param name="onnxData">An ONNX Runtime session or compatible metadata source.</param>
        /// <returns>The validated model metadata.</returns>
        public static OnnxModel ParseOnnx(this object onnxData)
        {
            var inputs = GetShape<long>(onnxData, "InputMetadata");
            var outputs = GetShape<int>(onnxData, "OutputMetadata");
            if (inputs.Count != 1)
                throw new YoloDotNetModelException($"YOLO models must define exactly one image input; found {inputs.Count}.");

            var metadata = GetMetadata(onnxData);
            var model = new OnnxModel
            {
                InputShapes = inputs,
                OutputShapes = outputs,
                CustomMetaData = metadata,
                ModelDataType = GetModelDataType(onnxData),
                ModelType = GetModelType(GetRequiredMetadata(metadata, "task")),
                ModelVersion = GetModelVersion(GetRequiredMetadata(metadata, "description")),
                Labels = MapLabelsAndColors(GetRequiredMetadata(metadata, "names")),
                InputShapeSize = CalculateTotalInputShapeSize(inputs.Single().Value)
            };

            return model;
        }

        #region Helper Methods
        /// <summary>
        /// Retrieves the shape dimensions for each input or output from an ONNX model property and returns them as a dictionary.
        /// </summary>
        private static Dictionary<string, T[]> GetShape<T>(object onnxData, string propertyName)
        {
            try
            {
                var metadataProperty = onnxData.GetType().GetProperty(propertyName)
                    ?? throw new YoloDotNetModelException($"{propertyName} property not found on ONNX model.");

                var metadata = metadataProperty.GetValue(onnxData)
                    ?? throw new YoloDotNetModelException($"{propertyName} value is null.");

                var shapes = new Dictionary<string, T[]>();

                if (metadata is not System.Collections.IEnumerable metadataItems)
                    throw new YoloDotNetModelException($"{propertyName} is not enumerable.");

                foreach (var item in metadataItems)
                {
                    if (item is null)
                        continue;

                    var tensorName = GetRequiredPropertyValue(item, "Key")?.ToString()
                        ?? throw new YoloDotNetModelException("Tensor name is null.");
                    var valueData = GetRequiredPropertyValue(item, "Value")
                        ?? throw new YoloDotNetModelException("Tensor metadata is null.");
                    var dimensions = GetRequiredPropertyValue(valueData, "Dimensions")
                        ?? throw new YoloDotNetModelException("Tensor dimensions are null.");

                    if (dimensions is not System.Collections.IEnumerable dimensionItems)
                        throw new YoloDotNetModelException("Tensor dimensions are not enumerable.");

                    var dimensionsList = new List<T>();

                    foreach (var dimension in dimensionItems)
                    {
                        var dim = (T)Convert.ChangeType(dimension, typeof(T));

                        dimensionsList.Add(dim);
                    }

                    shapes.Add(tensorName, [.. dimensionsList]);
                }

                return shapes;
            }
            catch (YoloDotNetModelException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new YoloDotNetModelException($"Failed to retrieve {propertyName} from ONNX model.", ex);
            }
        }

        /// <summary>
        /// Retrieves the custom metadata key-value pairs from the specified ONNX model data object.
        /// </summary>
        private static Dictionary<string, string> GetMetadata(object onnxData)
        {
            try
            {
                var modelMetadataProperty = onnxData.GetType().GetProperty("ModelMetadata")
                    ?? throw new InvalidOperationException("ModelMetadata property not found.");

                var modelMetadata = modelMetadataProperty.GetValue(onnxData)
                    ?? throw new InvalidOperationException("ModelMetadata value is null.");

                var customMetadataMapProperty = modelMetadata.GetType().GetProperty("CustomMetadataMap")
                    ?? throw new InvalidOperationException("CustomMetadataMap property not found.");

                var customMetadataMap = customMetadataMapProperty.GetValue(modelMetadata)
                    ?? throw new InvalidOperationException("CustomMetadataMap value is null.");

                var metadataCollection = new Dictionary<string, string>();

                if (customMetadataMap is not System.Collections.IEnumerable metadataItems)
                    return [];

                foreach (var item in metadataItems)
                {
                    if (item is null)
                        continue;

                    var key = GetRequiredPropertyValue(item, "Key")?.ToString();
                    var value = GetRequiredPropertyValue(item, "Value")?.ToString();
                    if (key is not null && value is not null)
                        metadataCollection.Add(key, value);
                }

                return metadataCollection;
            }
            catch (YoloDotNetModelException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new YoloDotNetModelException("Failed to read ONNX custom metadata.", ex);
            }
        }

        /// <summary>
        /// Determines the data type used by the ONNX model's input tensor.
        /// </summary>
        private static ModelDataType GetModelDataType(object onnxData)
        {
            try
            {
                var inputMetadataProperty = onnxData.GetType().GetProperty("InputMetadata")
                    ?? throw new YoloDotNetModelException("InputMetadata could not be retrieved from ONNX model.");

                var inputMetadata = inputMetadataProperty.GetValue(onnxData)
                    ?? throw new YoloDotNetModelException("InputMetadata value is null.");

                // Get the first input's element data type
                if (inputMetadata is not System.Collections.IEnumerable inputItems)
                    throw new YoloDotNetModelException("InputMetadata is not enumerable.");

                foreach (var item in inputItems)
                {
                    if (item is null)
                        continue;

                    var valueData = GetRequiredPropertyValue(item, "Value")
                        ?? throw new YoloDotNetModelException("Input metadata value is null.");
                    var elementDataType = GetRequiredPropertyValue(valueData, "ElementDataType")?.ToString();

                    // Check if the element data type is Float16
                    return elementDataType switch
                    {
                        "Float" => ModelDataType.Float,
                        "Float16" => ModelDataType.Float16,
                        _ => throw new YoloDotNetModelException($"Unsupported model input element type '{elementDataType}'.")
                    };
                }

                throw new YoloDotNetModelException("The ONNX model does not define an input tensor.");
            }
            catch (YoloDotNetModelException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new YoloDotNetModelException("Failed to retrieve model data type from ONNX model.", ex);
            }
        }

        /// <summary>
        /// Maps ONNX labels to corresponding colors for visualization.
        /// </summary>
        internal static LabelModel[] MapLabelsAndColors(string onnxLabelData)
        {
            if (string.IsNullOrWhiteSpace(onnxLabelData))
                throw new YoloDotNetModelException("The ONNX 'names' metadata is empty.");

            var matches = Regex.Matches(
                onnxLabelData,
                @"(?<id>\d+)\s*:\s*['""](?<name>.*?)['""]",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            if (matches.Count == 0)
                throw new YoloDotNetModelException("The ONNX 'names' metadata has an unsupported format.");

            var labels = matches
                .Select(match => new LabelModel
                {
                    Index = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture),
                    Name = match.Groups["name"].Value
                })
                .OrderBy(label => label.Index)
                .ToArray();

            if (labels.Select(label => label.Index).Distinct().Count() != labels.Length)
                throw new YoloDotNetModelException("The ONNX 'names' metadata contains duplicate label indexes.");
            if (labels.Where((label, position) => label.Index != position).Any())
                throw new YoloDotNetModelException("The ONNX 'names' metadata must contain contiguous indexes starting at zero.");

            return labels;
        }

        /// <summary>Reads a named public property from a reflected ONNX Runtime metadata object.</summary>
        private static object? GetRequiredPropertyValue(object source, string propertyName)
            => source.GetType().GetProperty(propertyName)?.GetValue(source)
                ?? throw new YoloDotNetModelException($"{propertyName} property is missing or null.");

        /// <summary>Gets a required metadata value or throws a model-specific validation error.</summary>
        private static string GetRequiredMetadata(IReadOnlyDictionary<string, string> metadata, string key)
            => metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new YoloDotNetModelException($"Required ONNX metadata '{key}' is missing or empty.");

        /// <summary>
        /// Calculates the total number of elements in a tensor based on its shape dimensions.
        /// </summary>
        internal static int CalculateTotalInputShapeSize(long[] shape)
        {
            if (shape.Length == 0)
                return 0;

            long shapeSize = 1;

            foreach (var dimension in shape)
            {
                if (dimension <= 0)
                    throw new YoloDotNetException(
                        $"All shape dimensions must be positive. Found invalid value: {dimension}",
                        nameof(shape));

                shapeSize = checked(shapeSize * dimension);
            }

            if (shapeSize > int.MaxValue)
                throw new YoloDotNetException("The input tensor contains too many elements.", nameof(shape));

            return (int)shapeSize;
        }

        internal static ModelType GetModelType(string modelType) => modelType.Trim().ToLowerInvariant() switch
        {
            "classify" => ModelType.Classification,
            "detect" => ModelType.ObjectDetection,
            "obb" => ModelType.ObbDetection,
            "pose" => ModelType.PoseEstimation,
            "segment" => ModelType.Segmentation,
            "semantic" => ModelType.SemanticSegmentation,
            "depth" => ModelType.DepthEstimation,
            _ => throw new YoloDotNetModelException($"Unsupported ONNX task '{modelType}'.")
        };

        /// <summary>
        /// Get ONNX model version
        /// </summary>
        internal static ModelVersion GetModelVersion(string modelDescription) => modelDescription.Trim().ToLowerInvariant() switch
        {
            // YOLO WorldV2 metadata commonly also starts with a regular YOLO version.
            var version when version.Contains("worldv2", StringComparison.Ordinal) => ModelVersion.WORLDV2,

            // YOLOv5
            var version when version.StartsWith("ultralytics yolov5") => ModelVersion.V5U,

            // YOLOv8
            var version when version.StartsWith("ultralytics yolov8") => ModelVersion.V8,
            var version when version.StartsWith("ultralytics yoloe-v8") => ModelVersion.V8E,

            // YOLOv9
            var version when version.StartsWith("ultralytics yolov9") => ModelVersion.V9,

            // YOLOv10
            var version when version.StartsWith("ultralytics yolov10") => ModelVersion.V10,

            // YOLOv11
            var version when version.StartsWith("ultralytics yolo11") => ModelVersion.V11,      // Note the missing v in Yolo11
            var version when version.StartsWith("ultralytics yoloe-11") => ModelVersion.V11E,   // Note the missing v in Yoloe-11

            // YOLOv12
            var version when version.StartsWith("ultralytics yolov12") => ModelVersion.V12,

            // YOLOv26
            var version when version.StartsWith("ultralytics yoloe-26") => ModelVersion.V26E,
            var version when version.StartsWith("ultralytics yolo26") => ModelVersion.V26,

            // RT-DETR
            var version when version.StartsWith("ultralytics rt-detr") => ModelVersion.RTDETR,

            // Fallback: if version metadata is missing, treat the model as YOLOv8.
            var version when version.StartsWith("ultralytics") && !version.Contains("yolo") => ModelVersion.V8,

            _ => throw new YoloDotNetModelException("Onnx model not supported!")
        };
        #endregion
    }
}
