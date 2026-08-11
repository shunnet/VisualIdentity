<h1 align="center">🔍 Snet.VisualIdentity</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/><br/>
</p>

<p align="center">
  <b>A .NET 10-based multi-model intelligent vision platform powered by YOLO</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/nuget/v/Snet.Yolo.Server?color=blue"/>
  <img src="https://img.shields.io/github/stars/shunnet/VisualIdentity?style=social"/>
</p>

<p align="center">
  🚀 Efficient · 🧩 Flexible · 📦 Easy to deploy · 🔒 Secure
</p>

<p align="center">
  <a href="https://snet.cn"><b>🌐 Website</b></a> ·
  <a href="https://github.com/shunnet/VisualIdentity"><b>📦 GitHub</b></a> ·
  <a href="https://snet.cn/EaiUj"><b>🎬 Demo</b></a> ·
  <a href="https://www.nuget.org/packages/Snet.Yolo.Server"><b>📦 NuGet</b></a>
</p>

<p align="center">
  English | 📖 <a href="README.md"><b>简体中文</b></a>
</p>

## 🌟 Introduction

**VisualIdentity** is a ready-to-use intelligent recognition platform combining modern **.NET**, the high-performance inference engine [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) and lightweight **SQLite** data management. It solves the pain point of "multi-model deployment + multi-task recognition" — **detection, classification, segmentation, pose estimation and oriented detection** are managed uniformly and switchable on demand.

> 💡 The `.NET` badge: the core library `Snet.Yolo.Server` multi-targets **net8.0 / net10.0**; the API services and tools are built on **.NET 10**.

### ✨ Core Features

| Feature | Description |
|---------|-------------|
| 🧠 **Multi-Model Management** | SQLite-based model CRUD with versioning and quick switching |
| 🎯 **Five-in-One Recognition** | Object detection · OBB · classification · segmentation · pose estimation |
| ⚡ **Multi-Hardware Acceleration** | CPU · CUDA / TensorRT · OpenVINO · CoreML · DirectML |
| 🌍 **Cross-Platform** | Windows · Linux · macOS · Docker |
| 🔒 **Production-Grade Security** | CSRF protection · rate limiting · CORS control · security headers |
| 📊 **Real-Time Metrics** | Millisecond latency stats, batch validation & confidence analysis |
| 🖥️ **WPF Debug Tool** | Visual verification for 5 recognition modes + data unification tool |
| 🐍 **Python Helper** | Built-in export script, one-click PyTorch → ONNX |

## 🎯 Use Cases

| Scenario | Purpose | Recommended Models |
|----------|---------|--------------------|
| 🏭 **Industrial QC** | Defect detection, foreign-object recognition, part counting | Detection, Segmentation |
| 🛒 **Retail Analytics** | Customer behavior tracking, shelf product detection | Detection, Classification |
| 🛡️ **Smart Security** | Anomaly monitoring, fall detection, zone intrusion | Pose, Detection |
| 🚗 **Autonomous Driving** | Road target detection, traffic sign recognition | OBB, Detection |
| 🏥 **Medical Imaging** | Lesion segmentation, cell classification | Segmentation, Classification |
| 📄 **Document Analysis** | Rotated text detection, table recognition | OBB |
| 🌐 **Edge Computing** | Raspberry Pi / Jetson lightweight deployment | CPU, OpenVINO |

## 🏗️ Architecture

