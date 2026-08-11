<h1 align="center">🔍 Snet.VisualIdentity</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/><br/>
</p>

<p align="center">
  <b>基于 .NET 10 的 YOLO 多模型智能视觉识别平台</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/nuget/v/Snet.Yolo.Server?color=blue"/>
  <img src="https://img.shields.io/github/stars/shunnet/VisualIdentity?style=social"/>
</p>

<p align="center">
  🚀 高效 · 🧩 灵活 · 📦 易部署 · 🔒 安全
</p>

<p align="center">
  <a href="https://snet.cn"><b>🌐 官方网站</b></a> ·
  <a href="https://github.com/shunnet/VisualIdentity"><b>📦 GitHub</b></a> ·
  <a href="https://snet.cn/EaiUj"><b>🎬 演示视频</b></a> ·
  <a href="https://www.nuget.org/packages/Snet.Yolo.Server"><b>📦 NuGet</b></a>
</p>

<p align="center">
  📖 <a href="README.en.md"><b>English</b></a> | 简体中文
</p>

## 🌟 项目简介

**VisualIdentity** 是一个开箱即用的智能识别平台：结合 **.NET** 的现代化能力、[YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 高性能推理引擎与 **SQLite** 轻量数据管理，解决「多模型部署 + 多任务识别」的落地痛点——**检测、分类、分割、姿态估计、定向检测** 五种任务统一管理、按需切换。

> 💡 `.NET` badge：核心库 `Snet.Yolo.Server` 多目标 **net8.0 / net10.0**；API 服务与工具均基于 **.NET 10**。

### ✨ 核心特性

| 特性 | 说明 |
|------|------|
| 🧠 **多模型管理** | 基于 SQLite 的模型增删改查，版本化管理与快速切换 |
| 🎯 **五合一识别** | 对象检测 · 定向检测 (OBB) · 图像分类 · 语义分割 · 姿态估计 |
| ⚡ **多硬件加速** | CPU · CUDA / TensorRT · OpenVINO · CoreML · DirectML |
| 🌍 **跨平台** | Windows · Linux · macOS · Docker |
| 🔒 **生产级安全** | CSRF 防护 · 速率限制 · CORS 控制 · 安全响应头 |
| 📊 **实时性能** | 毫秒级耗时统计，批量验证与置信度分析 |
| 🖥️ **WPF 调试工具** | 5 种识别模式可视化验证 + 数据统一标注工具 |
| 🐍 **Python 辅助** | 内置模型导出脚本，一键转换 PyTorch → ONNX |

## 🎯 应用场景

| 场景 | 用途 | 推荐模型类型 |
|------|------|------------|
| 🏭 **工业质检** | 瑕疵检测、异物识别、零件计数 | 检测、分割 |
| 🛒 **零售分析** | 顾客行为追踪、货架商品检测 | 检测、分类 |
| 🛡️ **智能安防** | 异常行为监测、跌倒检测、区域入侵 | 姿态估计、检测 |
| 🚗 **自动驾驶** | 道路目标检测、交通标志识别 | 定向检测、检测 |
| 🏥 **医疗影像** | 病灶分割、细胞分类 | 分割、分类 |
| 📄 **文档分析** | 旋转文本检测、表格识别 | 定向检测 |
| 🌐 **边缘计算** | 树莓派 / Jetson 轻量化部署 | CPU、OpenVINO |

## 🏗️ 项目架构

```
VisualIdentity/
├── Snet.Yolo.Server/              # 🧠 核心推理引擎 + 数据模型（net8.0/net10.0 双目标）
├── Snet.Yolo.Api.Shared/          # 🔗 共享 API 层（Shared Project：控制器 / 安全 / 图片处理）
├── Snet.Yolo.Api.Cpu/             # 🖥️ CPU API（HTTP 5157 · HTTPS 7257）
├── Snet.Yolo.Api.Cuda/            # 🎮 CUDA / TensorRT API（HTTP 5158 · HTTPS 7258）
├── Snet.Yolo.Api.OpenVino/        # 🔌 OpenVINO API（HTTP 5159 · HTTPS 7259）
├── Snet.Yolo.Api.CoreML/          # 🍎 CoreML API（HTTP 5160 · HTTPS 7260）
├── Snet.Yolo.Api.DirectML/        # 🪟 DirectML API（HTTP 5161 · HTTPS 7261）
├── Snet.Yolo.Tool/                # 🛠️ WPF 桌面调试工具
├── Snet.Yolo.Test/                # 🧪 集成测试（控制台）
├── Snet.Py/                       # 🐍 Python 模型导出脚本
└── appsettings.json               # ⚙️ 全局配置
```

### 🔄 数据流

```
客户端上传图片 → API 控制器（参数验证 + CSRF 检查）→ 速率限制中间件
→ ManageOperate（数据库查询模型路径）→ IdentityOperate（加载模型 + 硬件加速）
→ YoloDotNet 推理（GPU / CPU）→ ResultHandler（结果转换）
→ ImageHandler（标注绘制 + 磁盘存储）→ 返回 JSON + 图片 URL
```

## ⚡ 快速开始

### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 至少一个 ONNX 格式的 YOLO 模型（[导出方法](#-onnx-模型导出)）

### 1️⃣ 克隆仓库

```bash
git clone https://github.com/shunnet/VisualIdentity.git
cd VisualIdentity
```

### 2️⃣ 运行 CPU 版本 API

```bash
cd Snet.Yolo.Api.Cpu
dotnet run
```

浏览器访问 `http://localhost:5157/swagger` 查看 Swagger UI（仅 Development 环境）。

### 3️⃣ 上传模型并推理

```bash
# 1. 上传 ONNX 模型
curl -X POST http://localhost:5157/Operate/AddAsync \
  -F "file=@your_model.onnx" \
  -F "describe=我的检测模型" \
  -F "onnxType=ObjectDetection"

# 2. 快速推理（仅坐标 / 标签 / 置信度）
curl -X POST http://localhost:5157/Operate/IdentityAsync \
  -F "onnxIndex=1" -F "file=@test.jpg" \
  -F 'paramJson={"Confidence":0.2,"Iou":0.7}'

# 3. 完整推理（标注图 + 坐标 + 图片 URL）
curl -X POST http://localhost:5157/Operate/IdentityDrawAsync \
  -F "onnxIndex=1" -F "file=@test.jpg" \
  -F 'paramJson={"Confidence":0.2,"Iou":0.7}'
```

## 📦 NuGet 安装

在您自己的 .NET 项目中使用 VisualIdentity 核心库：

```bash
# 核心推理库（必装）
dotnet add package Snet.Yolo.Server

# 根据硬件任选其一（⚠️ 只能选一个）
dotnet add package YoloDotNet.ExecutionProvider.Cpu      # 🖥️ 通用 CPU
dotnet add package YoloDotNet.ExecutionProvider.Cuda     # 🎮 NVIDIA GPU + TensorRT
dotnet add package YoloDotNet.ExecutionProvider.OpenVino # 🔌 Intel OpenVINO
dotnet add package YoloDotNet.ExecutionProvider.CoreML   # 🍎 Apple Silicon
dotnet add package YoloDotNet.ExecutionProvider.DirectML # 🪟 Windows GPU
```

### 💡 C# 调用示例

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

// 创建推理实例（自动缓存，配置不变时复用）
var identity = IdentityOperate.Instance(new IdentityData
{
    Hardware = new CpuExecutionProvider("/path/to/model.onnx"),
    IdentifyType = OnnxType.ObjectDetection,
    SN = "my-detector"
});

using SKImage image = SKImage.FromEncodedData("/path/to/image.jpg");

// 执行推理
OperateResult result = await identity.RunAsync(new ObjectDetectionData
{
    Confidence = 0.23,  // 置信度阈值
    Iou = 0.7,          // 交并比阈值
    File = image.Encode().ToArray()
});

// 获取结果并绘制标注框
var detections = result.GetObjectDetectionResult()?.ToObjectDetection();
if (detections is { Count: > 0 })
{
    foreach (var d in detections)
        Console.WriteLine($"{d.Label.Name}: {d.Confidence:P1} @ {d.BoundingBox}");

    using SKBitmap annotated = image.Draw(detections);
    // 保存或显示 annotated...
}

identity.Dispose(); // 释放 GPU 资源
```

## 🔌 API 接口文档

### 📋 模型管理

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| `POST` | `/Operate/AddAsync` | 上传 ONNX 模型文件 | CSRF Token |
| `POST` | `/Operate/UpdateAsync` | 修改模型描述或类型 | CSRF Token |
| `POST` | `/Operate/DeleteAsync` | 删除模型（可选删除文件） | CSRF Token |
| `GET` | `/Operate/QueryAsync?index=1` | 查询指定模型 | 无 |
| `GET` | `/Operate/QueryAllAsync` | 查询全部模型 | 无 |

### 🧠 推理接口

| 方法 | 路径 | 说明 | 返回内容 |
|------|------|------|---------|
| `POST` | `/Operate/IdentityAsync` | 🚀 快速推理 | 仅坐标 / 标签 / 置信度 |
| `POST` | `/Operate/IdentityDrawAsync` | 🎨 完整推理 | 坐标 + 标注图 URL + 原图 URL |

> 📌 推理接口为 **POST multipart/form-data** 提交（`onnxIndex`、`file`、`paramJson` 均为表单字段），以下额外参数同样以表单字段传入：

| 硬件版本 | 额外字段 |
|---------|---------|
| 🎮 CUDA | `gpuid`（GPU ID）、`trtConfig`（TensorRT 配置） |
| 🍎 CoreML | `adaptive`（自适应模式，默认 `true`） |
| 🪟 DirectML | `gpuid`（GPU ID） |
| 🔌 OpenVINO | `openVino`（高级配置） |

### 🖼️ 历史图片

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/Operate/GetOriginalImage?name=xxx&type=ObjectDetection` | 获取原始图片 |
| `GET` | `/Operate/GetMarkImage?name=xxx&type=ObjectDetection` | 获取标注图片 |
| `GET` | `/Operate/GetImageDetails?name=xxx&type=ObjectDetection` | 完整详情（原图 + 标注 + 坐标 JSON） |

### 🏥 健康检查

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/health` | 健康检查（返回 `{"Status":"Healthy","Timestamp":"..."}`） |

### `paramJson` 参数格式

| 识别类型 | JSON 格式 |
|---------|----------|
| 对象检测 | `{"Confidence":0.2,"Iou":0.7}` |
| 定向检测 | `{"Confidence":0.2,"Iou":0.7}` |
| 图像分类 | `{"Classes":1}` |
| 姿态估计 | `{"Confidence":0.2,"Iou":0.7}` |
| 语义分割 | `{"Confidence":0.2,"Iou":0.7,"PixelConfidence":0.65}` |

## ⚙️ 配置文件

### `appsettings.json`

```json
{
  "AllowedOrigins": [],           // 🔒 CORS 白名单，空数组 = 拒绝所有跨域
  "RateLimit": {
    "PermitLimit": 120,           // ⏱️ 每分钟允许的请求数
    "WindowMinutes": 1,           // ⏱️ 时间窗口（分钟）
    "QueueLimit": 20              // ⏱️ 超出后的最大排队数
  },
  "ConfigModel": {
    "NameFormat": "yyyyMMddHHmmssffffff",              // 🏷️ 文件名时间格式
    "OriginalImageNamingFormat": "{0}-Original.jpeg",  // 🖼️ 原图命名
    "ResultImageNamingFormat": "{0}-Result.jpeg",      // 🎨 标注图命名
    "DetailsNamingFormat": "{0}-Details.ini",          // 📄 详情文件命名
    "RetentionDays": 30                                // 🗑️ 历史数据保留天数
  }
}
```

### 环境变量支持

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境（`Development` / `Production`） | `Production` |
| `ASPNETCORE_URLS` | 服务监听地址 | `http://localhost:5157` |

> ⚠️ Swagger UI 仅在 `Development` 环境下启用，生产环境自动关闭。

## 🧠 支持的任务

| 分类 (Classification) | 检测 (Detection) | OBB 定向检测 | 分割 (Segmentation) | 姿态估计 (Pose) |
|:---:|:---:|:---:|:---:|:---:|
| 🔖 整图分类 | 📦 边界框定位 | 🔄 旋转框定位 | 🎭 像素级分割 | 🦴 关键点检测 |
| 输出标签+置信度 | 输出框+标签+置信度 | 输出旋转框+角度 | 输出遮罩+框+标签 | 输出骨骼点+框 |

### 🦴 姿态估计 — 内置跌倒检测

`YoloPoseViewModel` 集成**实时跌倒检测算法**（`FallDetector`），基于 17 个人体关键点进行多维度分析：

| 检测维度 | 判定标准 | 可配置 |
|---------|---------|--------|
| 📏 身体高度 | 鼻-踝距离 < 50% 图像高度 | `FlatHeightRatio` |
| 📐 身体倾角 | 肩-髋连线 < 70° | `AngleThreshold` |
| ↔️ 躯干水平度 | 肩髋 Y 差值 < 10% 图像高度 | `TorsoHorizontalThresholdRatio` |
| 📍 近地距离 | 平均关键点 Y > 60% 图像高度 | `GroundProximityRatio` |
| ✅ 综合判定 | 满足 ≥ 2 项即判定跌倒 | `FallScoreThreshold` |

## ✅ 已验证的 YOLO 模型

以下 YOLO 模型已经过 **YoloDotNet** 与 **Snet.Yolo.Server** 的完整推理测试与验证：

| 分类 (Classification) | 检测 (Detection) | 分割 (Segmentation) | 姿态估计 (Pose) | OBB 定向检测 |
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

## 🖥️ 执行提供者

| Provider | Windows | Linux | macOS | Docker | 适用场景 |
|----------|:---:|:---:|:---:|:---:|----------|
| 🖥️ **CPU** | ✅ | ✅ | ✅ | ✅ | 通用推理、边缘设备 |
| 🎮 **CUDA / TensorRT** | ✅ | ✅ | ❌ | ✅ | NVIDIA GPU 加速 |
| 🔌 **OpenVINO** | ✅ | ✅ | ❌ | ✅ | Intel 芯片优化 |
| 🍎 **CoreML** | ❌ | ❌ | ✅ | ❌ | Apple Silicon (M1/M2/M3) |
| 🪟 **DirectML** | ✅ | ❌ | ❌ | ❌ | Windows GPU 通用加速 |

> ⚠️ 每个项目 / 进程只能引用**一个**执行提供程序包。混合使用会导致运行时冲突（DLL 重复加载、符号冲突）。

## 💡 ONNX 模型导出

### 使用 Python (Ultralytics)

```bash
pip install ultralytics
python Snet.Py/Snet.Py.py
```

### 手动导出

```bash
# YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

> 📌 使用正确的 opset 版本可确保与 ONNX Runtime 的最佳兼容性与推理性能。

## 🐳 Docker 部署

### 构建镜像

```bash
# CPU 版本
docker build -t snet-yolo-cpu -f Snet.Yolo.Api.Cpu/Dockerfile .

# CUDA 版本（需要 NVIDIA Container Toolkit）
docker build -t snet-yolo-cuda -f Snet.Yolo.Api.Cuda/Dockerfile .

# OpenVINO 版本
docker build -t snet-yolo-openvino -f Snet.Yolo.Api.OpenVino/Dockerfile .
```

### 运行容器

```bash
docker run -d -p 8080:8080 \
  -v /path/to/models:/app/wwwroot/onnxs \
  -v /path/to/data:/app/wwwroot \
  snet-yolo-cpu

curl http://localhost:8080/health   # 健康检查
```

> 📝 容器内部暴露 `8080`（及 `8081`）；CoreML（仅 macOS）与 DirectML（仅 Windows）不支持 Docker，应直接在目标系统上运行。

## 🧪 测试

```bash
cd Snet.Yolo.Test
# 设置环境变量后运行控制台集成测试
export YOLO_IMAGE_PATH="/path/to/test.jpg"
export YOLO_MODEL_PATH="/path/to/model.onnx"
export YOLO_TYPE="ObjectDetection"
dotnet run
```

## 🔒 安全特性

| 特性 | 实现方式 | 配置 |
|------|---------|------|
| 🌐 **CORS 控制** | `RestrictedOrigins` 策略 | `appsettings.json` → `AllowedOrigins` |
| 🛡️ **CSRF 防护** | `[ValidateAntiForgeryToken]` 过滤器 | 所有状态变更 POST 端点 |
| ⏱️ **速率限制** | 固定窗口算法 | `RateLimit` 配置节 |
| 🔐 **安全响应头** | 中间件自动注入 | X-Content-Type-Options / X-Frame-Options / CSP 等 |
| 📁 **文件名净化** | 过滤路径遍历字符 + GUID 唯一化 | 上传处理逻辑 |
| 🔑 **密钥管理** | User Secrets（开发）+ 环境变量（生产） | 每个项目独立 `UserSecretsId` |
| 📏 **文件大小限制** | Kestrel + FormOptions 双重限制 | 1GB 请求体上限 |
| 🧹 **数据自动清理** | `HistoryFileHandler` 定时任务 | `RetentionDays`（默认 30 天） |

## 📈 性能优化

| 优化项 | 说明 |
|--------|------|
| 🔄 **模型实例缓存** | 配置不变时复用模型实例，避免重复加载 |
| 🧵 **异步全链路** | HTTP → GPU 推理 → 磁盘写入全链路 `async/await` |
| 🖼️ **并行写盘** | 原图 / 标注图 / JSON 详情 `Task.WhenAll` 并行写入 |
| 💾 **内存优化** | `SKBitmap.Freeze()` 跨线程共享、`using` 确保 Dispose |

### 推理耗时构成（参考值，CPU 模式）

```
HTTP 接收        ~   5ms
图片解码         ~  20ms
ONNX 推理        ~ 150ms（取决于模型大小和硬件）
结果转换         ~   5ms
标注绘制         ~  30ms（仅 IdentityDraw 模式）
磁盘写入         ~  10ms（并行，不阻塞响应）
────────────────────────
总耗时 (快速)    ~ 180ms
总耗时 (完整)    ~ 220ms
```

## 📚 依赖组件

| 组件 | 说明 |
|------|------|
| 🔗 **Snet.DB** | Dapper & SqlSugarCore 双 ORM，自动建表，Code-First 体验 |
| ⚡ **YoloDotNet** | 超快速生产级 YOLO 推理库，支持 YOLOv5u → YOLOv26 全系列 |
| 🎨 **SkiaSharp** | 跨平台 2D 渲染：图片解码、标注绘制、关键点渲染 |
| 🗄️ **SQLite** | 嵌入式数据库：模型元数据管理 |

## 🙏 致谢

| 项目 | 说明 |
|------|------|
| 🌐 [Snet.cn](https://snet.cn) | 项目官方网站 |
| 🔥 [Ultralytics](https://github.com/ultralytics/ultralytics) | YOLO 模型训练与导出 |
| ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) | .NET YOLO 推理引擎 |
| 🖥️ [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) | WPF 现代化 UI 框架 |
| 🗄️ [SqlSugarCore](https://github.com/DotNetNext/SqlSugar) | ORM 框架 |
| 🎨 [SkiaSharp](https://github.com/mono/SkiaSharp) | 跨平台图形渲染 |

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

本项目基于 **MIT** 开源协议 —— 自由使用、修改、分发。

📄 完整条款请阅读 [LICENSE](LICENSE) 文件。

> ⚠️ 软件按「原样」提供，作者不对使用后果承担责任。

## 📈 Star History

<a href="https://www.star-history.com/?repos=shunnet%2FVisualIdentity&type=date&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&theme=dark&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA"/>
 </picture>
</a>
