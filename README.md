# VisualIdentity

[English](README.en.md)

VisualIdentity 是一套基于 .NET 10、ONNX Runtime、SkiaSharp 与 Blazor Server 的 YOLO 视觉识别解决方案。仓库同时提供桌面工具、标注/训练站点、HTTP API、本地执行提供程序以及自动化测试。

## 当前能力

- Ultralytics ONNX 推理：目标检测、实例分割、图像分类、姿态估计、定向框检测（OBB）。
- 模型系列：由本仓库 YoloDotNet 解析器支持的 YOLOv5u–YOLO26、YOLO-World 与 YOLO-E ONNX 模型。
- 执行后端：CPU；NVIDIA CUDA，可选 TensorRT。仓库当前没有 DirectML、OpenVINO 或 CoreML 项目。
- 标注与数据：矩形、多边形、笔刷、关键点、分类；YOLO/YOLO-with-images 等导入导出。
- 训练：自动创建 Python 虚拟环境、调用 Ultralytics、读取训练指标并定位 `best.pt`。
- 视频：FFmpeg/FFprobe 检测与安装、后台有界队列、进度、取消、结果视频。
- 界面语言：简体中文和 English。

> API 按当前产品要求保持匿名访问，不启用登录授权。它仍启用请求大小限制、固定窗口限流、受限 CORS、图片解码上限和安全响应头。请仅部署在可信网络或由外部网关承担访问控制。

## 解决方案结构

`VisualIdentity.sln` 当前包含以下项目：

| 项目 | 用途 |
|---|---|
| `YoloDotNet` | 模型元数据、预处理、后处理和统一推理入口 |
| `YoloDotNet.ExecutionProvider.Cpu` | CPU ONNX Runtime 执行提供程序 |
| `YoloDotNet.ExecutionProvider.Cuda` | CUDA/TensorRT 执行提供程序 |
| `Snet.Yolo.Server` | SQLite 数据访问、模型管理和推理服务 |
| `Snet.Yolo.Tool` | Windows WPF 桌面工具（CPU） |
| `Snet.Yolo.Api.Shared` | CPU/CUDA API 共用源码 |
| `Snet.Yolo.Api.Cpu` | CPU HTTP API |
| `Snet.Yolo.Api.Cuda` | CUDA/TensorRT HTTP API |
| `Snet.Yolo.Tasks.Core` | 标注配置、编辑、导出和训练领域逻辑 |
| `Snet.Yolo.Tasks.Shared` | CPU/CUDA Tasks 共用 Blazor 源码 |
| `Snet.Yolo.Tasks.Cpu` | CPU 标注、训练和验证站点 |
| `Snet.Yolo.Tasks.Cuda` | CUDA 标注、训练和验证站点 |
| `Snet.Yolo.Test` | xUnit 回归与集成测试 |
| `Snet.Py` | Python 辅助项目 |

Shared Project（`.shproj`）提供共用源码，本身不是独立可运行或发布的产品。

## 环境要求

- .NET 10 SDK。
- WPF：Windows x64/x86。
- CUDA 版本：兼容的 NVIDIA 驱动、CUDA/cuDNN 与 ONNX Runtime CUDA 运行时。
- Tasks 训练：Python 3、pip 和 venv；Linux Docker 镜像已包含这些工具及 FFmpeg。
- 视频处理：`ffmpeg` 与 `ffprobe`。

## 构建和测试

```powershell
dotnet restore VisualIdentity.sln
dotnet build VisualIdentity.sln -c Release
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-build
```

测试和 WPF 直接引用仓库中的 YoloDotNet 与 CPU 执行提供程序，不再引用同名的旧 NuGet CPU 包。

## 运行

### WPF 工具

```powershell
dotnet run --project Snet.Yolo.Tool/Snet.Yolo.Tool.csproj -c Release
```

### Tasks 站点

```powershell
# CPU: https://localhost:7351 / http://localhost:5151
dotnet run --project Snet.Yolo.Tasks.Cpu/Snet.Yolo.Tasks.Cpu.csproj

# CUDA: https://localhost:7352 / http://localhost:5152
dotnet run --project Snet.Yolo.Tasks.Cuda/Snet.Yolo.Tasks.Cuda.csproj
```

Tasks 使用 Cookie 登录。首次部署应设置管理员密码：

```powershell
$env:SNET_BOOTSTRAP_ADMIN_PASSWORD = "replace-with-a-strong-password"
```