```
VisualIdentity/
├── Snet.Yolo.Server/              # 🧠 Core inference engine + data models (net8.0/net10.0)
├── Snet.Yolo.Api.Shared/          # 🔗 Shared API layer (Shared Project: controllers / security / imaging)
├── Snet.Yolo.Api.Cpu/             # 🖥️ CPU API (HTTP 5157 · HTTPS 7257)
├── Snet.Yolo.Api.Cuda/            # 🎮 CUDA / TensorRT API (HTTP 5158 · HTTPS 7258)
├── Snet.Yolo.Api.OpenVino/        # 🔌 OpenVINO API (HTTP 5159 · HTTPS 7259)
├── Snet.Yolo.Api.CoreML/          # 🍎 CoreML API (HTTP 5160 · HTTPS 7260)
├── Snet.Yolo.Api.DirectML/        # 🪟 DirectML API (HTTP 5161 · HTTPS 7261)
├── Snet.Yolo.Tool/                # 🛠️ WPF desktop debug tool
├── Snet.Yolo.Test/                # 🧪 Integration tests (console)
├── Snet.Py/                       # 🐍 Python model export scripts
└── appsettings.json               # ⚙️ Global configuration
```

### 🔄 Data Flow

```
Client uploads image → API controller (validation + CSRF check) → rate-limit middleware
→ ManageOperate (query model path) → IdentityOperate (load model + accelerator)
→ YoloDotNet inference (GPU / CPU) → ResultHandler (result conversion)
→ ImageHandler (annotated drawing + disk storage) → JSON result + image URL
```

## ⚡ Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- At least one YOLO model in ONNX format ([export](#-onnx-model-export))

### 1️⃣ Clone

```bash
git clone https://github.com/shunnet/VisualIdentity.git
cd VisualIdentity
```

### 2️⃣ Run the CPU API

```bash
cd Snet.Yolo.Api.Cpu
dotnet run
```

Open `http://localhost:5157/swagger` for Swagger UI (Development environment only).

### 3️⃣ Upload a Model & Infer

```bash
# 1. Upload ONNX model
curl -X POST http://localhost:5157/Operate/AddAsync \
  -F "file=@your_model.onnx" \
  -F "describe=my detection model" \
  -F "onnxType=ObjectDetection"

# 2. Fast inference (coordinates / labels / confidence only)
curl -X POST http://localhost:5157/Operate/IdentityAsync \
  -F "onnxIndex=1" -F "file=@test.jpg" \
  -F 'paramJson={"Confidence":0.2,"Iou":0.7}'

# 3. Full inference (annotated image + coordinates + image URL)
curl -X POST http://localhost:5157/Operate/IdentityDrawAsync \
  -F "onnxIndex=1" -F "file=@test.jpg" \
  -F 'paramJson={"Confidence":0.2,"Iou":0.7}'
```

## 📦 NuGet Installation

Use the core library in your own .NET project:

```bash
# Core inference library (required)
dotnet add package Snet.Yolo.Server

# Pick exactly ONE execution provider (⚠️ only one allowed)
dotnet add package YoloDotNet.ExecutionProvider.Cpu      # 🖥️ Generic CPU
dotnet add package YoloDotNet.ExecutionProvider.Cuda     # 🎮 NVIDIA GPU + TensorRT
dotnet add package YoloDotNet.ExecutionProvider.OpenVino # 🔌 Intel OpenVINO
dotnet add package YoloDotNet.ExecutionProvider.CoreML   # 🍎 Apple Silicon
dotnet add package YoloDotNet.ExecutionProvider.DirectML # 🪟 Windows GPU
```

### 💡 C# Example

```csharp
using SkiaSharp;
using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

// Create an inference instance (cached automatically, reused while config is unchanged)
var identity = IdentityOperate.Instance(new IdentityData
{
    Hardware = new CpuExecutionProvider("/path/to/model.onnx"),
    IdentifyType = OnnxType.ObjectDetection,
    SN = "my-detector"
});

using SKImage image = SKImage.FromEncodedData("/path/to/image.jpg");

// Run inference
OperateResult result = await identity.RunAsync(new ObjectDetectionData
{
    Confidence = 0.23,  // confidence threshold
    Iou = 0.7,          // IoU threshold
    File = image.Encode().ToArray()
});

// Get results and draw bounding boxes
var detections = result.GetObjectDetectionResult()?.ToObjectDetection();
if (detections is { Count: > 0 })
{
    foreach (var d in detections)
        Console.WriteLine($"{d.Label.Name}: {d.Confidence:P1} @ {d.BoundingBox}");

    using SKBitmap annotated = image.Draw(detections);
    // Save or display annotated...
}

identity.Dispose(); // release GPU resources
```

