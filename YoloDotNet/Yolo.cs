// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet
{
    /// <summary>
    /// Initializes a new instance of YoloDotNet.
    /// </summary>
    /// <param name="options">Options for initializing the YoloDotNet model.</param>
    public class Yolo(YoloOptions options) : IDisposable
    {
        #region Private fields
        private readonly IModule _detection = ModuleFactory.CreateModule(options);
        private FFmpegService? _ffmpegService;
        private readonly object _videoLock = new();
        private int _disposed;
        #endregion

        #region Public Fields
        /// <summary>Gets metadata for the loaded ONNX model.</summary>
        public OnnxModel OnnxModel => _detection.OnnxModel;
        /// <summary>Callback invoked when a decoded video frame is ready.</summary>
        public Action<SKBitmap, long>? OnVideoFrameReceived;

        /// <summary>Callback invoked when the video stream reaches its end.</summary>
        public Action? OnVideoEnd;
        #endregion

        #region Semantic Segmentation

        /// <summary>Runs dense semantic segmentation on a bitmap.</summary>
        /// <param name="img">The source bitmap.</param>
        /// <param name="roi">An optional region of interest.</param>
        /// <returns>A class map aligned to the processed source region.</returns>
        public SemanticSegmentation RunSemanticSegmentation(SKBitmap img, SKRectI? roi = null)
            => ((ISemanticSegmentationModule)_detection).ProcessImage(img, roi);

        /// <summary>Runs dense semantic segmentation on an image.</summary>
        /// <param name="img">The source image.</param>
        /// <param name="roi">An optional region of interest.</param>
        /// <returns>A class map aligned to the processed source region.</returns>
        public SemanticSegmentation RunSemanticSegmentation(SKImage img, SKRectI? roi = null)
            => ((ISemanticSegmentationModule)_detection).ProcessImage(img, roi);

        #endregion

        #region Depth Estimation

        /// <summary>Runs monocular depth estimation on a bitmap.</summary>
        /// <param name="img">The source bitmap.</param>
        /// <param name="roi">An optional region of interest.</param>
        /// <returns>A depth map aligned to the processed source region.</returns>
        public DepthEstimation RunDepthEstimation(SKBitmap img, SKRectI? roi = null)
            => ((IDepthEstimationModule)_detection).ProcessImage(img, roi);

        /// <summary>Runs monocular depth estimation on an image.</summary>
        /// <param name="img">The source image.</param>
        /// <param name="roi">An optional region of interest.</param>
        /// <returns>A depth map aligned to the processed source region.</returns>
        public DepthEstimation RunDepthEstimation(SKImage img, SKRectI? roi = null)
            => ((IDepthEstimationModule)_detection).ProcessImage(img, roi);

        #endregion

        #region Classification

        /// <summary>
        /// Run image classification on an Image.
        /// </summary>
        /// <param name="img">The SKBitmap to classify.</param>
        /// <param name="classes">The number of classes to return (default is 1).</param>
        /// <returns>A list of classification results.</returns>
        public List<Classification> RunClassification(SKBitmap img, int classes = 1)
            => ((IClassificationModule)_detection).ProcessImage(img, classes, 0, 0);

        /// <summary>
        /// Run image classification on an Image.
        /// </summary>
        /// <param name="img">The SKImage to classify.</param>
        /// <param name="classes">The number of classes to return (default is 1).</param>
        /// <returns>A list of classification results.</returns>
        public List<Classification> RunClassification(SKImage img, int classes = 1)
            => ((IClassificationModule)_detection).ProcessImage(img, classes, 0, 0);

        #endregion

        #region Object Detection

        /// <summary>
        /// Run object detection on an Image.
        /// </summary>
        /// <param name="img">The SKBitmap to obb detect.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of classification results.</returns>
        public List<ObjectDetection> RunObjectDetection(SKBitmap img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
            => ((IObjectDetectionModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        /// <summary>
        /// Run object detection on an Image.
        /// </summary>
        /// <param name="img">The SKImage to obb detect.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of classification results.</returns>
        public List<ObjectDetection> RunObjectDetection(SKImage img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
             => ((IObjectDetectionModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        #endregion

        #region OBB (Oriented Bounding Box)

        /// <summary>
        /// Run oriented bounding bBox detection on an image.
        /// </summary>
        /// <param name="img">The SKBitmap to obb detect.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<OBBDetection> RunObbDetection(SKBitmap img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
            => ((IOBBDetectionModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        /// <summary>
        /// Run oriented bounding bBox detection on an image.
        /// </summary>
        /// <param name="img">The SKImage to obb detect.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<OBBDetection> RunObbDetection(SKImage img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
            => ((IOBBDetectionModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        #endregion

        #region Segmentation

        /// <summary>
        /// Run segmentation on an image.
        /// </summary>
        /// <param name="img">The SKBitmap to segmentate.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="pixelConfedence">The mask pixel confidence threshold (default is 0.65).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<Segmentation> RunSegmentation(SKBitmap img, double confidence = 0.2, double pixelConfedence = 0.65, double iou = 0.7, SKRectI? roi = null)
            => ((ISegmentationModule)_detection).ProcessImage(img, confidence, pixelConfedence, iou, roi);

        /// <summary>
        /// Run segmentation on an image.
        /// </summary>
        /// <param name="img">The SKImage to segmentate.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="pixelConfedence">The mask pixel confidence threshold (default is 0.65).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<Segmentation> RunSegmentation(SKImage img, double confidence = 0.2, double pixelConfedence = 0.65, double iou = 0.7, SKRectI? roi = null)
            => ((ISegmentationModule)_detection).ProcessImage(img, confidence, pixelConfedence, iou, roi);

        #endregion

        #region Pose Estimation

        /// <summary>
        /// Run pose estimation on an image.
        /// </summary>
        /// <param name="img">The SKBitmap to pose estimate.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<PoseEstimation> RunPoseEstimation(SKBitmap img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
            => ((IPoseEstimationModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        /// <summary>
        /// Run pose estimation on an image.
        /// </summary>
        /// <param name="img">The SKImage to pose estimate.</param>
        /// <param name="confidence">The confidence threshold for detected objects (default is 0.2).</param>
        /// <param name="iou">IoU (Intersection Over Union) overlap threshold value for removing overlapping bounding boxes (default: 0.7).</param>
        /// <param name="roi">Optional region of interest within the image to focus detection on.</param>
        /// <returns>A list of Segmentation results.</returns>
        public List<PoseEstimation> RunPoseEstimation(SKImage img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)
            => ((IPoseEstimationModule)_detection).ProcessImage(img, confidence, 0, iou, roi);

        #endregion

        #region Video

        /// <summary>
        /// Initializes the video stream using the specified <see cref="VideoOptions"/> and sets up event handlers for frame processing and video completion.
        /// </summary>
        /// <param name="videoOptions"></param>
        public void InitializeVideo(VideoOptions videoOptions)
        {
            ArgumentNullException.ThrowIfNull(videoOptions);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var replacement = new FFmpegService(videoOptions, options)
            {
                OnFrameReady = (frame, frameIndex) => OnVideoFrameReceived?.Invoke(frame, frameIndex),
                OnVideoEnd = () => OnVideoEnd?.Invoke()
            };

            lock (_videoLock)
            {
                _ffmpegService?.Dispose();
                _ffmpegService = replacement;
            }
        }

        /// <summary>
        /// Retrieves a list of available video input devices detected on the current system.
        /// </summary>
        /// <exception cref="YoloDotNetVideoException"></exception>
        public static List<string> GetVideoDevices()
            => FFmpegService.GetVideoDevicesOnSystem() ?? throw new YoloDotNetVideoException(
                "No video initialized. Please call InitializeVideo() before attempting to retrieve metadata.");

        /// <summary>
        /// Retrieves metadata about the stream or initialized video, such as duration, frame rate, and resolution.
        /// </summary>
        /// <exception cref="YoloDotNetVideoException"></exception>
        public VideoMetadata GetVideoMetaData()
            => GetVideoService().VideoMetadata ?? throw new YoloDotNetVideoException(
                "No video initialized. Please call InitializeVideo() before attempting to retrieve metadata.");

        /// <summary>
        /// Starts decoding and processing video frames from the initialized video stream.
        /// </summary>
        public void StartVideoProcessing()
            => GetVideoService().Start();

        /// <summary>
        /// Stops video frame processing and releases resources associated with the video stream.
        /// </summary>
        public void StopVideoProcessing()
            => GetVideoService().Stop();

        /// <summary>Gets the configured video service or throws a domain-specific initialization error.</summary>
        private FFmpegService GetVideoService()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lock (_videoLock)
                return _ffmpegService ?? throw new YoloDotNetVideoException("No video initialized. Please call InitializeVideo() first.");
        }

        #endregion

        #region Model Info

        /// <summary>
        /// Gets a description of the currently loaded YOLO model,
        /// including the model type and version. Returns "No model loaded"
        /// if no model has been initialized.
        /// </summary>
        public string ModelInfo =>
            _detection.OnnxModel == null
                ? "No model loaded"
                : $"{_detection.OnnxModel.ModelType} (yolo {_detection.OnnxModel.ModelVersion.ToString().ToLower()})";

        #endregion

        #region Dispose

        /// <summary>Releases the processing module and any initialized video service.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _detection.Dispose();

            lock (_videoLock)
            {
                _ffmpegService?.Dispose();
                _ffmpegService = null;
            }

            options.ExecutionProvider?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