数据与训练产物按规范化后的用户名隔离。主要目录位于应用输出目录下的 `wwwroot/data`、`wwwroot/db` 和 `train`。

### HTTP API

```powershell
# CPU: https://localhost:7257 / http://localhost:5157
dotnet run --project Snet.Yolo.Api.Cpu/Snet.Yolo.Api.Cpu.csproj

# CUDA: https://localhost:7258 / http://localhost:5158
dotnet run --project Snet.Yolo.Api.Cuda/Snet.Yolo.Api.Cuda.csproj
```

开发环境提供 Swagger。主要路由位于 `/Operate/*`：

- `AddAsync`、`UpdateAsync`、`DeleteAsync`、`QueryAsync`、`QueryAllAsync` 管理模型。
- `IdentityAsync` 返回推理数据。
- `IdentityDrawAsync` 返回推理数据并保存 JPEG 原图、绘制图和详情。
- `GetOriginalImage`、`GetMarkImage`、`GetImageDetails` 查询历史结果。
- `/health` 返回健康状态。

上传端点使用 `multipart/form-data`。推理会话按模型、执行设备和 TensorRT 配置复用，避免每次请求重新加载模型。

CUDA API 不接受任意 TensorRT 文件系统路径：引擎缓存由服务端写入 `tensorrt-cache`；INT8 校准文件只能按文件名引用服务端 `tensorrt-calibration` 中已安装的文件。

## 标注、导出与训练

Tasks 根据 Label Studio 风格的 XML 配置推断任务：

| 配置控件 | YOLO 任务 | 标签格式 |
|---|---|---|
| `RectangleLabels` | detect | `class cx cy width height` |
| `RectangleLabels` + `yoloTask="obb"` | obb | `class x1 y1 x2 y2 x3 y3 x4 y4` |
| `PolygonLabels` / `BrushLabels` | segment | `class x1 y1 ... xn yn` |
| `Choices` / `Labels` | classify | 分类目录/类别 |
| 父矩形 + `KeyPointLabels` | pose | bbox + 固定顺序关键点 `(x y visibility)` |

所有 YOLO 几何坐标均归一化到 0–1。Pose 关键点按配置顺序输出，缺失点写为 `0 0 0`。

训练使用 Ultralytics CLI。应用会把运行目录固定在工程空间，读取运行目录下的 `results.csv`，并从 `weights/best.pt` 返回最佳模型。

## Docker

仓库提供六个 Dockerfile：

- Linux：`docker/Tasks.Cpu.Dockerfile`、`Tasks.Cuda.Dockerfile`、`Api.Cpu.Dockerfile`、`Api.Cuda.Dockerfile`。
- Windows：`docker/Tasks.Windows.Dockerfile`、`Api.Windows.Dockerfile`，通过 `PROJECT_NAME` 和 `APP_ASSEMBLY` 选择 CPU/CUDA 项目。

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

CUDA 容器需要 NVIDIA Container Toolkit，并在运行时传递 GPU。

## 发布产物

`.github/workflows/release.yml` 只发布仓库中真实存在的产品：

- `Snet.Yolo.Tool`：`win-x64`、`win-x86`。
- `Snet.Yolo.Tasks.Cpu`：`linux-x64`、`linux-arm64`、`win-x64`。
- `Snet.Yolo.Tasks.Cuda`：`linux-x64`、`win-x64`。
- `Snet.Yolo.Api.Cpu`：`linux-x64`、`linux-arm64`、`win-x64`。
- `Snet.Yolo.Api.Cuda`：`linux-x64`、`win-x64`。
- GHCR Linux 镜像：Tasks/API 的 CPU 与 CUDA 四个镜像。

推送 `v*` 标签会发布；手动发布只允许从 `main` 执行。工作流不会移动或删除已存在且指向其他提交的发布标签。

## 配置与安全

- `AllowedOrigins`：允许的跨域来源；空数组表示不向跨域调用开放。
- `RateLimit`：API 固定窗口限流参数。
- `ConfigModel.MaxImageBytes` / `MaxModelBytes`：API 上传大小上限。
- 图片在解码前后均受格式、字节数和最大像素数校验。
- 上传文件名、历史文件名、工程标识和用户目录均进行路径约束。
- API 匿名是明确的部署选择，不等于面向公网默认安全。

## License

MIT
