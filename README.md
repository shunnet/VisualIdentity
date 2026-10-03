<h1 align="center">🔍 Snet.VisualIdentity</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/><br/>
</p>

<p align="center">
  <b>基于 .NET 10 的 YOLO + Anomalib 工业视觉检测平台</b>
</p>

<p align="center">
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

## 📑 目录

| | | |
|---|---|---|
| [🌟 项目简介](#-项目简介) | [🎯 应用场景](#-应用场景) | [🏗️ 项目架构](#-项目架构) |
| [⚡ 快速开始](#-快速开始) | [🏷️ Tasks 工作台](#-tasks-web-标注与训练工作台) | [🎬 视频与 FFmpeg](#-视频验证的-ffmpeg-部署) |
| [🖥️ 界面展示](#-界面展示) | [📦 NuGet 安装](#-nuget-安装) | [🔌 API 接口](#-api-接口文档) |
| [⚙️ 配置文件](#-配置文件) | [🧠 支持的任务](#-支持的任务) | [🖥️ 执行提供者](#-执行提供者) |
| [🐳 Docker 部署](#-docker-部署) | [🧪 测试](#-测试) | [🔒 安全特性](#-安全特性) |
| [🪄 SAM 辅助标注](#-sam-点选辅助标注) | [🧩 联合验证 Demo](#-联合验证控制台-demo) | [📚 依赖组件](#-依赖组件) |

## 🌟 项目简介

**VisualIdentity** 是基于 **.NET 10** 的视觉检测平台，提供项目管理、图片标注、模型训练、ONNX 验证与 API 服务。**YOLO** 支持目标检测、分类、实例分割、姿态估计和定向检测五类任务，用已标注的数据识别目标或已知缺陷；**Anomalib** 使用正常图片训练异常检测模型，定位与正常状态不同的区域，无需逐一标注缺陷类型。两类模型分别管理，共用 `Snet.Yolo.Server` 中的识别能力，并提供 CPU 与 CUDA 版本。**SAM** 为矩形、多边形和笔刷提供点选物体的辅助标注，支持 MobileSAM、SAM 2.1 Tiny、SAM ViT-B/L/H，结果经人工确认后保存。

在工业质检中，可以先用 Anomalib 回答「异常在哪里」，再用训练过对应缺陷类别的 YOLO 回答「这是什么缺陷」。Tasks 的「联合验证」页可自动选取当前用户已有的两类模型，对同一张图片依次识别并关联结果；也可分别运行单模型，对照识别效果。

验证链路直接调用同进程的 `Snet.Yolo.Server`，不经 HTTP API：YOLO 模型的上传登记与按用户查询复用 `ManageOperate`，识别由 Server 的 YOLO 推理服务执行；Anomalib 模型包由 `AnomalibModelRegistry` 管理，推理由 Server 的 ONNX 组件执行。联合验证也在 Server 内查询两类模型、执行识别并关联结果。Tasks 负责页面、上传进度、当前用户身份和视频调度。

> 💡 当前解决方案统一使用 **.NET 10**；WPF 工具目标框架为 `net10.0-windows`。

### ✨ 核心特性（功能总览）

#### 🧠 识别与模型

| 特性 | 说明 |
|------|------|
| 🎯 **YOLO 五类任务** | 对象检测 · 定向检测 (OBB) · 图像分类 · 实例分割 · 姿态估计，统一管理、按需切换 |
| 🔎 **Anomalib 异常定位** | 仅用正常图片训练 PaDiM 或 EfficientAD Small，验证时返回异常分数、区域及热图 |
| 🧠 **独立模型管理** | YOLO 模型由 SQLite 管理；Anomalib 模型按用户隔离注册，支持导入、下载和删除 ONNX 模型包 |
| 🖱️ **图片验证** | YOLO 与 Anomalib 各有独立验证页；联合验证页提供联合识别、仅 Anomalib、仅 YOLO 三种模式 |
| 🔍 **大图查看器** | 点击缩略图查看原图，支持缩放、拖动与翻页；YOLO 结果查看器还可切换原图 |
| 🎬 **视频验证** | YOLO 按标签汇总识别结果；Anomalib 逐帧统计异常并生成区域标注视频 |
| ⚡ **多硬件执行** | YOLO 支持 CPU、NVIDIA CUDA / TensorRT；Anomalib ONNX 识别支持 CPU / CUDA，Tasks 与 API 复用 Server 核心逻辑 |
| 📊 **识别统计** | 显示推理耗时、目标置信度、异常区域与视频进度；实际吞吐取决于模型和硬件 |

#### 🏷️ Tasks Web 工作台

| 特性 | 说明 |
|------|------|
| 🏷️ **双模型工作台** | YOLO 完成工程管理、标注、训练与验证；Anomalib 完成正常图上传、异常模型训练与验证 |
| 📤 **上传进度** | 上传中心在同一浏览器连接内保留任务，切页可查看进度和取消；刷新不保证续传 |
| 🗂️ **刷新恢复** | YOLO 按用户及模型保存验证队列；Anomalib 与联合验证按用户保存文件、选中项、参数和结果；仅在当前应用进程内恢复 |
| 🎞️ **图片与视频验证** | 一次最多 100 个文件；视频后台逐帧识别、实时显示进度与预计剩余时间 |
| 📥 **增量导入** | YOLO ZIP 可一批批上传：同名类别复用、新类名追加、标注下标自动重映射到工程标签 |
| 🪄 **SAM 辅助标注** | 点选前景、Shift 点选背景，Enter 确认、Esc 取消；模型、开关和设备偏好可恢复 |
| 🐍 **模型导出** | Tasks 导出训练得到的 YOLO ONNX；独立 Python 脚本支持指定权重、格式、opset 与设备 |
| 🧩 **联合验证 Demo** | 独立控制台通过 Core → Server 调用，保存标注图、热图和结构化结果，便于平台接入 |
| 🖥️ **WPF 调试工具** | YOLO 五类任务可视化验证与数据整理；SAM、Anomalib 和联合验证页面位于 Tasks |

#### 🏋️ 训练

| 特性 | 说明 |
|------|------|
| 🔢 **默认 500 轮** | YOLO 与 EfficientAD 的轮数滑块为 100–10000、步长 100；PaDiM 是特征统计模型，不显示轮数设置；YOLO 可提前停止 |
| 🎯 **默认不切验证集** | YOLO 默认使用全部训练数据；可在训练配置里勾选「使用验证集（自动划分 10%）」 |
| 🩺 **训练前体检** | 日志给出每类实例数、图片数、目标像素尺寸与验证集大小，提示缺类、小目标和数据量风险 |
| 🔬 **训练后自检** | 读取 `results.csv` 指标，在指标异常或验证样本不足时以置信度 0.25 对训练集自检；这是诊断信息，不能代替独立测试集的效果评估 |
| 📥 **权重下载命令** | 企业代理拦截 GitHub 时（curl 60），日志直接给出按系统生成的 `curl` 命令（自动带上代理与 CA 参数），下完即被复用 |
| 🔎 **Anomalib 训练门禁** | 训练后导出 ONNX，检查与训练模型的一致性，并阻止正常图校准集误报率超过 5% 的模型注册 |

#### 🚀 部署与运维

| 特性 | 说明 |
|------|------|
| 🌍 **多平台发布** | WPF 支持 Windows；Tasks/API 发布 Windows 与 Linux 包，并提供 Linux Docker 镜像 |
| 🛠️ **FFmpeg 自检** | 上传视频即自检：Windows 弹窗选择（手填路径 / 静默下载安装），Linux 直接用 apt 全局安装，失败不影响图片流程 |
| 🔤 **中文字体绘制** | 视频标注使用 CJK 字体；Linux 可尝试安装，离线/无权限环境需手动提供字体 |
| 📦 **开箱即用** | CPU 与 CUDA/TensorRT 产品独立运行；核心库与执行提供程序也可作为 NuGet 依赖使用 |

#### 🔒 安全与性能

| 特性 | 说明 |
|------|------|
| 🔒 **明确安全边界** | Tasks 使用 Cookie 登录与 CSRF 防护；API 按产品要求匿名，并提供限流、CORS 与安全响应头 |
| 🔐 **Tasks 按用户隔离** | 工程、标注、模型、验证数据与文件按登录用户隔离，训练环境共享；API 模型使用独立服务账户 |
| 🔄 **会话生命周期** | API YOLO 缓存会话；Anomalib 在推理服务内缓存；SAM 最多驻留一套会话；Tasks 图片 YOLO 验证每次创建并释放会话 |
| 🧵 **异步任务处理** | 上传、训练调度与文件读写采用异步处理，耗时视频识别可查看进度并取消 |

> 📖 各项细节见下方对应章节：[Tasks 工作台](#-tasks-web-标注与训练工作台) · [视频与 FFmpeg](#-视频验证的-ffmpeg-部署) · [配置文件](#-配置文件) · [安全特性](#-安全特性) · [性能优化](#-性能优化)

## 🎯 应用场景

💡 以下是可基于模型能力实现的应用方向，不是内置业务系统；跟踪、告警、医疗判断等仍需业务模型、规则与现场验证。

| 场景 | 用途 | 推荐模型类型 |
|------|------|------------|
| 🏭 **工业质检** | 先定位可疑区域，再识别已知缺陷类型；也可进行异物识别、零件计数 | Anomalib、检测、分割 |
| 🛒 **零售分析** | 顾客行为追踪、货架商品检测 | 检测、分类 |
| 🛡️ **智能安防** | 异常行为监测、跌倒检测、区域入侵 | 姿态估计、检测 |
| 🚗 **自动驾驶** | 道路目标检测、交通标志识别 | 定向检测、检测 |
| 🏥 **医疗影像** | 病灶分割、细胞分类 | 分割、分类 |
| 📄 **文档分析** | 旋转文本检测、表格识别 | 定向检测 |
| 🌐 **边缘计算** | Windows/Linux x64 CPU、Linux ARM64 CPU；CUDA 发布面向 x64 NVIDIA 环境 | CPU、CUDA |

## 🏗️ 项目架构

```
VisualIdentity/
├── YoloDotNet/                    # 🧠 ONNX 模型解析、预处理与后处理
├── YoloDotNet.ExecutionProvider.Cpu/  # 🖥️ CPU 执行提供程序
├── YoloDotNet.ExecutionProvider.Cuda/ # 🎮 CUDA / TensorRT 执行提供程序
├── Snet.Yolo.Server/              # 🗄️ SQLite、YOLO / anomalib / sam 推理与联合匹配
├── Snet.Yolo.Api.Shared/          # 🔗 共享 API 层（Shared Project：控制器 / 安全 / 图片处理）
├── Snet.Yolo.Api.Cpu/             # 🖥️ CPU API（HTTP 5157 · HTTPS 7257）
├── Snet.Yolo.Api.Cuda/            # 🎮 CUDA / TensorRT API（HTTP 5158 · HTTPS 7258）
├── Snet.Yolo.Tasks.Core/          # 🏷️ 标注配置、编辑、导出与训练领域逻辑
├── Snet.Yolo.Tasks.Shared/        # 🔗 Tasks 共享项目（Blazor 组件、服务与静态资源）
├── Snet.Yolo.Tasks.Cpu/           # 🖥️ CPU Tasks（HTTP 5151 · HTTPS 7351）
├── Snet.Yolo.Tasks.Cuda/          # 🎮 CUDA / TensorRT Tasks（HTTP 5152 · HTTPS 7352）
├── Snet.Yolo.Tool/                # 🛠️ WPF 桌面调试工具
├── Snet.Yolo.Test/                # 🧪 xUnit 回归与集成测试
├── Snet.VisualIdentity.JointVerificationDemo/ # 🧩 独立联合验证控制台与解决方案
├── Snet.Py/                       # 🐍 Python 模型导出脚本
├── .github/workflows/release.yml   # 🚀 构建、测试、安装包与 GHCR 发布
├── docker/                        # 🐳 Tasks / API 的 CPU 与 CUDA 镜像定义
├── VisualIdentity.sln             # 🧩 解决方案入口
└── appsettings.json               # ⚙️ API 共用配置
```

📌 上述端口来自开发启动配置 `launchSettings.json`，发布部署请显式设置监听地址；不会固定继承开发端口。

### 🔄 数据流

```
Tasks（登录用户）
├─ YOLO 验证 → ManageOperate → YoloValidationService → IdentityOperate → YoloDotNet
├─ Anomalib 验证 → AnomalibModelRegistry → AnomalibOnnxInference
├─ 联合验证 → JointValidationService → Anomalib → YOLO（异常时）→ JointValidationMatcher
└─ SAM 辅助标注 → SamModelStore / SamOnnxRuntime → 掩码 → 确认后保存标注

HTTP API（独立宿主、匿名入口）
├─ YOLO → 限流 / 参数校验 → ManageOperate → 缓存推理会话 → JSON
│                                            └─ IdentityDrawAsync → 图片与详情落盘
└─ Anomalib → 限流 / 参数校验 → AnomalibModelRegistry → ONNX → JSON / 可选热图
```

## ⚡ 快速开始

### 🧰 前置要求

- 📦 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 🧠 直接验证或 API 推理需要业务模型：YOLO ONNX，或 Anomalib ONNX + 配套清单；也可先在 Tasks 训练。
- 🐍 训练需要 Python 3、pip、venv；SAM 标注与 ONNX 验证不依赖 Python 训练环境。
- 🎮 CUDA 推理需要兼容 NVIDIA 驱动及 CUDA/cuDNN；视频验证另外需要 FFmpeg/FFprobe。
- 🗄️ 应用运行账户需要对数据、模型、训练、SAM 和工具目录有写权限。

### 1️⃣ 克隆仓库

```bash
git clone https://github.com/shunnet/VisualIdentity.git
cd VisualIdentity
```

### 🏷️ 使用 Tasks Web 工作台

```bash
# CPU 版本
dotnet run --project Snet.Yolo.Tasks.Cpu
```

浏览器访问 `http://localhost:5151`。首次启动会创建默认管理员 `snet`，默认密码为 `123456`。可在启动前通过 `SNET_BOOTSTRAP_ADMIN_PASSWORD` 覆盖密码；现有管理员无法登录时，也可设置该变量并重启，以同步管理员密码。

> 🔐 部署前请设置非默认管理员密码，并通过 HTTPS 或受信任的反向代理提供访问。
> 🐍 Tasks 为 YOLO 与 Anomalib 分别建立共享虚拟环境；训练设备由 Python/PyTorch 决定，不由启动 CPU/CUDA 宿主决定。

🖥️ 需要指定验证推理硬件时，改为启动对应项目：

| 项目 | 执行提供程序 | 适用平台 |
|---|---|---|
| `Snet.Yolo.Tasks.Cpu` | `YoloDotNet.ExecutionProvider.Cpu` | 通用 CPU |
| `Snet.Yolo.Tasks.Cuda` | `YoloDotNet.ExecutionProvider.Cuda` | NVIDIA CUDA / TensorRT |

💡 例如：`dotnet run --project Snet.Yolo.Tasks.Cuda`。CPU 与 CUDA 项目共同导入 `Snet.Yolo.Tasks.Shared`，仅执行提供程序不同；训练环境仍然共享。

### 2️⃣ 运行 CPU 版本 API

```bash
cd Snet.Yolo.Api.Cpu
dotnet run
```

🌐 浏览器访问 `http://localhost:5157/swagger` 查看 Swagger UI（仅 Development 环境）。

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

## 🏷️ Tasks Web 标注与训练工作台

🧩 `Snet.Yolo.Tasks.Shared` 提供解决方案内置 Blazor Web 工作台的共享实现，CPU 与 CUDA 项目复用同一套界面、业务服务与静态资源；CPU 环境使用 `Snet.Yolo.Tasks.Cpu`。工作台覆盖从数据准备到模型验证的完整流程：

1. 🔐 登录后创建工程，选择检测、分割、分类、姿态估计或 OBB 任务模板。
2. 🖼️ 导入图片并在浏览器中完成矩形、旋转框、多边形、关键点或分类标注。
3. 📦 导出 YOLO 标签，或导出同时包含原图的 YOLO ZIP 数据集。
4. ⚙️ 配置轮数、图像尺寸、基础模型与设备，实时查看训练阶段、指标和日志。
5. 🚀 下载训练得到的 `best.pt`，或导出 ONNX 并直接进入验证页推理。

### ✏️ 图片多边形与曲线标注

手动多边形绘制时点击起点、双击或按 Enter 闭合。使用选择工具选中多边形后显示顶点/曲线操作按钮；方形把手拖动顶点，圆形把手拖动曲线控制点。删除顶点至少保留三个，相邻边重新连接为直线。Esc 取消拖动，Ctrl+Z / Ctrl+Shift+Z 撤销/重做。

保存的标注 JSON 在 `value.snet_bezier` 中保留百分比控制点，重新打开可以继续编辑。YOLO 分割标签与 COCO 导出自动将曲线细分为多边形（原图像素误差不超过 0.5px），不会修改原始标注；不识别该扩展字段的第三方 JSON 工具只会读取锚点。本功能仅用于图片，不包含视频跟踪。

### 🪄 SAM 点选辅助标注

模型、辅助标注开关和所选设备按登录用户保存在当前浏览器，刷新后自动恢复；原 GPU 不可用时恢复为 CPU。不保存提示点或未确认的预览。浏览器禁用本地存储时，这些偏好不能跨刷新保留。

**运算设备**：五种 SAM 模型均可选择 CPU 或单张 NVIDIA GPU。界面自动检测 GPU 编号、名称及显存，沿用训练配置的设备按钮与硬件卡片，可手动指定 GPU；默认 CPU。CPU 发行包仅开放 CPU，GPU 需 CUDA 发行包及兼容驱动、CUDA/cuDNN。选择 GPU 后初始化失败会明确报错，不静默降级为 CPU；部分不支持的 ONNX 算子仍可能由 CPU 执行。当前交互式单图推理不支持多 GPU 联合运算，因此不提供多选。切换设备会清除预览和图片编码，下次点选在所选设备创建会话；服务端仍最多驻留一套会话，多用户切换模型或设备会产生重新加载开销。

工具下方开启 **SAM 辅助标注**，选矩形、多边形或笔刷，点击物体生成预览；继续点击补充前景，按住 Shift 点击排除背景，最后点击「确认标注」。矩形采用物体外接框，多边形采用简化外轮廓，笔刷采用实心掩码。关闭 SAM 恢复手动工具。SAM 不是类别识别，也不保证一次点击就精确贴边；结果应人工检查。

工具下方可以自行选择 **MobileSAM（默认） / SAM 2.1 Tiny / SAM ViT-B / SAM ViT-L / SAM ViT-H**，界面同时显示文件体积和资源提示。由 `Snet.Yolo.Server` 直接执行 ONNX 推理，不经过 API，不需要 Python 环境。CPU/CUDA 宿主复用现有 ONNX 硬件配置；同一图片、同一模型复用编码，切图或换模型会清除未确认预览与页面编码，取消未完成请求。首次下载会查询上游，选择当前程序已验证目录中的最新兼容权重，锁定实际仓库提交并校验压缩包、ONNX 及外部权重的 SHA-256；失败可重新开启重试。服务端最多驻留一套模型会话，加载新模型前释放旧会话，避免多套同时占用内存/显存；多用户选择不同模型时切换会增加加载耗时。

**版本与更新**：开启 SAM 后显示已安装的提交版本，提供「检查更新」；存在兼容新权重时显示「更新模型」，更新成功后提供「回退上一版本」。已有安装不会自动覆盖。相同权重的新提交可锁定其最新提交；上游权重变化但未进入兼容目录时，只提示尚未验证，不自动使用。维护者需完成 ONNX 接口与效果验证、保留历史目录记录，并随程序发布更新兼容目录；“最新兼容”不是任意上游最新版，也不保证效果更好。当前每种模型有一个已验证权重版本，因此没有新兼容权重时不会显示更新按钮。查询上游失败时首次下载退回程序内推荐的固定版本；已有版本保持不变。

版本记录位于各模型目录的 `active-model.json`，记录当前与上一版本；手动更新下载到 `.versions/<实际提交 SHA>/`，通过完整性校验、所选 CPU/GPU 的编码/解码试运行后才原子切换记录。下载、验证或取消失败保留原版本；未激活的已下载文件作为重试缓存保留。旧文件不删除，供回退及其他页面已编码图片继续使用。更新是服务器共享操作，影响全部用户；发起更新的页面清空未确认预览与编码，其他页面已有图片编码继续绑定原解码器，新图片使用当前版本。保留上一版本与候选版本会额外占用磁盘空间；离线部署仍可按下方目录提供已验证文件或 ZIP。

| 模型 | ONNX 文件合计 | 单图特征缓存 | 资源提示 |
| --- | --- | --- | --- |
| MobileSAM | 约 45 MB | 约 4 MB | 资源占用低，默认推荐，适合 CPU/低显存设备 |
| SAM 2.1 Tiny | 约 126 MB | 约 16 MB | 资源占用中等，建议 GPU，CPU 首次处理更慢 |
| SAM ViT-B | 约 376 MB | 约 4 MB | 权重及中间计算占用高，优先使用 GPU，CPU 首次编码较慢 |
| SAM ViT-L | 约 1251 MB | 约 4 MB | 更高内存/显存开销，建议 GPU，CPU 编码较慢 |
| SAM ViT-H | 约 2567 MB | 约 4 MB | 资源开销最高，建议资源充足的 GPU，低显存可能内存不足 |

**文件体积和特征缓存不是整体内存/显存需求**：运行时还有模型权重、中间计算和原图占用，实际依赖硬件、执行后端与图片尺寸。下载/解压还需要额外磁盘空间（Tiny 压缩包约 117 MB，ViT-B 约 349 MB，ViT-L 约 1162 MB，ViT-H 约 2384 MB）。ViT-H 的外部权重文件必须与编码器放在同一目录。更大不保证每张图更准确，请用实际图片比较。所有用户共享程序根目录下的 `sam`，不放进 `train` 或用户工程目录：

```text
程序根目录/
└── sam/
    ├── mobile_sam_image_encoder.onnx
    ├── sam_mask_decoder_multi.onnx
    ├── sam2.1-tiny/
    │   ├── sam2.1_hiera_tiny.encoder.onnx
    │   └── sam2.1_hiera_tiny.decoder.onnx
    ├── sam-vit-b/
    │   ├── sam_vit_b_01ec64.encoder.onnx
    │   └── sam_vit_b_01ec64.decoder.onnx
    ├── sam-vit-l/
    │   ├── sam_vit_l_0b3195.encoder.onnx
    │   └── sam_vit_l_0b3195.decoder.onnx
    └── sam-vit-h/
        ├── sam_vit_h_4b8939.encoder.onnx
        ├── sam_vit_h_4b8939.encoder_data.bin
        └── sam_vit_h_4b8939.decoder.onnx
```

离线部署可从 [Acly/MobileSAM 固定版本](https://huggingface.co/Acly/MobileSAM/tree/0d3b403339b4674a82493d5e97964dd78089ddc8) 下载这两个同名文件后复制到 `sam`（目录需有写权限）；应用仍会校验，文件不匹配时保留原文件并提示处理。代理网络必须配好受信任证书，不关闭 TLS 校验。模型源自 [MobileSAM](https://github.com/ChaoningZhang/MobileSAM)，ONNX 转换版本由 Acly 提供；分发模型时遵循其上游许可。

新增模型采用 [SAM 2.1 Tiny 固定权重包](https://huggingface.co/vietanhdev/segment-anything-2.1-onnx-models/blob/6a3ac868340a3196a349050a6efae22a5acc0330/sam2.1_hiera_tiny_20260221.zip) 和 [SAM ViT-B 固定权重包](https://huggingface.co/vietanhdev/segment-anything-onnx-models/blob/9effc01a9e135621d710d49159f1ffb0b6f724dc/sam_vit_b_01ec64.zip)，由 vietanhdev 提供 ONNX 导出，原始模型为 Meta SAM/SAM 2.1。离线时可将 ZIP 放入对应子目录，应用校验后只解压固定名称的 ONNX 和所需外部权重；也可按上述目录手动放置已解压文件。不会执行 ZIP 中的配置或代码。离线提供的 ZIP 保留；本次自动下载的 ZIP 成功解压后清理。 ViT-L 与 ViT-H 分别使用同一固定仓库版本中的 [sam_vit_l_0b3195.zip](https://huggingface.co/vietanhdev/segment-anything-onnx-models/blob/9effc01a9e135621d710d49159f1ffb0b6f724dc/sam_vit_l_0b3195.zip) 与 [sam_vit_h_4b8939.zip](https://huggingface.co/vietanhdev/segment-anything-onnx-models/blob/9effc01a9e135621d710d49159f1ffb0b6f724dc/sam_vit_h_4b8939.zip)。ViT-H 三个模型文件均须完整，启动时逐一校验；大模型单次下载超时为 30 分钟，可取消。

图片最多 2400 万像素，推理最长边 1024；最多 64 个前景/背景点，首点必须为前景；极小缺陷可能需要手动修正。矩形、多边形可继续编辑；SAM 笔刷支持选择、删除、撤销/重做和保存回显，不支持整体拖移或修改原掩码。笔刷 RLE 保留孔洞，YOLO 单多边形标签仅导出外轮廓，不表达孔洞。

### 🔎 业务流程：先找哪里异常，再判断是什么缺陷

工业现场可以把 Anomalib 和 YOLO 当成分工不同的两位检查员：先用正常图片训练 **Anomalib**，让它在新图片中标出与正常状态不同的可疑区域，回答「异常在哪里」；再用已标注缺陷类别的图片训练 **YOLO**，在同一图片中识别划痕、裂纹、异物等已知缺陷，回答「这是什么」。把位置和类别结合起来，便于复核、记录和处理。

Anomalib 不需要预先收集每一种缺陷样本，但发现异常不等于知道缺陷名称；YOLO 能给出训练过的类别，但不保证识别从未学过的缺陷。在「联合验证」页，Anomalib 先定位异常；如果图像被判为异常，YOLO 对原图识别一次，再把检测框与异常区域按位置关联。YOLO 没有匹配到的异常仍显示为「类型未识别」，不会改判正常。该级联目前用于**图片**；视频仍使用各自的独立验证流程。

联合验证的模型设置可调整识别参数：Anomalib 提供区域异常阈值和最小异常面积；YOLO 参数与独立验证页一致，目标检测显示置信度和 IoU，实例分割额外显示像素置信度。页面将当前参数传给 `Snet.Yolo.Server`，修改参数后需重新识别。

联合验证支持多选上传图片（最多保留 500 张），通过与 YOLO 验证页一致的缩略图列表切换图片。点击图片自动识别，默认联合识别；执行「仅 Anomalib」或「仅 YOLO」后，后续点击沿用该模式。每张图片独立保留结果、热力图与耗时，刷新可恢复图片列表及当前选中项；修改模型或参数会清除全部旧结果，避免不同配置的结果混用。

Anomalib 独立验证也提供区域过滤参数（初始区域阈值 0.80、最小面积 4 个异常图像素，非原图像素）。区域异常阈值使用数值输入，联合验证的模型设置区支持滚动查看全部参数。提高参数可减少零碎框，但可能漏掉细小缺陷；它们不改变模型的整图正常/异常判断，也不改变训练注册的 5% 误报门禁。Anomalib 与联合验证按登录用户保留已上传文件、模型选择、已完成结果、日志及参数，刷新页面可恢复；识别中的任务在离开页面时取消，不自动续跑。状态保存在应用内存中，应用重启后清空；移除文件才删除对应源文件，继续上传不会删除之前的图片。

### 🧩 Anomalib 项目与模型验证

在侧栏进入「Anomalib 项目」，新建独立工程并上传至少 10 张内容不同的**正常图片**，无需画框或提供缺陷标签。工程详情页支持点击缩略图查看原图、翻页浏览（每页 150 张，页码写入 URL，刷新后恢复），并显示训练阶段与状态。训练页沿用 YOLO 的进度、日志和右侧硬件资源布局；点击「开始训练」后，在弹窗中选择 PaDiM（默认）或 EfficientAD Small，并配置输入尺寸和设备。选择模型时下方会显示该算法的特点。YOLO 工程详情页也提供「模型训练」入口。Tasks 首次运行会建立独立的 `train/anomalib/.env`，不改动 YOLO 训练环境；训练完成后导出 ONNX，并用本次训练的内存模型与 ONNX 比较校准图片结果（不反序列化 `.pt`）。只有一致性与正常图误报门禁都通过的模型才会注册并出现在「Anomalib 验证」。

🧠 **模型与输入尺寸**：PaDiM 按画面位置建立正常特征分布，适合相对固定的机位；EfficientAD Small 使用教师—学生网络，侧重快速推理。训练和添加模型弹窗仅提供 PaDiM、EfficientAD Small；后端仍保留 PatchCore 实验类型，不属于当前界面支持范围。输入尺寸是训练和推理时缩放后的正方形尺寸，新配置默认 `640 × 640`，可设为 128–2048 之间的 32 的倍数；修改后需重新训练。增大尺寸能保留更多小目标细节，但会增加资源开销，也不保证减少误检。

🖼️ **验证与模型文件**：验证页采用与 YOLO 相同的模型、结果、文件和预览分区，支持批量图片与视频。图片显示异常热图和原图坐标区域；视频需 FFmpeg/FFprobe，逐帧识别并生成可播放的区域标注视频，可查看进度并取消（单文件不超过 100 MiB、视频不超过 10000 帧）。模型可从列表下载或删除；下载得到包含 `model.onnx` 和 `model.manifest.json` 的 ZIP，添加模型时可在弹窗中填写名称、描述、类型并上传这种 ZIP。Anomalib 四个页面支持中英文切换，第三方训练原始日志保持原文。

> ⚠️ 注册前会检查训练模型与 ONNX 的一致性，并要求留出的正常图片校准集误报率不超过 5%；超过阈值则不注册。通过门禁仍**不代表缺陷检出率合格**，应使用独立的正常图和已知异常图检查误报、漏检。尤其使用 PaDiM 时，应尽量保持机位和检测区域稳定。不通过门禁的模型不会提供推理。实际训练需本机 Python 3 和模型依赖可用，首次安装可能需要联网。

EfficientAD 首次训练还会下载预训练教师权重和 ImageNette 数据。若在 WSL 中遇到 `CERTIFICATE_VERIFY_FAILED`，应检查 WSL 内的代理和 CA 信任（Windows 的证书信任不会自动解决 WSL 内 Python 的证书错误）；可将 `Training:CaBundle` 配置为 WSL 内可读取的 PEM 证书包路径，重启服务后 Anomalib 训练子进程会继承该配置。不要通过关闭 TLS 证书校验解决。

### 📤 上传中心

🧭 `UploadCenter` 管理工程图片（含 Anomalib 正常图）、分类图片、YOLO ZIP、YOLO 验证文件和 YOLO ONNX 上传。Anomalib 验证模型包、验证文件及联合验证图片由各页面单独处理，不共用这个上传通道：

| 特性 | 说明 |
|---|---|
| 🔄 **切页不中断** | 同一 Blazor 电路内切页或重绘可恢复进度；浏览器刷新或断开电路不保证上传续传 |
| 📊 **进度可见** | 横幅显示当前文件名、字节数、百分比与「已完成 / 总数」，完成后自动收起 |
| ⏹️ **可取消** | 流式读写检查取消；工程导入未提交时回滚本批文件和标注，验证上传保留已成功文件 |
| 🧹 **失败处理** | 验证文件逐个报告失败并继续；工程图片和 YOLO ZIP 导入按批次提交，失败时回滚 |

### 🖼️ 验证页

📤 YOLO 与 Anomalib 验证每次最多选择 100 个图片/视频。YOLO 每个用户的每个模型最多保留 500 个文件；Anomalib 每个用户最多保留 500 个文件。无模型选择时显示「请选择模型」，选中后展示识别区；模型管理和识别均直接走 Server。以下交互以 YOLO 验证页为主，Anomalib 显示异常区域和视频异常统计：

| 交互 | 说明 |
|---|---|
| 🖱️ **点击图片** | 载入后**自动执行识别**，省掉「选图 → 点识别」两步 |
| 🎬 **视频** | 解码耗时较长，仍由「识别」按钮触发；状态条实时显示阶段、帧进度与预计剩余时间 |
| ⏹️ **随时取消** | 排队中的视频任务会被跳过，执行中的会中断抽帧/逐帧推理/编码（并结束 ffmpeg 进程） |
| 🔍 **双击大图** | 打开查看器：滚轮以光标为锚点缩放、按住拖动、双击或按钮还原、顶部「原图」勾选切换原图/标注图、Esc 或点窗外关闭 |
| 📋 **结果聚合** | 视频按标签汇总为「平均置信度 + 全片识别次数」；照片保持逐目标显示坐标 |

> 📌 验证数据（文件队列、选中项、识别结果）仅保留在当前应用进程内，正常关闭或重启 Tasks 后会清空，不写入业务数据库。

### ✏️ 编辑标签的同步语义（标签配置 = 唯一数据源）

平台的标签配置（工程里的 `LabelConfigXml`）是**唯一权威数据源**：标注画布、区域列表、工具栏、统计、导出、训练类表全部**在读取时从它派生**，所以结构上不会出现"某处还记着旧标签"的漂移。删除/改名这类**存量标注上的改动**则在**保存时一次性收敛**到所有已有标注：

| 操作 | 行为 |
|---|---|
| ✏️ **改名** | 所有引用该标签的标注框都显示新名字，导出/训练用的类名一起变 |
| 🎨 **改色** | 颜色只存在于标签配置里（标注框只记名字），所以标注页、区域列表、画布全部即时生效 |
| 🗑️ **删除标签** | 该标签的**所有标注框一并删除**（画布不再显示、统计不再计数、导出也不会静默丢数据），并且**不会保留位置**：后面的标签下标整体前移 |
| 🧾 **下标变化提示** | 删除确认框会明确提示"其余标签的下标会前移"；类别顺序变化会影响导出与训练的类名顺序 |
| 🔁 **重新导入不受影响** | YOLO ZIP 导入按**类名**匹配（同名的复用、新的追加），包里的 id 顺序随便排，所以下标前移不会让旧数据错位 |

> 💡 想让类别保持稠密、没有空类别：直接删除用不到的标签即可；工程外若有按下标对齐的快照（如旧训练记录），删完后按新顺序重新导出一次。
#### 🖼️ 验证页大图预览（原图不压缩）

验证页上传的图片**原样保存**（不做任何加工，上传就是你原本的速度）。为了显示不卡顿，服务端会为图片生成一张小预览：

| 环节 | 行为 |
|---|---|
| ⬆️ **上传** | 原图直接落盘，服务端零加工 ✓ |
| 🔥 **后台预热** | 上传成功后有界并发生成预览，失败不影响原图识别；耗时取决于图像尺寸和硬件 |
| 🖼️ **页面显示** | 文件列表、主视图、画布叠加全部用预览（**75 MB 的 BMP → 约 370 KB**），浏览器不再解码 5120×5120 大位图 ✓ |
| 🔍 **双击查看器** | **直接用原图**（要看就是看清细节，放大后依然清晰）；原图万一取不到才退回预览 ✓ |
| 🗑️ **删除** | 原图与预览一起删除；应用停止时会清理本进程创建的验证文件 ✓ |

> 💡 为什么不能"只靠 CSS 缩小显示"：浏览器必须先**解码整张位图**才会缩小 —— 5120×5120 单张就要约 100 MB 内存，列表里几张就够把主线程卡住（心跳发不出去还会被判定断线）。预览把解码成本从 100 MB 降到几 MB，这才是"丝滑"的关键。

| 配置（`appsettings.json`） | 默认 | 说明 |
|---|---|---|
| `Images:Preview:Enabled` | `true` | 关掉就直接显示原图（不生成预览） |
| `Images:Preview:MaxEdge` | `1600` | 预览最长边 |
| `Images:Preview:TargetBytes` | `409600` | 预览目标体积（400 KiB）；最低质量时仍可能超出 |
| `Images:Preview:StartQuality` / `MinQuality` | `82` / `60` | 预览 JPEG 质量区间 |

> 🔗 列表状态写进地址栏：项目详情的**页码、搜索词、展开的类别文件夹**（`?page=3&q=…&folder=…`）与用户管理的搜索词（`?q=…`）刷新后都会恢复，链接也能直接分享；标注页当前是第几张图本来就在路由里（`/labeling/{工程}/{序号}`）。
> 📌 覆盖范围：**所有"看一眼"的位置**都用预览（验证页列表与主视图、项目详情的图片表格与文件夹封面、分类图片网格）；**标注页画布与查看器大图仍加载原图**（标注需要像素级精度）。识别本身始终用原图。
> ⏳ 加载反馈：验证页查看器、标注页首次打开与「上一页/下一页」在解码大图期间都会显示加载动画（标注页还会预加载相邻图片，切页通常瞬间完成）。
> ⚠️ 识别框的 `Position` 坐标是**原图像素**空间（例如 5120），而画布放的是预览图（1600）：前端会按原图尺寸把框**等比缩放**到画布上，所以叠加框位置准确；页面同时把原图地址作为兜底，预览取不到时会自动退回原图继续绘制。
### 📥 YOLO ZIP 导入（可反复增量上传）

此导入入口仅支持**矩形目标检测**数据，不能导入分割、多边形、姿态、OBB 或分类标签。支持两种来源，上传到检测工程的「导入 YOLO ZIP」：

| 来源 | 包内结构 |
|---|---|
| 本应用导出 | `classes.txt` + `images/` + `labels/` |
| Roboflow / Ultralytics 导出（YOLOv5 / v8 / 11 / 26 等） | `data.yaml`（`names`、`nc`）+ `train`/`valid`/`test` 各自的 `images/` 与 `labels/` |

包内规则会逐个校验，不符合直接拒绝：

| 要求 | 说明 |
|---|---|
| 📄 类别表 | `classes.txt`（每行一个类名，或 `索引 名称`，索引必须从 0 连续）**或** `data.yaml` 的 `names`（行内列表 `['a', 'b']`、块列表 `- a`、映射 `{0: a}` 都认；写了 `nc` 会与类别数交叉校验）。两者同时存在时以 `classes.txt` 为准 |
| 🖼️ 图片 | 任意层级下 `images/` 目录里的 jpg / jpeg / png / gif / webp / bmp |
| 🏷️ 标注 | 同级 `labels/` 下的同名 `.txt`，每行 `类别 cx cy w h`（归一化 0~1，空文件 = 纯背景图）。**图片缺少对应 `.txt` 时按"无目标"导入**——YOLO 生态（含 Roboflow）就是用"没有 `.txt`"表示无标注图片；反过来"有标注却没有图片"仍然是错误 |
| 📏 体积 | 单图 ≤ 100 MiB、图片数 ≤ 100,000、单次上传 ≤ 16 GiB、解压后图片总量 ≤ 64 GiB |
| 🔁 重复条目 | 同一份 `data.yaml` 被重复写进包里（Roboflow 就会写三份）只要内容一致就接受；图片/标注路径重复一律拒绝 |

> 💾 ZIP 先保存到系统临时目录，图片再解压到工程目录。上传前会检查可用空间，但压缩率及并发写入会影响实际需求；不能按“两倍 ZIP 大小”保证足够。请为解压后的图片预留空间，失败时未提交的导入会回滚。

导入是**增量**的：可以一批批往同一个工程里传，标注持续累加：

| 情况 | 平台行为 |
|---|---|
| 包里的类名与工程已有标签**同名**（忽略大小写） | **复用**已有标签，连原有写法都保留，不新增 |
| 包里出现**新类名** | 追加为新标签，并分配不与现有颜色重复的颜色 |
| 标注里的类别下标 | 按「本包下标 → 工程标签名」**重映射**后写入，包的 id 顺序可以任意排列 |
| 重复导入同一批 | 标签不会重复（幂等）；但**图片会重复**，同一批请不要重复上传 |
| 工程自带模板标签（如 `Airplane` / `Car`） | 不会被删除；用不到就在标签编辑器里删掉，免得训练时多出零样本类别 |

> 💡 多批上传时**类名保持一致**是唯一需要人工守住的事——名字就是类别身份（"虫茧" ≠ "茧"）。

### 🏋️ 训练

🧠 YOLO 训练弹窗提供 YOLO26、YOLO11 的 Nano / Small / Medium / Large / XLarge，默认 `yolo26n.pt`，按任务自动选择 `-seg` / `-cls` / `-pose` / `-obb` 后缀。图片大小默认 640，滑块范围 32–4096，步长 32。Anomalib 输入尺寸范围为 128–2048，默认 640、步长 32；只有 EfficientAD 显示训练轮数滑块。

| 特性 | 说明 |
|---|---|
| 🔢 **默认 500 轮** | YOLO 与 EfficientAD 的轮数滑块为 100–10000、步长 100；PaDiM 是特征统计模型，不显示轮数设置；YOLO 可提前停止 |
| 🎯 **默认不切验证集** | YOLO 默认使用全部训练数据；可在训练配置里勾选「使用验证集」 |
| 🩺 **训练前体检** | 显示每类实例数、图片数、目标像素尺寸、验证集大小，提示数据质量风险 |
| 🔬 **训练后自检** | 读取 `results.csv`，在指标异常或验证样本不足时对**训练集**以置信度 0.25 自检并给出指标；自检失败不撤销训练产物，不代表真实场景合格 |
| 📥 **权重下载命令** | 检测到证书/下载类失败时，按当前系统生成可直接复制的 `curl` 命令（自动带上 `Training:Proxy` / `Training:CaBundle`），下完即被自动复用 |

🐍 训练环境（Python + venv + torch + ultralytics）由 Tasks 自动检测与搭建，代理与 CA 通过 `Training` 配置节统一控制。GPU 训练不会只判断“是否安装 torch”，还会核验 `torch.version.cuda` 与 `torch.cuda.is_available()`：Pascal / Volta / Turing 使用兼容面更广的 CUDA 11.8 wheel，Ampere 及更新架构在驱动满足 CUDA 12 要求时使用 CUDA 12.8 wheel；旧驱动自动选择兼容通道，无法安全使用 CUDA 的旧卡明确回退 CPU。

🎮 YOLO 与 Anomalib 训练弹窗会列出检测到的全部 NVIDIA GPU，可通过设备按钮选择 CPU、自动、一张或多张 GPU；训练页按设备分别显示 GPU 利用率与显存。YOLO 多卡使用 Ultralytics 分布式训练；Anomalib 的 EfficientAD 可选择多卡，PaDiM 只能选择单卡。Windows 原生 PyTorch 的多卡训练不受当前方案支持，请在 Linux / WSL2 使用；多卡数据并行不会把各卡显存合并成一块，单卡显存不足仍可能报错。开始训练前会校验所选设备是否对 PyTorch 可见，Anomalib 仍需通过 ONNX 一致性与正常图误报门禁才会注册模型。

📦 YOLO 训练与 AMP（自动混合精度）自检共用程序目录下的 `train/yolo/weights` 缓存。训练前通过 Ultralytics 设置接口配置权重路径，配置文件隔离在当前工程的 `ultralytics-config` 目录，不改用户全局设置；多卡子进程继承同一配置。将 `yolo26n.pt` 放进缓存后，AMP 自检可直接复用，仍保留自检；该自检文件尚未缓存时仍可能需要下载，即使训练选择了其他型号。

### 🎬 视频验证的 FFmpeg 部署

🎥 视频解码需要 `ffmpeg` 和 `ffprobe`（图片验证不依赖它们）。**上传视频时会自动自检**，缺失时：

| 平台 | 行为 |
|---|---|
| 🪟 **Windows** | 弹窗让用户选择：**手动指定路径**（填 `ffmpeg.exe` 或所在目录）或 **静默下载安装**（从 [GyanD/codexffmpeg](https://github.com/GyanD/codexffmpeg/releases) 取最新版，解压到程序目录 `tools/ffmpeg/win-<arch>/`），并在页面上显示下载/解压进度 |
| 🐧 **Linux（Ubuntu/Debian）** | 不弹窗，直接 `sudo -n apt-get install -y ffmpeg` 全局安装并显示进度；索引过期会自动 `apt-get update` 后重试；**失败才弹窗**（附手动指定路径兜底） |
| 🍎 **macOS / 其它** | 弹窗手动指定路径（或自行 `brew install ffmpeg` 后自动发现） |

📌 安装路径记录在 `tools/media-tools.json`。Linux 会尝试安装缺失的 `fonts-noto-cjk`，但依赖软件源、网络和安装权限；非 root 容器通常应在自定义镜像中预装或挂载可用字体。字体缺失可能导致中文方框。安装失败会提示，图片上传/识别仍可使用；缺少媒体工具时视频验证不能继续。

🔍 自动查找顺序（也支持完全手动）：

1️⃣ `MediaTools:FFmpegPath` / `MediaTools:FFprobePath` 配置。
2️⃣ `SNET_FFMPEG_PATH` / `SNET_FFPROBE_PATH` 环境变量。
3️⃣ 安装记录 `tools/media-tools.json`（手动指定或自动安装后写入）。
4️⃣ 应用目录下的 `tools/ffmpeg/<RID>/`，例如 `tools/ffmpeg/win-x64/` 或 `tools/ffmpeg/linux-x64/`。
5️⃣ 系统 `PATH` 及 Windows/Linux/macOS 常见安装目录。

#### 🪟 手动安装（Windows 10/11）

```powershell
# 🪄 winget（推荐）
winget install --id Gyan.FFmpeg --exact

# 🍫 或 Chocolatey
choco install ffmpeg

# ✅ 新开一个终端后验证两个命令
ffmpeg -version
ffprobe -version
```

💡 Windows Server 没有 `winget` 时，可从 [FFmpeg 官方下载页](https://ffmpeg.org/download.html) 选择 Windows 构建，解压后将 `bin` 目录加入 `PATH`，或将该目录填入 `MediaTools:FFmpegPath`。

#### 🐧 手动安装（Ubuntu / Debian）

```bash
sudo apt update
sudo apt install -y ffmpeg
# 🔤 中文字体（视频标注里的中文需要）
sudo apt install -y fonts-noto-cjk
ffmpeg -version
ffprobe -version
```

📦 `ffprobe` 由同一个 `ffmpeg` 软件包提供，不需要另外安装。Tasks 会自动完成上面两步，这里仅作为离线/无 sudo 权限时的兜底。

#### 🧩 手动安装（其他 Linux 发行版）

```bash
# 🎩 Fedora
sudo dnf install -y ffmpeg-free

# 🏔️ Arch Linux
sudo pacman -S ffmpeg

# 🏔️ Alpine Linux
sudo apk add ffmpeg

ffmpeg -version
ffprobe -version
```

💡 如果发行版软件源没有 FFmpeg，可将两个可执行文件放入发布目录的 `tools/ffmpeg/linux-x64/` 或 `tools/ffmpeg/linux-arm64/`，然后执行 `chmod +x ffmpeg ffprobe`；也可以通过 `SNET_FFMPEG_PATH` 与 `SNET_FFPROBE_PATH` 显式指定路径。

#### 🍎 手动安装（macOS）

```bash
brew install ffmpeg
ffmpeg -version
ffprobe -version
```

#### 🔧 显式配置路径

```json
{
  "MediaTools": {
    "FFmpegPath": "/opt/ffmpeg/bin/ffmpeg",
    "FFprobePath": "/opt/ffmpeg/bin/ffprobe",
    "InstallDirectory": "/opt/ffmpeg",
    "DiscoverInstalledTools": true
  }
}
```

📌 路径也可填包含这两个文件的目录（含解压常见的 `bin/` 子目录）。`InstallDirectory` 指定自动安装位置（默认程序目录下 `tools/ffmpeg`）；`DiscoverInstalledTools` 设为 `false` 时只认配置、安装记录与安装目录，便于固定使用某一套工具。如果工具缺失或显式路径错误，视频任务会立即停止并显示当前操作系统、CPU 架构和可用的配置方式，不会持续转圈。

🗄️ 工作台使用 SQLite 保存工程、用户和标注元数据。工程、标注任务、验证模型及数据库查询均按登录用户隔离；上传图片保存到 `wwwroot/data/uploads/<用户名>/`，ONNX 模型保存到 `wwwroot/onnxs/<用户名>/`，训练数据和产物分别保存到 `train/yolo/<用户名>/<项目>/` 和 `train/anomalib/<用户名>/<项目>/`，共享环境分别为 `train/yolo/.env` 和 `train/anomalib/.env`。不兼容或自动迁移旧训练目录，旧文件不会删除；YOLO 权重缓存和状态分别位于 `train/yolo/weights/` 和 `train/yolo/statuses/`。升级前已有的无归属数据自动归入 `snet`。删除任务或工程时会同步清理当前用户的对应文件。认证采用服务端 Cookie，会话页面、上传文件、模型下载和训练 Hub 均要求登录，普通用户不显示且不能访问用户管理页。

训练目录结构（`<项目>` 使用工程标识；各算法环境跨用户共享，工程数据按用户隔离）：

```text
train/
├── yolo/
│   ├── .env/
│   ├── weights/
│   ├── statuses/
│   └── <用户名>/<项目>/
└── anomalib/
    ├── .env/
    ├── scripts/
    └── <用户名>/<项目>/
```

与 `.env`、`weights`、`statuses`、`scripts` 等共享目录同名的用户会使用安全映射目录，避免覆盖共享文件。`train/cuda-runtime/` 仍是验证模块共用的 CUDA 运行库目录，不属于某个训练算法。

## 🖥️ 界面展示

<p align="center">
  <img src="images/1.png" width="900"/>
  <img src="images/1.1.png" width="900"/>
  <img src="images/1.2.png" width="900"/>
  <img src="images/2.png" width="900"/>
  <img src="images/3.png" width="900"/>
  <img src="images/4.png" width="900"/>
  <img src="images/5.png" width="900"/>
</p>

## 📦 NuGet 安装

💡 在您自己的 .NET 项目中使用 VisualIdentity 核心库：

```bash
# 核心推理库（必装）
dotnet add package Snet.Yolo.Server

# 按部署硬件二选一，不要同时安装两套原生执行提供程序
dotnet add package YoloDotNet.ExecutionProvider.Cpu      # 🖥️ 通用 CPU
dotnet add package YoloDotNet.ExecutionProvider.Cuda     # 🎮 NVIDIA GPU + TensorRT
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

// 创建实例，作用域结束后异步释放；连续处理时复用此实例
await using var identity = new IdentityOperate(new IdentityData
{
    Hardware = new CpuExecutionProvider("/path/to/model.onnx"),
    IdentifyType = OnnxType.ObjectDetection,
    SN = "my-detector"
});

byte[] imageBytes = await File.ReadAllBytesAsync("/path/to/image.jpg");
using SKImage image = SKImage.FromEncodedData(imageBytes)
    ?? throw new InvalidDataException("Invalid image");

// 执行推理
OperateResult result = await identity.RunAsync(new ObjectDetectionData
{
    Confidence = 0.23,  // 置信度阈值
    Iou = 0.7,          // 交并比阈值
    File = imageBytes
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

```

## 🧩 联合验证控制台 Demo

📦 [Snet.VisualIdentity.JointVerificationDemo](Snet.VisualIdentity.JointVerificationDemo/README.md) 是独立 .NET 10 控制台解决方案，通过 `Snet.Yolo.Tasks.Core → Snet.Yolo.Server` 在进程内推理，不启动 Tasks 网站，也不调用 HTTP API。依赖按项目引用自动还原。

1. 🧠 修改 [demo.json](Snet.VisualIdentity.JointVerificationDemo/demo.json)：填写 Anomalib 的 ONNX/清单、YOLO ONNX、图片路径及各模型参数。相对路径以配置文件目录为基准；不自动下载业务模型。
2. 🔀 选择 `Joint`、`AnomalibOnly` 或 `YoloOnly`。联合模式整图正常时跳过 YOLO；异常时识别原图，再关联异常区域与 YOLO 检测/分割外接框。
3. 🖼️ 输出到程序集目录 `result/<时间戳与唯一标识>/`：`annotated.png`、可选 `heatmap.png` 和 `result.json`，不修改原图与模型。

在仓库根目录执行：

```powershell
# CPU
dotnet run --project Snet.VisualIdentity.JointVerificationDemo -- "D:\models\demo.json"
# CUDA；配置中的 GpuId 指定单张显卡，需匹配的驱动和原生库
dotnet run --project Snet.VisualIdentity.JointVerificationDemo -p:UseCuda=true -- "D:\models\demo.json"
```

🎬 视频平台可复用 `JointVerificationEngine` 会话，在退出时异步释放，用有界队列控制积压。当前示例输入是图片文件路径，并非零拷贝视频流或实时帧率保证；热图编码和结果写盘应按需关闭或降频。单个引擎不会自动将一帧分配到多 GPU。

## 🔌 API 接口文档

> ⚠️ API 按当前产品要求保持匿名访问，不启用登录授权。请仅部署在可信网络，或在反向代理/API 网关上增加访问控制；限流与 CORS 不能替代身份认证。

### 📋 模型管理

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| `POST` | `/Operate/AddAsync` | 上传 ONNX 模型文件 | 无（建议置于受信网络或认证网关后） |
| `POST` | `/Operate/UpdateAsync` | 修改模型描述或类型 | 无（建议置于受信网络或认证网关后） |
| `POST` | `/Operate/DeleteAsync` | 删除模型（可选删除文件） | 无（建议置于受信网络或认证网关后） |
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

### 🧩 Anomalib 模型与识别

Anomalib 与 YOLO 模型分别管理。模型包必须是包含 `model.onnx` 和 `model.manifest.json` 的 ZIP；导入时会验证清单、SHA-256 及 ONNX 输入输出契约。API 模型归属固定服务账户 `snet`，存放于 API 自己的 `anomalib-api/<username>/<project>/` 目录，不会读取 TASKS 已登录用户的模型。TASKS 的 Anomalib 验证页仍直接调用 `Snet.Yolo.Server`，不绕行 HTTP API。

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/anomalib/models` | 列出 API 账户的模型 |
| `GET` | `/api/anomalib/models/{projectId}/{runId}` | 查看模型名称、描述、类型和注册时间 |
| `POST` | `/api/anomalib/models` | `multipart/form-data`：`file`（ZIP）、`name`、`description`、`modelKind`（`Padim`、`EfficientAdSmall` 或 `PatchcoreExperimental`） |
| `PUT` | `/api/anomalib/models/{projectId}/{runId}` | JSON：`name`、`description`；算法类型与模型清单绑定，不允许修改 |
| `GET` | `/api/anomalib/models/{projectId}/{runId}/download` | 下载 ONNX 与清单 ZIP |
| `DELETE` | `/api/anomalib/models/{projectId}/{runId}` | 删除已注册模型 |
| `POST` | `/api/anomalib/models/{projectId}/{runId}/identify` | `multipart/form-data`：`file`（图片）、可选 `includeHeatmap`；返回整图分数、异常判定、原图坐标区域、单张推理耗时和可选热图 |

📦 ZIP 必须只包含根目录的这两个文件；解压后 ONNX 上限 500 MiB、清单上限 1 MiB。导入不等于重新训练或重新验证原模型的准确率。

导入包上限为 512 MiB；识别图片受 `ConfigModel:MaxImageBytes`（默认 100 MiB）限制。Anomalib 在 CPU 版 API 使用 CPU ONNX Runtime，在 CUDA 版 API 使用 CUDA；它不使用 YOLO 的 TensorRT 参数。

🎛️ 当前 Anomalib HTTP 识别接口只暴露 `includeHeatmap`（默认 `false`），不接收验证页的区域阈值和最小面积字段；Server 默认使用模型阈值及最小面积 4。YOLO 更新/删除接口的 `index` 等参数通过 query 提交；上传与识别使用表单。API 图片支持 JPG/JPEG/PNG/BMP。SAM 辅助标注与联合验证目前通过 Tasks/Core/Server 使用，没有对应的独立 HTTP 路由。

### 🖼️ 历史图片

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/Operate/GetOriginalImage?name=xxx&type=ObjectDetection&date=yyyy-MM-dd` | 获取原始图片（日期可选，省略时查找最近记录） |
| `GET` | `/Operate/GetMarkImage?name=xxx&type=ObjectDetection&date=yyyy-MM-dd` | 获取标注图片（日期可选） |
| `GET` | `/Operate/GetImageDetails?name=xxx&type=ObjectDetection&date=yyyy-MM-dd` | 完整详情（原图 + 标注 + 坐标 JSON；日期可选） |

### 🏥 健康检查

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/health` | 健康检查（返回 `{"Status":"Healthy","Timestamp":"..."}`） |

### 🧾 `paramJson` 参数格式

| 识别类型 | JSON 格式 |
|---------|----------|
| 对象检测 | `{"Confidence":0.2,"Iou":0.7}` |
| 定向检测 | `{"Confidence":0.2,"Iou":0.7}` |
| 图像分类 | `{"Classes":1}` |
| 姿态估计 | `{"Confidence":0.2,"Iou":0.7}` |
| 实例分割 | `{"Confidence":0.2,"Iou":0.7,"PixelConfidence":0.65}` |

## ⚙️ 配置文件

### ⚙️ API 根目录 `appsettings.json`

```jsonc
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
    "RetentionDays": 30,                              // 🗑️ 历史数据保留天数
    "MaxImageBytes": 104857600,                       // 🖼️ 单张推理图片上限（默认 100 MiB）
    "MaxModelBytes": 1073741824                       // 🧠 单个 ONNX 模型上限（默认 1 GiB）
  }
}
```

Tasks 使用 `Snet.Yolo.Tasks.Shared/appsettings.json`。训练代理位于 `Training:Proxy` 与 `Training:CaBundle`；媒体工具可通过 `MediaTools:FFmpegPath`、`MediaTools:FFprobePath`、`MediaTools:InstallDirectory` 和 `MediaTools:DiscoverInstalledTools` 配置，也可使用 `SNET_FFMPEG_PATH` / `SNET_FFPROBE_PATH` 环境变量。

> 💡 企业网络里配好训练代理与 CA 后重启，训练日志给出的权重下载命令会自动带上 `--cacert` / `-x`。

### 🌱 环境变量支持

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境（`Development` / `Production`） | `Production` |
| `ASPNETCORE_URLS` | 服务监听地址 | 取决于启动参数/配置；容器示例为 `http://+:8080` |
| `SNET_BOOTSTRAP_ADMIN_PASSWORD` | Tasks 的 `snet` 管理员口令；设置时会在启动阶段覆盖默认密码并同步现有管理员 | `123456` |

> ⚠️ Swagger UI 仅在 `Development` 环境下启用，生产环境自动关闭。

## 🧠 支持的任务

| 分类 (Classification) | 检测 (Detection) | OBB 定向检测 | 分割 (Segmentation) | 姿态估计 (Pose) |
|:---:|:---:|:---:|:---:|:---:|
| 🔖 整图分类 | 📦 边界框定位 | 🔄 旋转框定位 | 🎭 像素级分割 | 🦴 关键点检测 |
| 输出标签+置信度 | 输出框+标签+置信度 | 输出旋转框+角度 | 输出遮罩+框+标签 | 输出骨骼点+框 |
| <img src="https://user-images.githubusercontent.com/35733515/297393507-c8539bff-0a71-48be-b316-f2611c3836a3.jpg" width=260> | <img src="https://user-images.githubusercontent.com/35733515/273405301-626b3c97-fdc6-47b8-bfaf-c3a7701721da.jpg" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/d15c5b3e-18c7-4c2c-9a8d-1d03fb98dd3c" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/3ae97613-46f7-46de-8c5d-e9240f1078e6" width=260> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/b7abeaed-5c00-4462-bd19-c2b77fe86260" width=260> |

> 📌 Tasks、HTTP API 与 WPF 当前公开以上五类任务。底层 `YoloDotNet` 还包含 YOLO26 语义分割和单目深度估计模块，但尚未通过 `Snet.Yolo.Server.OnnxType` 暴露为产品入口。

### 🦴 姿态估计 — 内置跌倒检测

🚨 WPF 的 `YoloPoseViewModel` 集成基于 17 个人体关键点的跌倒规则判断（`FallDetector`）。这是几何启发式示例，不是独立训练的跌倒模型，也不构成实际场景准确率或实时性保证：

| 检测维度 | 判定标准 | 可配置 |
|---------|---------|--------|
| 📏 身体高度 | 鼻-踝距离 < 50% 图像高度 | `FlatHeightRatio` |
| 📐 身体倾角 | 肩-髋连线 < 70° | `AngleThreshold` |
| ↔️ 躯干水平度 | 肩髋 Y 差值 < 10% 图像高度 | `TorsoHorizontalThresholdRatio` |
| 📍 近地距离 | 平均关键点 Y > 60% 图像高度 | `GroundProximityRatio` |
| ✅ 综合判定 | 满足 ≥ 2 项即判定跌倒 | `FallScoreThreshold` |

## ✅ 支持的 YOLO 模型系列

✅ 当前仓库的 **YoloDotNet** 解析器包含以下模型系列和任务模块。具体 ONNX 文件仍须使用匹配的 Ultralytics 导出方式与输出布局；“支持”不代表任意第三方修改图都无需验证：

| 分类 (Classification) | 检测 (Detection) | 分割 (Segmentation) | 姿态估计 (Pose) | OBB 定向检测 |
|:---:|:---:|:---:|:---:|:---:|
| YOLOv8-cls | YOLOv5u | YOLOv8-seg | YOLOv8-pose | YOLOv8-obb |
| YOLOv11-cls | YOLOv8 | YOLOv11-seg | YOLOv11-pose | YOLOv11-obb |
| YOLOv12-cls | YOLOv9 | YOLOv12-seg | YOLOv12-pose | YOLOv12-obb |
| YOLOv26-cls | YOLOv10 | YOLOv26-seg | YOLOv26-pose | YOLOv26-obb |
| | YOLOv11 | YOLOv9-seg | | |
| | YOLOv12 | YOLO-E (v8/v11/v26) | | |
| | YOLOv26 | | | |
| | YOLO-World (v2) | | | |
| | YOLO-E | | | |
| | RT-DETR | | | |

## 🖥️ 执行提供者

| Provider | Windows | Linux | Docker | 适用场景 |
|----------|:---:|:---:|:---:|----------|
| 🖥️ **CPU** | ✅ | ✅ | ✅ | 通用推理、x64/ARM64 环境 |
| 🎮 **CUDA / TensorRT** | ✅ | ✅ | ✅ | NVIDIA GPU 加速 |

> 📌 当前产品项目只提供 CPU 与 CUDA/TensorRT 两种执行路径；CUDA Tasks 仅发布 GPU 版 ONNX Runtime，并在 CUDA 不可用时复用其中内置的 CPU 执行路径，避免两套原生运行库互相覆盖。

当前 CUDA 执行提供程序固定使用 `Microsoft.ML.OnnxRuntime.Gpu` **1.23.2**，作为现有 CUDA 12.8 / cuDNN 9 部署及较早 NVIDIA 显卡环境的兼容基线；这不表示该版本保证支持所有旧显卡。若较新的显卡无法使用 CUDA 推理，请将 `YoloDotNet.ExecutionProvider.Cuda` 中的 `Microsoft.ML.OnnxRuntime.Gpu` 升级到适配该显卡的最新稳定版本，并同步检查 `Snet.Yolo.Server` 的 ONNX Runtime Managed 依赖、驱动、CUDA/cuDNN 版本及项目的 CUDA 运行库准备逻辑，重新构建和发布。只升级 NuGet 包而保留不匹配的 CUDA 运行库，仍可能初始化失败；以 [ONNX Runtime CUDA 兼容表](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html) 为准。

🎮 CUDA Tasks 在每次开始识别前确认当前构建与 GPU 环境。Windows / Linux x64 缺少 CUDA 12 与 cuDNN 9 时，程序通过 NVIDIA 官方 pip wheel 安装到应用私有目录 `train/cuda-runtime/`，不会修改系统驱动、`PATH` 或 `LD_LIBRARY_PATH`；按钮在准备期间显示进度并禁止重复点击。系统驱动仍由管理员维护：Windows 使用 NVIDIA 官方驱动，Ubuntu/Debian、Fedora/RHEL、SUSE、Arch 使用各发行版对应的 NVIDIA 驱动仓库；WSL 只更新 Windows 宿主驱动与 `wsl --update`，不要在 WSL 内安装 Linux 显卡驱动；容器还需要 NVIDIA Container Toolkit。macOS 不支持 CUDA；本仓库没有提供 MPS/CoreML 产品构建，当前发布矩阵也不包含 macOS。详见 [ONNX Runtime CUDA 要求](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html)、[NVIDIA CUDA Windows 安装](https://docs.nvidia.com/cuda/cuda-installation-guide-microsoft-windows/) 与 [CUDA on WSL](https://docs.nvidia.com/cuda/wsl-user-guide/)。

### 📌 当前依赖与硬件边界

| 入口 | 运算路径 |
|------|----------|
| 🖥️ CPU 原生执行提供程序 | `Microsoft.ML.OnnxRuntime 1.30.0` |
| 🎮 CUDA 原生执行提供程序 | `Microsoft.ML.OnnxRuntime.Gpu 1.23.2`；Server 的 Managed 引用为 1.23.2，最终依赖解析以宿主构建为准 |
| 🏋️ YOLO / Anomalib 训练 | Python / PyTorch；Anomalib 固定 2.6.2，训练 ONNX Runtime 1.23.2；Ultralytics/PyTorch 安装并非固定同一版本 |
| 🪄 SAM 标注 | ONNX Runtime，CPU 或手动指定单 GPU；不依赖训练 Python 环境 |
| 🔀 Tasks 验证 | 随 CPU/CUDA 产品选择执行路径；CUDA 不可用时验证可回退 CPU，SAM 的 GPU 初始化失败则明确报错 |
| 🧩 Demo | 构建时选择 CPU/CUDA，配置 `GpuId`；单实例不做多卡联合推理 |

训练设备由 Python 检测和配置决定，与宿主安装 CPU/GPU ONNX Runtime 包不是同一件事。CPU Tasks 也可在 Python 环境支持时进行 GPU 训练。Tasks 当前验证 GPU 路径使用 GPU 0，YOLO CUDA API 可以通过 `gpuid` 指定设备；这些不是多 GPU 训练配置。新配置下 PaDiM 输入过大可能在特征统计阶段耗尽显存，优先降低输入尺寸或改用 CPU，内存分配器选项不能增加物理显存。

## 💡 ONNX 模型导出

### 🐍 使用 Python (Ultralytics)

```bash
pip install ultralytics
python Snet.Py/Snet.Py.py /path/to/best.pt --opset 18
```

### ⌨️ 手动导出

```bash
# YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

> 📌 脚本默认读取 `best.pt`、使用 opset 17；YOLO26 请显式指定 18。还需校验模型的输入输出布局、预处理及执行提供程序兼容性，不能仅凭 opset 保证正确识别。

## 🐳 Docker 部署

🎯 发布工作流只打包仓库中真实存在的产品：WPF 发布 `win-x64`、`win-x86`；Tasks/API 的 CPU 版本发布 `linux-x64`、`linux-arm64`、`win-x64`，CUDA 版本发布 `linux-x64`、`win-x64`。GHCR 只构建 Tasks/API 的 Linux CPU 与 CUDA 镜像；仓库中的 Windows Dockerfile 用于手动构建，不在 GitHub Actions 镜像矩阵中。

### 🏗️ 构建镜像

```bash
# Linux CPU（Tasks 镜像包含 ffmpeg、ffprobe 与 Python）
docker build -t snet-yolo-tasks-cpu -f docker/Tasks.Cpu.Dockerfile .
docker build -t snet-yolo-api-cpu -f docker/Api.Cpu.Dockerfile .

# Linux CUDA（运行时需要 NVIDIA Container Toolkit）
docker build -t snet-yolo-tasks-cuda -f docker/Tasks.Cuda.Dockerfile .
docker build -t snet-yolo-api-cuda -f docker/Api.Cuda.Dockerfile .

```

### 🚀 运行容器

```bash
# 首次准备 YOLO 模型卷的目录权限（新卷）；服务仍以非 root 用户运行
tasks_uid=$(docker run --rm --entrypoint id snet-yolo-tasks-cpu -u)
docker run --rm --user 0 --entrypoint sh \
  -v snet-tasks-models:/models snet-yolo-tasks-cpu \
  -c "chown $tasks_uid:$tasks_uid /models"

# CPU Tasks Web 工作台
docker run -d --name snet-yolo-tasks-cpu -p 8080:8080 \
  -e SNET_BOOTSTRAP_ADMIN_PASSWORD='<strong-password>' \
  -v snet-tasks-data:/app/wwwroot/data \
  -v snet-tasks-db:/app/wwwroot/db \
  -v snet-tasks-models:/app/wwwroot/onnxs \
  -v snet-tasks-train:/app/train \
  -v snet-tasks-sam:/app/sam \
  snet-yolo-tasks-cpu

# 确认镜像内 ffmpeg 和 ffprobe 都可用
docker exec snet-yolo-tasks-cpu ffmpeg -version
docker exec snet-yolo-tasks-cpu ffprobe -version

# CPU API
docker run -d -p 8080:8080 \
  -v snet-api-data:/app/wwwroot \
  -v /path/to/writable-anomalib-api:/app/anomalib-api \
  snet-yolo-api-cpu

curl http://localhost:8080/health   # 健康检查
curl http://localhost:8080/Operate/QueryAllAsync
```

> 📝 Linux Tasks 镜像中的 Debian `ffmpeg` 包同时提供 `ffmpeg` 和 `ffprobe`。CUDA 容器运行时需要 NVIDIA Container Toolkit 与可用 GPU。

Tasks 的 CPU/CUDA 镜像预先创建 `/app/sam` 并授权给非 root 运行用户；Windows Tasks 镜像对应 `C:/app/sam`，授予 `ContainerUser` 可继承的修改权限。SAM 卷保存下载的权重、`active-model.json` 与 `.versions` 中的更新/回退版本。重建或升级容器时，必须继续挂载同一个命名卷（例如 `snet-tasks-sam`）；单独的 `VOLUME` 声明产生匿名卷，后续新容器不会自动复用它。不要删除 SAM 卷或以只读方式挂载；备份需包含整个 `sam` 目录。

CUDA Tasks 同样添加 `-v snet-tasks-sam:/app/sam`；Windows Tasks 使用 `--mount type=volume,source=snet-tasks-sam,target=C:/app/sam`。使用宿主机目录绑定挂载或已有卷时，挂载后的权限以宿主机/卷实际权限为准，镜像内授权不会自动修复：Linux 目录需允许镜像运行 UID 写入（可用 `docker run --rm --entrypoint id <Tasks镜像>` 查看，当前 CUDA 为 `1654`）；Windows 目录需允许容器用户修改。不要让多个运行中的 Tasks 实例同时写入同一个 SAM 卷，版本切换只在单个服务进程内协调。

⚠️ 当前 Tasks 镜像没有预建 `wwwroot/onnxs`；因此新模型卷需按示例准备目录权限，已有卷也需核对实际 UID。CUDA 镜像使用相同权限原则。

🗄️ Tasks 需要持续保存 `wwwroot/data`、`wwwroot/db`、`wwwroot/onnxs`、`train` 和 `sam`；只保存图片和数据库会丢失上传的 YOLO 模型。需保留媒体工具配置时另备份 `tools/media-tools.json`；若希望升级后 Cookie 仍有效，还需持续保存 ASP.NET Core Data Protection 密钥。

⚠️ 当前 API 镜像仅预先授权 `/app/wwwroot`，没有预先创建并授权 `/app/anomalib-api`。上面的 API 绑定挂载目录必须先创建，并允许镜像非 root 用户写入（用 `docker run --rm --entrypoint id <API镜像>` 查询 UID）；不能直接假设空命名卷具备权限。不要用重叠的 `wwwroot` 与 `wwwroot/onnxs` 绑定挂载覆盖彼此。API 的模型与历史图片卷独立于 Tasks。

📦 发布包使用 `--self-contained false`：Web 产品需要 .NET 10 ASP.NET Core Runtime，WPF 需要 Windows Desktop Runtime。WPF 发布工作流默认使用 CPU；手动 CUDA 构建使用 `-p:UseCuda=true`，CPU/GPU 发布到不同目录。控制台 Demo 不在当前发布矩阵中。CUDA API 镜像基于 CUDA 12.6.3/cuDNN 运行镜像，Tasks 的私有运行库准备逻辑不是所有产品的自动安装保证。

## 🧪 测试

```bash
# 🧪 单元测试（xUnit）：上传中心、训练编排、数据集导出/体检、验证结果、媒体工具与 FFmpeg 安装等
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj

# 浏览器标注交互回归（首次运行需下载 Chromium）
npm ci
npx playwright install chromium
node --test Snet.Yolo.Test/Browser/polygon-editor.test.mjs Snet.Yolo.Test/Browser/sam-editor.test.mjs

# Release 编译
dotnet build VisualIdentity.sln -c Release
```

## 🔒 安全特性

| 特性 | 实现方式 | 配置 |
|------|---------|------|
| 🌐 **CORS 控制** | `RestrictedOrigins` 策略 | `appsettings.json` → `AllowedOrigins` |
| 🛡️ **CSRF 防护** | Tasks 的 Cookie 会话表单使用 Antiforgery Token；独立 API 保持无状态客户端兼容 | 登录、退出等浏览器表单 |
| ⏱️ **速率限制** | 固定窗口算法 | `RateLimit` 配置节 |
| 🔐 **安全响应头** | 中间件自动注入 | X-Content-Type-Options / X-Frame-Options / Referrer-Policy / Permissions-Policy（未配置 CSP） |
| 📁 **文件名净化** | 过滤路径遍历字符 + GUID 唯一化 | 上传处理逻辑 |
| 📏 **文件大小限制** | Kestrel + FormOptions 双重限制 | API 默认图片 100 MiB、模型 1 GiB；Tasks 请求体与数据集 ZIP 同为 `UploadCenter.MaxArchiveBytes`（16 GiB） |
| 🧹 **数据自动清理** | `HistoryFileHandler` 定时任务 | `RetentionDays`（默认 30 天） |

## 📈 性能优化

| 优化项 | 说明 |
|--------|------|
| 🔄 **模型会话复用** | API YOLO 使用会话缓存；Tasks YOLO 图片验证每次请求创建并释放实例；Anomalib、SAM 与 Demo 按各自作用域复用 |
| 🧵 **异步编排** | 请求、并发控制与文件 I/O 使用异步接口；ONNX 原生推理并非可随时取消的异步内核 |
| 🖼️ **并行写盘** | 原图 / 标注图 / JSON 详情 `Task.WhenAll` 并行写入 |
| 💾 **资源生命周期** | WPF `BitmapSource.Freeze()` 支持跨线程显示，Skia/ONNX 对象按所有权及时释放 |

> 📌 实际延迟取决于模型、输入尺寸、执行提供程序、GPU、TensorRT 配置与存储性能；仓库不声明脱离具体硬件和模型的固定毫秒数。

## 📚 依赖组件

| 组件 | 说明 |
|------|------|
| 🔗 **Snet.DB** | Dapper & SqlSugarCore 双 ORM，自动建表，Code-First 体验 |
| ⚡ **YoloDotNet** | 仓库内的 .NET YOLO 推理实现，支持上表列出的模型/任务组合 |
| 🎨 **SkiaSharp** | 跨平台 2D 渲染：图片解码、标注绘制、关键点渲染 |
| 🗄️ **SQLite** | 工程、用户、标注与 YOLO 模型元数据；Anomalib 清单/产物另由文件注册目录管理 |
| 🌐 **ASP.NET Core / Blazor** | Tasks 工作台、SignalR 训练日志与独立 Web API |
| 🧠 **ONNX Runtime** | YOLO、Anomalib、SAM 原生推理与 CPU/CUDA 执行提供程序 |
| 🏋️ **PyTorch / Ultralytics / Anomalib** | Python 训练与模型导出，和 .NET 推理环境分开 |
| 🪄 **SAM / MobileSAM / SAM 2** | 交互式物体掩码辅助标注，使用经校验的 ONNX 模型目录 |
| 🎬 **FFmpeg / FFprobe** | 视频解码、探测与结果编码；中文标注另需可用 CJK 字体 |

## 🙏 致谢

| 项目 | 说明 |
|------|------|
| 🌐 [Snet.cn](https://snet.cn) | 项目官方网站 |
| 🔥 [Ultralytics](https://github.com/ultralytics/ultralytics) | YOLO 模型训练与导出 |
| 🔍 [Anomalib](https://github.com/open-edge-platform/anomalib) | 工业异常检测模型训练、异常定位与 ONNX 导出 |
| ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) | .NET YOLO 推理引擎 |
| 🪄 [Segment Anything](https://github.com/facebookresearch/segment-anything) / [SAM 2](https://github.com/facebookresearch/sam2) | SAM 模型与交互式分割 |
| 📱 [MobileSAM](https://github.com/ChaoningZhang/MobileSAM) | 轻量 SAM 辅助标注模型 |
| 🧠 [ONNX Runtime](https://github.com/microsoft/onnxruntime) | 跨平台模型推理与硬件执行提供程序 |
| 🖥️ [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) | WPF 现代化 UI 框架 |
| 🗄️ [SqlSugarCore](https://github.com/DotNetNext/SqlSugar) | ORM 框架 |
| 🎨 [SkiaSharp](https://github.com/mono/SkiaSharp) | 跨平台图形渲染 |

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

⚖️ 本项目基于 **MIT** 开源协议 —— 自由使用、修改、分发。

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