## 🔌 API Reference

### 📋 Model Management

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `POST` | `/Operate/AddAsync` | Upload ONNX model file | CSRF Token |
| `POST` | `/Operate/UpdateAsync` | Update model description or type | CSRF Token |
| `POST` | `/Operate/DeleteAsync` | Delete model (optionally the file) | CSRF Token |
| `GET` | `/Operate/QueryAsync?index=1` | Query a specific model | None |
| `GET` | `/Operate/QueryAllAsync` | Query all models | None |

### 🧠 Inference

| Method | Path | Description | Returns |
|--------|------|-------------|---------|
| `POST` | `/Operate/IdentityAsync` | 🚀 Fast inference | Coordinates / labels / confidence only |
| `POST` | `/Operate/IdentityDrawAsync` | 🎨 Full inference | Coordinates + annotated image URL + original URL |

> 📌 Inference endpoints are **POST multipart/form-data** (`onnxIndex`, `file`, `paramJson` are form fields). Hardware-specific fields below are also sent as form fields:

| Hardware | Extra Fields |
|----------|--------------|
| 🎮 CUDA | `gpuid` (GPU ID), `trtConfig` (TensorRT config) |
| 🍎 CoreML | `adaptive` (adaptive mode, default `true`) |
| 🪟 DirectML | `gpuid` (GPU ID) |
| 🔌 OpenVINO | `openVino` (advanced config) |

### 🖼️ History Images

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/Operate/GetOriginalImage?name=xxx&type=ObjectDetection` | Original image |
| `GET` | `/Operate/GetMarkImage?name=xxx&type=ObjectDetection` | Annotated image |
| `GET` | `/Operate/GetImageDetails?name=xxx&type=ObjectDetection` | Full details (original + annotated + coordinates JSON) |

### 🏥 Health Check

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Health check (`{"Status":"Healthy","Timestamp":"..."}`) |

### `paramJson` Formats

| Task Type | JSON Format |
|-----------|-------------|
| Object Detection | `{"Confidence":0.2,"Iou":0.7}` |
| Oriented Detection | `{"Confidence":0.2,"Iou":0.7}` |
| Classification | `{"Classes":1}` |
| Pose Estimation | `{"Confidence":0.2,"Iou":0.7}` |
| Segmentation | `{"Confidence":0.2,"Iou":0.7,"PixelConfidence":0.65}` |

## ⚙️ Configuration

### `appsettings.json`

```json
{
  "AllowedOrigins": [],           // 🔒 CORS whitelist; empty array = reject all cross-origin
  "RateLimit": {
    "PermitLimit": 120,           // ⏱️ max requests per minute
    "WindowMinutes": 1,           // ⏱️ time window (minutes)
    "QueueLimit": 20              // ⏱️ max queue size after the limit
  },
  "ConfigModel": {
    "NameFormat": "yyyyMMddHHmmssffffff",              // 🏷️ filename time format
    "OriginalImageNamingFormat": "{0}-Original.jpeg",  // 🖼️ original image naming
    "ResultImageNamingFormat": "{0}-Result.jpeg",      // 🎨 annotated image naming
    "DetailsNamingFormat": "{0}-Details.ini",          // 📄 details file naming
    "RetentionDays": 30                                // 🗑️ history retention days
  }
}
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment (`Development` / `Production`) | `Production` |
| `ASPNETCORE_URLS` | Listen address | `http://localhost:5157` |

> ⚠️ Swagger UI is enabled only in `Development`; it is disabled automatically in production.

## 🧠 Supported Tasks

