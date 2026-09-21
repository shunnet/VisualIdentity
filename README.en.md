# VisualIdentity

[简体中文](README.md)

VisualIdentity is a .NET 10 YOLO vision solution built with ONNX Runtime, SkiaSharp, and Blazor Server. It contains a desktop tool, annotation/training sites, HTTP APIs, local execution providers, and automated tests.

## Current capabilities

- Ultralytics ONNX inference: object detection, instance segmentation, image classification, pose estimation, and oriented bounding boxes (OBB).
- Model families supported by the repository's YoloDotNet parsers: YOLOv5u through YOLO26, YOLO-World, and YOLO-E ONNX models.
- Backends: CPU and NVIDIA CUDA, with optional TensorRT. There are currently no DirectML, OpenVINO, or CoreML projects.
- Annotation: rectangles, polygons, brushes, keypoints, and classifications; YOLO and YOLO-with-images import/export.
- Training: Python virtual-environment setup, Ultralytics execution, metric reading, and `best.pt` discovery.
- Video: FFmpeg/FFprobe setup, bounded background processing, progress, cancellation, and rendered output.
- UI languages: Simplified Chinese and English.

> The API intentionally remains anonymous per the current product requirement. Request-size limits, fixed-window rate limiting, restricted CORS, decoded-image limits, and security headers still apply. Deploy it only on a trusted network or behind an access-controlling gateway.

## Solution layout

`VisualIdentity.sln` contains:

| Project | Purpose |
|---|---|
| `YoloDotNet` | Model metadata, preprocessing, postprocessing, and inference |
| `YoloDotNet.ExecutionProvider.Cpu` | CPU ONNX Runtime provider |
| `YoloDotNet.ExecutionProvider.Cuda` | CUDA/TensorRT provider |
| `Snet.Yolo.Server` | SQLite data access, model management, and inference services |
| `Snet.Yolo.Tool` | Windows WPF desktop tool using CPU |
| `Snet.Yolo.Api.Shared` | Shared CPU/CUDA API source |
| `Snet.Yolo.Api.Cpu` / `.Cuda` | CPU and CUDA/TensorRT HTTP APIs |
| `Snet.Yolo.Tasks.Core` | Annotation, editing, export, and training domain logic |
| `Snet.Yolo.Tasks.Shared` | Shared Blazor source |
| `Snet.Yolo.Tasks.Cpu` / `.Cuda` | CPU and CUDA annotation/training/validation sites |
| `Snet.Yolo.Test` | xUnit regression and integration tests |
| `Snet.Py` | Python helper project |

The `.shproj` projects contain shared source; they are not independently runnable or published products.

## Requirements

- .NET 10 SDK.
- WPF: Windows x64 or x86.
- CUDA: compatible NVIDIA driver, CUDA/cuDNN, and ONNX Runtime CUDA dependencies.
- Tasks training: Python 3, pip, and venv. Linux Tasks images include them and FFmpeg.
- Video processing: `ffmpeg` and `ffprobe`.

## Build and test

```powershell
dotnet restore VisualIdentity.sln
dotnet build VisualIdentity.sln -c Release
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-build
```

Tests and WPF reference the YoloDotNet and CPU provider projects in this repository instead of an older same-named NuGet package.

## Run

### WPF tool

```powershell
dotnet run --project Snet.Yolo.Tool/Snet.Yolo.Tool.csproj -c Release
```

### Tasks sites

```powershell
# CPU: https://localhost:7351 / http://localhost:5151
dotnet run --project Snet.Yolo.Tasks.Cpu/Snet.Yolo.Tasks.Cpu.csproj

# CUDA: https://localhost:7352 / http://localhost:5152
dotnet run --project Snet.Yolo.Tasks.Cuda/Snet.Yolo.Tasks.Cuda.csproj
```

Tasks uses cookie authentication. Set the bootstrap administrator password for a first deployment:

```powershell
$env:SNET_BOOTSTRAP_ADMIN_PASSWORD = "replace-with-a-strong-password"
```

Data and training artifacts are isolated by normalized username under `wwwroot/data`, `wwwroot/db`, and `train`.

### HTTP APIs

