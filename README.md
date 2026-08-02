<h1 align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/><br/>
  🔍 VisualIdentity
</h1>

<p align="center">
  <b>基于 .NET 10 的 YOLO 多模型智能视觉识别平台</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/github/stars/shunnet/VisualIdentity?style=social"/>
  <img src="https://img.shields.io/nuget/v/Snet.Yolo.Server?color=blue"/>
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

## 📖 目录

- [🌟 项目简介](#-项目简介)
- [🎯 应用场景](#-应用场景)
- [🏗️ 项目架构](#️-项目架构)
- [⚡ 快速开始](#-快速开始)
- [📦 NuGet 安装](#-nuget-安装)
- [🔌 API 接口文档](#-api-接口文档)
- [⚙️ 配置文件](#️-配置文件)
- [🧠 支持的任务](#-支持的任务)
- [✅ 已验证的 YOLO 模型](#-已验证的-yolo-模型)
- [🖥️ 执行提供者](#️-执行提供者)
- [💡 ONNX 模型导出](#-onnx-模型导出)
- [🐳 Docker 部署](#-docker-部署)
- [🧪 测试](#-测试)
- [🔒 安全特性](#-安全特性)
- [📈 性能优化](#-性能优化)
- [📚 依赖组件](#-依赖组件)
- [🙏 致谢](#-致谢)
- [📜 许可证](#-许可证)
- [⭐ 项目星标历史](#-项目星标历史)

## 🌟 项目简介

在 **AI 应用落地**的过程中，**模型管理**与**多任务识别**一直是开发者的痛点。
无论是**检测、分类、分割、姿态估计、定向检测**，往往都需要同时部署多个模型，传统方案在**效率**和**易用性**上总会遇到瓶颈。

**VisualIdentity** 正是为了解决这一系列问题而生。
它结合了 **.NET 10** 的现代化能力、[YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 的高性能推理引擎、以及 **SQLite** 的轻量级数据管理，为开发者提供一个**开箱即用**的智能识别平台。

### ✨ 核心特性

| 特性 | 说明 |
|------|------|
| 🧠 **多模型管理** | 基于 SQLite 的模型增删改查，支持版本化管理与快速切换 |
| 🎯 **五合一识别** | 对象检测 · 定向检测 (OBB) · 图像分类 · 语义分割 · 姿态估计 |
| ⚡ **多硬件加速** | CPU · CUDA / TensorRT · OpenVINO · CoreML · DirectML |
| 🌍 **跨平台** | Windows · Linux · macOS · Docker |
| 🔒 **生产级安全** | CSRF 防护 · 速率限制 · CORS 控制 · 安全响应头 |
| 📊 **实时性能** | 内置毫秒级耗时统计，支持批量验证与置信度统计分析 |
| 🖥️ **WPF 调试工具** | 可视化图片验证、跌倒检测、数据统一标注工具 |
| 🐍 **Python 辅助** | 内置模型导出脚本，一键转换 PyTorch → ONNX |

## 🎯 应用场景

| 场景 | 用途 | 推荐模型类型 |
|------|------|------------|
| 🏭 **工业质检** | 瑕疵检测、异物识别、零件计数 | 检测、分割 |
| 🛒 **零售分析** | 顾客行为追踪、货架商品检测 | 检测、分类 |
| 🛡️ **智能安防** | 异常行为监测、跌倒检测、区域入侵 | 姿态估计、检测 |
| 🚗 **自动驾驶** | 道路目标检测、交通标志识别 | 定向检测、检测 |
| 🏥 **医疗影像** | 病灶分割、细胞分类 | 分割、分类 |
| 🎓 **科研教育** | 多模型对比实验、教学演示平台 | 全部类型 |
| 🌐 **边缘计算** | 树莓派/ Jetson 轻量化部署 | CPU、OpenVINO |
| 📄 **文档分析** | 旋转文本检测、表格识别 | 定向检测 |

## 🏗️ 项目架构

```
VisualIdentity/
├── Snet.Yolo.Server/              # 🧠 核心推理引擎 + 数据模型
│   ├── IdentityOperate.cs         #    YOLO 视觉识别（5 种任务）
│   ├── ManageOperate.cs           #    SQLite CRUD 模型管理
│   ├── handler/                   #    结果转换 · 公共工具 · 姿态颜色
│   ├── interface/                 #    IData · IIdentity · IManage
│   └── models/                    #    data/ (实体) · enum/ (枚举)
│
├── Snet.Yolo.Api.Shared/          # 🔗 共享 API 层（Shared Project）
│   ├── Controllers/               #    OperateBaseController（核心控制器）
│   ├── Handler/                   #    图片处理 · 历史文件清理
│   ├── Attribute/                 #    文件类型验证过滤器
│   ├── Model/                     #    ConfigModel
│   └── Program.cs                 #    ASP.NET 启动配置
│
├── Snet.Yolo.Api.Cpu/             # 🖥️ CPU API（端口 5157）
├── Snet.Yolo.Api.Cuda/            # 🎮 CUDA / TensorRT API（端口 5158）
├── Snet.Yolo.Api.OpenVino/        # 🔌 OpenVINO API（端口 5159）
├── Snet.Yolo.Api.CoreML/          # 🍎 Apple CoreML API（端口 5160）
├── Snet.Yolo.Api.DirectML/        # 🪟 DirectML API（端口 5161）
│
├── Snet.Yolo.Tool/                # 🛠️ WPF 桌面调试工具
│   ├── View/                      #    6 种识别模式页面
│   ├── ViewModel/                 #    识别逻辑 · 跌倒检测 · 数据统一
│   └── Data/                      #    UI 数据模型
│
├── Snet.Yolo.Test/                # 🧪 集成测试项目
├── Snet.Py/                       # 🐍 Python 模型导出脚本
│
├── appsettings.json               # ⚙️ 全局配置
└── VisualIdentity.sln             # 📦 解决方案文件
```

### 🔄 数据流

```
客户端上传图片 → API 控制器（参数验证 + CSRF 检查）
    → 速率限制中间件
    → ManageOperate（查询数据库获取 ONNX 模型路径）
    → IdentityOperate（加载 YOLO 模型 + 硬件加速器）
    → YoloDotNet 推理引擎（GPU / CPU 运算）
    → ResultHandler（结果转换）
    → ImageHandler（标注图片绘制 + 磁盘存储）
    → 返回 JSON 结果 + 图片 URL
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

浏览器访问 `http://localhost:5157/swagger` 查看 Swagger UI。

### 3️⃣ 上传模型并推理

```bash
# 1. 上传 ONNX 模型
curl -X POST http://localhost:5157/Operate/AddAsync \
  -F "file=@your_model.onnx" \
  -F "describe=我的检测模型" \
  -F "onnxType=ObjectDetection"

# 2. 执行推理（仅返回坐标数据）
curl -X POST http://localhost:5157/Operate/IdentityAsync \
  -F "onnxIndex=1" \
  -F "file=@test.jpg" \
  -F 'paramJson={"Confidence":0.2,"Iou":0.7}'

# 3. 执行推理（返回标注图 + 坐标 + 图片 URL）
curl -X POST http://localhost:5157/Operate/IdentityDrawAsync \
  -F "onnxIndex=1" \
  -F "file=@test.jpg" \
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

// 加载图片
using SKImage image = SKImage.FromEncodedData("/path/to/image.jpg");

// 执行推理
OperateResult result = await identity.RunAsync(new ObjectDetectionData
{
    Confidence = 0.23,  // 置信度阈值
    Iou = 0.7,          // 交并比阈值
    File = image.Encode().ToArray()
});

// 获取结果
var detections = result.GetObjectDetectionResult()?.ToObjectDetection();
if (detections is { Count: > 0 })
{
    foreach (var d in detections)
    {
        Console.WriteLine($"{d.Label.Name}: {d.Confidence:P1} @ {d.BoundingBox}");
    }

    // 绘制标注框
    using SKBitmap annotated = image.Draw(detections);
    // 保存或显示 annotated...
}

// 用完后释放（释放 GPU 资源）
identity.Dispose();
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

### 🖼️ 历史图片

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/Operate/GetOriginalImage?name=xxx&type=ObjectDetection` | 获取原始图片 |
| `GET` | `/Operate/GetMarkImage?name=xxx&type=ObjectDetection` | 获取标注图片 |
| `GET` | `/Operate/GetImageDetails?name=xxx&type=ObjectDetection` | 获取完整详情（原图+标注+坐标JSON） |

### 🏥 健康检查

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/health` | 健康检查（返回 `{"Status":"Healthy","Timestamp":"..."}`） |

### 🔧 各硬件加速版本的额外参数

```bash
# CUDA - 指定 GPU ID + TensorRT 配置
IdentityAsync?onnxIndex=1&gpuid=0&trtConfig=...

# CoreML - 自适应模式
IdentityAsync?onnxIndex=1&adaptive=true

# DirectML - 指定 GPU ID
IdentityAsync?onnxIndex=1&gpuid=0

# OpenVINO - 高级配置
IdentityAsync?onnxIndex=1&openVino=...
```

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
  "AllowedOrigins": [],           // 🔒 CORS 白名单，空数组=拒绝所有跨域
  "RateLimit": {
    "PermitLimit": 120,           // ⏱️ 每分钟允许的请求数
    "WindowMinutes": 1,           // ⏱️ 时间窗口（分钟）
    "QueueLimit": 20              // ⏱️ 超出后的最大排队数
  },
  "ConfigModel": {
    "NameFormat": "yyyyMMddHHmmssffffff",           // 🏷️ 文件名时间格式
    "OriginalImageNamingFormat": "{0}-Original.jpeg",  // 🖼️ 原图命名
    "ResultImageNamingFormat": "{0}-Result.jpeg",      // 🎨 标注图命名
    "DetailsNamingFormat": "{0}-Details.ini",          // 📄 详情文件命名
    "RetentionDays": 30                               // 🗑️ 历史数据保留天数
  }
}
```

### 环境变量支持

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境（`Development` / `Production`） | `Production` |
| `ASPNETCORE_URLS` | 服务监听地址 | `http://localhost:5157` |

> ⚠️ **注意**: Swagger UI 仅在 `Development` 环境下启用，生产环境自动关闭。

## 🧠 支持的任务

| 分类 (Classification) | 检测 (Detection) | OBB 定向检测 | 分割 (Segmentation) | 姿态估计 (Pose) |
|:---:|:---:|:---:|:---:|:---:|
| 🔖 整图分类 | 📦 边界框定位 | 🔄 旋转框定位 | 🎭 像素级分割 | 🦴 关键点检测 |
| 输出标签+置信度 | 输出框+标签+置信度 | 输出旋转框+角度 | 输出遮罩+框+标签 | 输出骨骼点+框 |
| <img src="https://user-images.githubusercontent.com/35733515/297393507-c8539bff-0a71-48be-b316-f2611c3836a3.jpg" width=260> | <img src="https://user-images.githubusercontent.com/35733515/273405301-626b3c97-fdc6-47b8-bfaf-c3a7701721da.jpg" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/d15c5b3e-18c7-4c2c-9a8d-1d03fb98dd3c" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/3ae97613-46f7-46de-8c5d-e9240f1078e6" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/b7abeaed-5c00-4462-bd19-c2b77fe86260" width=260> |

### 🦴 姿态估计 — 内置跌倒检测

WPF 工具中的 `YoloPoseViewModel` 集成了**实时跌倒检测算法**（`FallDetector`），基于 17 个人体关键点进行多维度分析：

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
| | YOLOv11 | YOLO-World (v2) | | |
| | YOLOv12 | | | |
| | YOLOv26 | | | |
| | RT-DETR | | | |

## 🖥️ 执行提供者

| Provider | Windows | Linux | macOS | Docker | 适用场景 |
|----------|:---:|:---:|:---:|:---:|----------|
| 🖥️ **CPU** | ✅ | ✅ | ✅ | ✅ | 通用推理、边缘设备 |
| 🎮 **CUDA / TensorRT** | ✅ | ✅ | ❌ | ✅ | NVIDIA GPU 加速 |
| 🔌 **OpenVINO** | ✅ | ✅ | ❌ | ✅ | Intel 芯片优化 |
| 🍎 **CoreML** | ❌ | ❌ | ✅ | ❌ | Apple Silicon (M1/M2/M3) |
| 🪟 **DirectML** | ✅ | ❌ | ❌ | ❌ | Windows GPU 通用加速 |

> ⚠️ **重要**: 每个项目/进程只能引用**一个**执行提供程序包。混合使用不同提供程序会导致运行时冲突（DLL 重复加载、符号冲突）。

## 💡 ONNX 模型导出

### 使用 Python (Ultralytics)

```bash
# 安装依赖
pip install ultralytics

# 训练或下载模型后导出
python Snet.Py/Snet.Py.py
```

### 手动导出

```bash
# YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

> 📌 使用正确的 opset 版本可确保与 ONNX Runtime 的最佳兼容性和推理性能。

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
# CPU 版本
docker run -d -p 8080:8080 \
  -v /path/to/models:/app/wwwroot/onnxs \
  -v /path/to/data:/app/wwwroot \
  snet-yolo-cpu

# 健康检查
curl http://localhost:8080/health
```

> 📝 CoreML（仅 macOS）和 DirectML（仅 Windows）不支持 Docker 部署，应直接在目标系统上运行。

## 🧪 测试

```bash
# 运行集成测试（需设置环境变量）
cd Snet.Yolo.Test
export YOLO_IMAGE_PATH="/path/to/test.jpg"
export YOLO_MODEL_PATH="/path/to/model.onnx"
export YOLO_TYPE="ObjectDetection"
dotnet run

# 或使用 xUnit 运行
dotnet test
```

## 🔒 安全特性

| 特性 | 实现方式 | 配置 |
|------|---------|------|
| 🌐 **CORS 控制** | `RestrictedOrigins` 策略 | `appsettings.json` → `AllowedOrigins` |
| 🛡️ **CSRF 防护** | `[ValidateAntiForgeryToken]` 过滤器 | 所有状态变更 POST 端点 |
| ⏱️ **速率限制** | 固定窗口算法 (Fixed Window) | `RateLimit` 配置节 |
| 🔐 **安全响应头** | 中间件自动注入 | X-Content-Type-Options, X-Frame-Options, CSP 等 |
| 📁 **文件名净化** | 过滤路径遍历字符 + GUID 唯一化 | 上传处理逻辑 |
| 🔑 **密钥管理** | User Secrets (开发) + 环境变量 (生产) | 每个项目独立 `UserSecretsId` |
| 🚫 **生产环境** | Swagger 自动禁用 | `ASPNETCORE_ENVIRONMENT=Production` |
| 📏 **文件大小限制** | Kestrel + FormOptions 双重限制 | 1GB 请求体上限 |
| 🧹 **数据自动清理** | `HistoryFileHandler` 定时任务 | `RetentionDays` (默认 30 天) |

## 📈 性能优化

### 推理性能

| 优化项 | 说明 |
|--------|------|
| 🔄 **模型实例缓存** | GPU 模型实例在配置未变时自动复用，避免重复加载 |
| 🧵 **异步全链路** | 从 HTTP 请求到 GPU 推理再到磁盘写入，全链路 `async/await` |
| 🖼️ **并行写盘** | 原图、标注图、JSON 详情文件使用 `Task.WhenAll` 并行写入 |
| 💾 **内存优化** | `SKBitmap.Freeze()` 跨线程共享、`MemoryStream` 及时释放、`using` 确保 Dispose |

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

### 🔗 [Snet.DB](https://www.nuget.org/packages/Snet.DB)

- 集成 **Dapper** & **SqlSugarCore** 双 ORM 引擎
- 支持高性能 SQL 映射与链式查询
- 自动建表，Code-First 开发体验
- 保持轻量的同时具备**生产级性能**

### ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet)

- 适用于 .NET 的**超快速、可投入生产**的 YOLO 推理库
- 模块化、轻量级的 C# 库，实现实时计算机视觉
- 支持 YOLOv5u → YOLOv26 全系列模型
- 集成 SORT 目标追踪、GPU 加速、自定义后处理

### 🎨 SkiaSharp

- 跨平台 2D 图形渲染引擎
- 用于图片解码、标注框绘制、关键点渲染

### 🗄️ SQLite

- 嵌入式零配置关系型数据库
- 用于 ONNX 模型元数据管理（路径、类型、描述、时间戳）

## 🙏 致谢

| 项目 | 说明 |
|------|------|
| 🌐 [Snet.cn](https://snet.cn) | 项目官方网站 |
| 🔥 [Ultralytics](https://github.com/ultralytics/ultralytics) | YOLO 模型训练与导出 |
| ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) | .NET YOLO 推理引擎 |
| 🖥️ [WpfMUI](https://github.com/shunnet/WpfMUI) | WPF 现代化 UI 框架 |
| 🗄️ [SqlSugarCore](https://github.com/DotNetNext/SqlSugar) | ORM 框架 |
| 🎨 [SkiaSharp](https://github.com/mono/SkiaSharp) | 跨平台图形渲染 |

## 📜 许可证

本项目基于 **MIT** 开源协议发布。

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

请阅读 [LICENSE](LICENSE) 获取完整条款。

> ⚠️ 软件按"原样"提供，作者不对使用后果承担责任。

## 📈 Star History

<a href="https://www.star-history.com/?repos=shunnet%2FVisualIdentity&type=date&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&theme=dark&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=shunnet/VisualIdentity&type=date&legend=bottom-right&sealed_token=jvjH1AZFSXflOGVE7gveyIW2Bq008loM9hOu9VceYDivd2bPkD0fEyfe8zFiqRkP-XIlgwg-b5OQTyLQq9rBBx_ERIk7NBQmgWubF8Akb13yd8u0s1ZBLA" />
 </picture>
</a>