| Classification | Detection | OBB | Segmentation | Pose |
|:---:|:---:|:---:|:---:|:---:|
| 🔖 Whole-image classification | 📦 Bounding boxes | 🔄 Rotated boxes | 🎭 Pixel-level masks | 🦴 Keypoints |
| Labels + confidence | Boxes + labels + confidence | Rotated boxes + angle | Masks + boxes + labels | Keypoints + boxes |

### 🦴 Pose Estimation — Built-in Fall Detection

`YoloPoseViewModel` integrates a real-time **fall detection algorithm** (`FallDetector`) analyzing 17 human keypoints across multiple dimensions:

| Dimension | Criterion | Configurable |
|-----------|-----------|--------------|
| 📏 Body height | Nose-ankle distance < 50% image height | `FlatHeightRatio` |
| 📐 Body tilt | Shoulder-hip line < 70° | `AngleThreshold` |
| ↔️ Torso levelness | Shoulder-hip Y delta < 10% image height | `TorsoHorizontalThresholdRatio` |
| 📍 Ground proximity | Mean keypoint Y > 60% image height | `GroundProximityRatio` |
| ✅ Final verdict | ≥ 2 criteria met → fall | `FallScoreThreshold` |

## ✅ Verified YOLO Models

The following YOLO models have been fully inference-tested with **YoloDotNet** and **Snet.Yolo.Server**:

| Classification | Detection | Segmentation | Pose | OBB |
|:---:|:---:|:---:|:---:|:---:|
| YOLOv8-cls | YOLOv5u | YOLOv8-seg | YOLOv8-pose | YOLOv8-obb |
| YOLOv11-cls | YOLOv8 | YOLOv11-seg | YOLOv11-pose | YOLOv11-obb |
| YOLOv12-cls | YOLOv9 | YOLOv12-seg | YOLOv12-pose | YOLOv12-obb |
| YOLOv26-cls | YOLOv10 | YOLOv26-seg | YOLOv26-pose | YOLOv26-obb |
| | YOLOv11 | | | |
| | YOLOv12 | | | |
| | YOLOv26 | | | |
| | YOLO-World (v2) | | | |
| | YOLO-E | | | |
| | RT-DETR | | | |

## 🖥️ Execution Providers

| Provider | Windows | Linux | macOS | Docker | Use Cases |
|----------|:---:|:---:|:---:|:---:|-----------|
| 🖥️ **CPU** | ✅ | ✅ | ✅ | ✅ | Generic inference, edge devices |
| 🎮 **CUDA / TensorRT** | ✅ | ✅ | ❌ | ✅ | NVIDIA GPU acceleration |
| 🔌 **OpenVINO** | ✅ | ✅ | ❌ | ✅ | Intel chip optimization |
| 🍎 **CoreML** | ❌ | ❌ | ✅ | ❌ | Apple Silicon (M1/M2/M3) |
| 🪟 **DirectML** | ✅ | ❌ | ❌ | ❌ | Generic Windows GPU |

> ⚠️ Each project/process may reference **exactly one** execution provider package. Mixing providers causes runtime conflicts (duplicate DLL loading, symbol clashes).

## 💡 ONNX Model Export

### Via Python (Ultralytics)

```bash
pip install ultralytics
python Snet.Py/Snet.Py.py
```

### Manual Export

```bash
# YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

> 📌 Using the correct opset ensures best compatibility and inference performance with ONNX Runtime.

## 🐳 Docker Deployment

### Build Images

```bash
# CPU
docker build -t snet-yolo-cpu -f Snet.Yolo.Api.Cpu/Dockerfile .

# CUDA (requires NVIDIA Container Toolkit)
docker build -t snet-yolo-cuda -f Snet.Yolo.Api.Cuda/Dockerfile .

# OpenVINO
docker build -t snet-yolo-openvino -f Snet.Yolo.Api.OpenVino/Dockerfile .
```

### Run a Container

```bash
docker run -d -p 8080:8080 \
  -v /path/to/models:/app/wwwroot/onnxs \
  -v /path/to/data:/app/wwwroot \
  snet-yolo-cpu