```powershell
# CPU: https://localhost:7257 / http://localhost:5157
dotnet run --project Snet.Yolo.Api.Cpu/Snet.Yolo.Api.Cpu.csproj

# CUDA: https://localhost:7258 / http://localhost:5158
dotnet run --project Snet.Yolo.Api.Cuda/Snet.Yolo.Api.Cuda.csproj
```

Swagger is enabled in Development. Main routes are under `/Operate/*`:

- `AddAsync`, `UpdateAsync`, `DeleteAsync`, `QueryAsync`, and `QueryAllAsync` manage models.
- `IdentityAsync` returns inference data.
- `IdentityDrawAsync` also stores JPEG originals, rendered images, and details.
- `GetOriginalImage`, `GetMarkImage`, and `GetImageDetails` retrieve history.
- `/health` returns service health.

Uploads use `multipart/form-data`. Inference sessions are reused by model, device, and TensorRT configuration.

The CUDA API does not accept arbitrary TensorRT filesystem paths. Engine caches are server-owned under `tensorrt-cache`; INT8 calibration files may only be selected by file name from `tensorrt-calibration`.

## Annotation, export, and training

Tasks infers the YOLO task from Label Studio-style XML:

| Control | YOLO task | Label format |
|---|---|---|
| `RectangleLabels` | detect | `class cx cy width height` |
| `RectangleLabels` with `yoloTask="obb"` | obb | `class x1 y1 x2 y2 x3 y3 x4 y4` |
| `PolygonLabels` / `BrushLabels` | segment | `class x1 y1 ... xn yn` |
| `Choices` / `Labels` | classify | class directories/categories |
| Parent rectangle + `KeyPointLabels` | pose | bbox plus ordered `(x y visibility)` keypoints |

Geometry is normalized to 0–1. Pose keypoints follow configuration order; missing points are `0 0 0`.

Training runs through the Ultralytics CLI. The application pins the run directory to project storage, reads `results.csv` from the run root, and returns `weights/best.pt`.

## Docker

Six Dockerfiles are provided:

- Linux: `docker/Tasks.Cpu.Dockerfile`, `Tasks.Cuda.Dockerfile`, `Api.Cpu.Dockerfile`, and `Api.Cuda.Dockerfile`.
- Windows: `docker/Tasks.Windows.Dockerfile` and `Api.Windows.Dockerfile`; select CPU/CUDA with `PROJECT_NAME` and `APP_ASSEMBLY`.

```bash
docker build -f docker/Tasks.Cpu.Dockerfile -t visualidentity-tasks-cpu .
docker run --rm -p 8080:8080 \
  -e SNET_BOOTSTRAP_ADMIN_PASSWORD='replace-with-a-strong-password' \
  -v visualidentity-data:/app/wwwroot/data \
  -v visualidentity-db:/app/wwwroot/db \
  -v visualidentity-train:/app/train \
  visualidentity-tasks-cpu

docker build -f docker/Api.Cpu.Dockerfile -t visualidentity-api-cpu .
docker run --rm -p 8080:8080 -v visualidentity-api:/app/wwwroot visualidentity-api-cpu
```

CUDA containers require NVIDIA Container Toolkit and GPU access.

## Release artifacts

`.github/workflows/release.yml` publishes only products that exist:

- `Snet.Yolo.Tool`: `win-x64`, `win-x86`.
- CPU Tasks and API: `linux-x64`, `linux-arm64`, `win-x64`.
- CUDA Tasks and API: `linux-x64`, `win-x64`.
- GHCR Linux images: CPU and CUDA variants of Tasks and API.

Pushing a `v*` tag publishes a release. Manual releases are restricted to `main`; the workflow refuses to move or delete an existing tag that points elsewhere.

## Configuration and security

- `AllowedOrigins`: allowed cross-origin callers; an empty list exposes no CORS origin.
- `RateLimit`: API fixed-window rate limits.
- `ConfigModel.MaxImageBytes` / `MaxModelBytes`: API upload limits.
- Image type, byte size, and decoded pixel count are validated.
- Uploaded names, history names, project IDs, and user storage segments are path-constrained.
- Anonymous API access is an explicit deployment choice, not a secure public-Internet default.

## License

MIT