curl http://localhost:8080/health   # health check
```

> 📝 The container exposes `8080` (and `8081` internally); CoreML (macOS only) and DirectML (Windows only) do not support Docker — run them directly on the target OS.

## 🧪 Testing

```bash
cd Snet.Yolo.Test
# Set env vars, then run the console integration tests
export YOLO_IMAGE_PATH="/path/to/test.jpg"
export YOLO_MODEL_PATH="/path/to/model.onnx"
export YOLO_TYPE="ObjectDetection"
dotnet run
```

## 🔒 Security Features

| Feature | Implementation | Configuration |
|---------|----------------|---------------|
| 🌐 **CORS** | `RestrictedOrigins` policy | `appsettings.json` → `AllowedOrigins` |
| 🛡️ **CSRF** | `[ValidateAntiForgeryToken]` filter | All state-changing POST endpoints |
| ⏱️ **Rate Limiting** | Fixed window algorithm | `RateLimit` section |
| 🔐 **Security Headers** | Middleware injection | X-Content-Type-Options / X-Frame-Options / CSP, etc. |
| 📁 **Filename Sanitization** | Path traversal filtering + GUID uniqueness | Upload handling |
| 🔑 **Secret Management** | User Secrets (dev) + env vars (prod) | Per-project `UserSecretsId` |
| 📏 **File Size Limit** | Kestrel + FormOptions dual limit | 1 GB request body cap |
| 🧹 **Auto Cleanup** | `HistoryFileHandler` scheduled task | `RetentionDays` (default 30) |

## 📈 Performance

| Optimization | Description |
|--------------|-------------|
| 🔄 **Model Instance Caching** | Reuse instances while config is unchanged |
| 🧵 **Async End-to-End** | `async/await` across HTTP → GPU → disk |
| 🖼️ **Parallel Disk Writes** | Original / annotated / details via `Task.WhenAll` |
| 💾 **Memory Optimization** | `SKBitmap.Freeze()` cross-thread sharing, `using`-guaranteed dispose |

### Latency Breakdown (reference, CPU mode)

```
HTTP receive      ~   5ms
Image decode      ~  20ms
ONNX inference    ~ 150ms (model & hardware dependent)
Result conversion ~   5ms
Annotation draw   ~  30ms (IdentityDraw only)
Disk write        ~  10ms (parallel, non-blocking)
────────────────────────
Fast total        ~ 180ms
Full total        ~ 220ms
```

## 📚 Dependencies

| Component | Description |
|-----------|-------------|
| 🔗 **Snet.DB** | Dual ORM (Dapper & SqlSugarCore), auto table creation, Code-First |
| ⚡ **YoloDotNet** | Ultra-fast production-grade YOLO inference, YOLOv5u → YOLOv26 |
| 🎨 **SkiaSharp** | Cross-platform 2D rendering: decode, annotation, keypoints |
| 🗄️ **SQLite** | Embedded database: model metadata management |

## 🙏 Acknowledgements

| Project | Description |
|---------|-------------|
| 🌐 [Snet.cn](https://snet.cn) | Official website |
| 🔥 [Ultralytics](https://github.com/ultralytics/ultralytics) | YOLO training & export |
| ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) | .NET YOLO inference engine |
| 🖥️ [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) | Modern WPF UI framework |
| 🗄️ [SqlSugarCore](https://github.com/DotNetNext/SqlSugar) | ORM framework |
| 🎨 [SkiaSharp](https://github.com/mono/SkiaSharp) | Cross-platform graphics |

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

This project is licensed under the **MIT** License — free to use, modify and distribute.

📄 See the [LICENSE](LICENSE) file for the full terms.

> ⚠️ The software is provided "as is", without warranty of any kind.

## 📈 Star History

<a href="https://www.star-history.com/?repos=shunnet%2FVisualIdentity&type=date&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&theme=dark&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
 </picture>
</a>
