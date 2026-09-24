<h1 align="center">🔍 Snet.VisualIdentity</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/><br/>
</p>

<p align="center">
  <b>基于 .NET 10 的 YOLO 多模型智能视觉识别平台</b>
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

## 🌟 项目简介

**VisualIdentity** 是一个开箱即用的智能识别平台：结合 **.NET** 的现代化能力、[YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 高性能推理引擎与 **SQLite** 轻量数据管理，解决「多模型部署 + 多任务识别」的落地痛点——**检测、分类、分割、姿态估计、定向检测** 五种任务统一管理、按需切换。

> 💡 当前解决方案统一使用 **.NET 10**；WPF 工具目标框架为 `net10.0-windows`。

### ✨ 核心特性（功能总览）

#### 🧠 识别与模型

| 特性 | 说明 |
|------|------|
| 🎯 **五合一识别** | 对象检测 · 定向检测 (OBB) · 图像分类 · 实例分割 · 姿态估计，统一管理、按需切换 |
| 🧠 **多模型管理** | 基于 SQLite 的模型增删改查与快速切换 |
| 🖱️ **点图即识别** | 验证页点击图片自动识别；视频因为耗时长，仍由「识别」按钮触发，并可随时取消 |
| 🔍 **大图查看器** | 双击图片打开：滚轮缩放（以光标为锚点）· 按住拖动 · 双击/按钮还原 · 顶部「原图」切换 |
| 🎬 **视频结果聚合** | 识别结果按标签汇总为「平均置信度 + 全片识别次数」，不再罗列无意义的坐标 |
| ⚡ **多硬件加速** | CPU · NVIDIA CUDA / TensorRT；Tasks 与 API 复用同一套业务实现 |
| 📊 **实时性能** | 毫秒级耗时统计，批量验证与置信度分析 |

#### 🏷️ Tasks Web 工作台

| 特性 | 说明 |
|------|------|
| 🏷️ **全流程工作台** | 浏览器内完成工程管理、数据导入、五类任务标注、YOLO 导出、训练与模型验证 |
| 📤 **上传不中断** | 上传任务由服务持有：切换页面、切回、甚至重新渲染都不会丢失进度，横幅可随时取消 |
| 🗂️ **每模型独立队列** | 每个模型各自保存验证文件队列、当前选中项与识别结果，刷新浏览器后仍可恢复 |
| 🎞️ **图片与视频验证** | 一次最多 100 个文件；视频后台逐帧识别、实时显示进度与预计剩余时间 |
| 📥 **增量导入** | YOLO ZIP 可一批批上传：同名类别复用、新类名追加、标注下标自动重映射到工程标签 |
| 🐍 **Python 辅助** | 内置模型导出脚本，一键转换 PyTorch → ONNX |
| 🖥️ **WPF 调试工具** | 5 种识别模式可视化验证 + 数据统一标注工具 |

#### 🏋️ 训练

| 特性 | 说明 |
|------|------|
| 🔢 **默认 300 轮** | 小数据集在 50 轮时只有几十次参数更新，模型学不到东西；Ultralytics 会按 `patience` 自动早停 |
| 🎯 **默认不切验证集** | 小数据集再切掉 10% 会明显影响训练；需要客观指标时在训练配置里勾选「使用验证集（自动划分 10%）」 |
| 🩺 **训练前体检** | 日志给出每类实例数、图片数、目标像素尺寸与验证集大小，并对"注定识别不到"的数据逐条告警 |
| 🔬 **训练后自检** | 读取 `results.csv` 的 mAP；验证集过小时自动在训练集上复验，明确告知模型是否真的学到了东西 |
| 📥 **权重下载命令** | 企业代理拦截 GitHub 时（curl 60），日志直接给出按系统生成的 `curl` 命令（自动带上代理与 CA 参数），下完即被复用 |

#### 🚀 部署与运维

| 特性 | 说明 |
|------|------|
| 🌍 **多平台发布** | WPF 支持 Windows；Tasks/API 发布 Windows 与 Linux 包，并提供 Linux Docker 镜像 |
| 🛠️ **FFmpeg 自检** | 上传视频即自检：Windows 弹窗选择（手填路径 / 静默下载安装），Linux 直接用 apt 全局安装，失败不影响图片流程 |
| 🔤 **中文不再变方框** | 视频标注文字改用系统中文字体绘制；Linux 缺少中文字体时随 FFmpeg 一起自动安装 |
| 📦 **开箱即用** | CPU 与 CUDA/TensorRT 产品独立运行；核心库与执行提供程序也可作为 NuGet 依赖使用 |

#### 🔒 安全与性能

| 特性 | 说明 |
|------|------|
| 🔒 **明确安全边界** | Tasks 使用 Cookie 登录与 CSRF 防护；API 按产品要求匿名，并提供限流、CORS 与安全响应头 |
| 🔐 **按用户隔离** | 工程、标注、模型、验证数据与文件按登录用户隔离，训练环境共享 |
| 🔄 **模型实例缓存** | 配置不变时复用模型实例，避免重复加载 |
| 🧵 **异步全链路** | HTTP → GPU 推理 → 磁盘写入全链路 `async/await` |

> 📖 各项细节见下方对应章节：[Tasks 工作台](#-tasks-web-标注与训练工作台) · [视频与 FFmpeg](#-视频验证的-ffmpeg-部署) · [配置文件](#️-配置文件) · [安全特性](#-安全特性) · [性能优化](#-性能优化)

## 🎯 应用场景

| 场景 | 用途 | 推荐模型类型 |
|------|------|------------|
| 🏭 **工业质检** | 瑕疵检测、异物识别、零件计数 | 检测、分割 |
| 🛒 **零售分析** | 顾客行为追踪、货架商品检测 | 检测、分类 |
| 🛡️ **智能安防** | 异常行为监测、跌倒检测、区域入侵 | 姿态估计、检测 |
| 🚗 **自动驾驶** | 道路目标检测、交通标志识别 | 定向检测、检测 |
| 🏥 **医疗影像** | 病灶分割、细胞分类 | 分割、分类 |
| 📄 **文档分析** | 旋转文本检测、表格识别 | 定向检测 |
| 🌐 **边缘计算** | x64/ARM64 CPU 或 NVIDIA Jetson 部署 | CPU、CUDA |

## 🏗️ 项目架构

```
VisualIdentity/
├── YoloDotNet/                    # 🧠 ONNX 模型解析、预处理与后处理
├── YoloDotNet.ExecutionProvider.Cpu/  # 🖥️ CPU 执行提供程序
├── YoloDotNet.ExecutionProvider.Cuda/ # 🎮 CUDA / TensorRT 执行提供程序
├── Snet.Yolo.Server/              # 🗄️ SQLite 数据访问、模型管理与推理服务
├── Snet.Yolo.Api.Shared/          # 🔗 共享 API 层（Shared Project：控制器 / 安全 / 图片处理）
├── Snet.Yolo.Api.Cpu/             # 🖥️ CPU API（HTTP 5157 · HTTPS 7257）
├── Snet.Yolo.Api.Cuda/            # 🎮 CUDA / TensorRT API（HTTP 5158 · HTTPS 7258）
├── Snet.Yolo.Tasks.Core/          # 🏷️ 标注配置、编辑、导出与训练领域逻辑
├── Snet.Yolo.Tasks.Shared/        # 🔗 Tasks 共享项目（Blazor 组件、服务与静态资源）
├── Snet.Yolo.Tasks.Cpu/           # 🖥️ CPU Tasks（HTTP 5151 · HTTPS 7351）
├── Snet.Yolo.Tasks.Cuda/          # 🎮 CUDA / TensorRT Tasks（HTTP 5152 · HTTPS 7352）
├── Snet.Yolo.Tool/                # 🛠️ WPF 桌面调试工具
├── Snet.Yolo.Test/                # 🧪 xUnit 回归与集成测试
├── Snet.Py/                       # 🐍 Python 模型导出脚本
├── docker/                        # 🐳 Tasks / API 的 CPU 与 CUDA 镜像定义
├── VisualIdentity.sln             # 🧩 解决方案入口
└── appsettings.json               # ⚙️ API 共用配置
```

### 🔄 数据流

```
客户端上传图片 → API 控制器（参数验证）→ 速率限制中间件
→ ManageOperate（数据库查询模型路径）→ IdentityOperate（加载模型 + 硬件加速）
→ YoloDotNet 推理（GPU / CPU）→ ResultHandler（结果转换）
→ ImageHandler（标注绘制 + 磁盘存储）→ 返回 JSON + 图片 URL
```

## ⚡ 快速开始

### 🧰 前置要求

- 📦 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 🧠 至少一个 ONNX 格式的 YOLO 模型（[导出方法](#-onnx-模型导出)）

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

> 🐍 训练功能还需要本机可用的 Python。Tasks 会检测并创建共享虚拟环境，再按所选任务启动 Ultralytics 训练。

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

### 🧩 Anomalib 异常区域（第一阶段）

在侧栏进入「Anomalib 项目」，新建独立工程并上传至少 10 张**正常图片**，无需画框或提供缺陷标签。工程详情页支持点击缩略图查看原图、翻页浏览（每页 150 张），并显示训练阶段与状态。训练页沿用 YOLO 的进度、日志和右侧硬件资源布局；点击「开始训练」后，在弹窗中选择 PaDiM（默认）、EfficientAD Small 或实验性 PatchCore，并配置输入尺寸和设备。选择模型时下方会显示该算法的特点。YOLO 工程详情页也提供「模型训练」入口。Tasks 首次运行会建立独立的 `train/anomalib/.env`，不改动 YOLO 训练环境；训练完成后导出 ONNX，并用本次训练的内存模型与 ONNX 比较校准图片结果（不反序列化 `.pt`）。只有一致性与正常图误报门禁都通过的模型才会注册并出现在「Anomalib 验证」。

🧠 **模型与输入尺寸**：PaDiM 按画面位置建立正常特征分布，适合相对固定的机位；EfficientAD Small 使用教师—学生网络，侧重快速推理；PatchCore 用正常图像的局部特征库进行近邻比较，其 ONNX 导出仍为实验性。输入尺寸是训练和推理时缩放后的正方形尺寸，默认 `256 × 256`，可设为 128–2048 之间的 32 的倍数；修改后需重新训练。增大尺寸能保留更多小目标细节，但会增加资源开销，也不保证减少误检。

🖼️ **验证与模型文件**：验证页采用与 YOLO 相同的模型、结果、文件和预览分区，支持批量图片与视频。图片显示异常热图和原图坐标区域；视频需 FFmpeg/FFprobe，逐帧识别并生成可播放的区域标注视频，可查看进度并取消（单文件不超过 100 MiB、视频不超过 10000 帧）。模型可从列表下载或删除；下载得到包含 `model.onnx` 和 `model.manifest.json` 的 ZIP，添加模型时可在弹窗中填写名称、描述、类型并上传这种 ZIP。Anomalib 四个页面支持中英文切换，第三方训练原始日志保持原文。

> ⚠️ 注册前会检查训练模型与 ONNX 的一致性，并要求留出的正常图片校准集误报率不超过 5%；超过阈值则不注册。通过门禁仍**不代表缺陷检出率合格**，应使用独立的正常图和已知异常图检查误报、漏检。尤其使用 PaDiM 时，应尽量保持机位和检测区域稳定。PatchCore 的 ONNX 导出仍属实验路径；不通过门禁时不会提供推理。此阶段只定位异常区域，**尚未接入 YOLO 缺陷分类/分割级联**。实际训练需本机 Python 3 和模型依赖可用，首次安装可能需要联网。

EfficientAD 首次训练还会下载预训练教师权重和 ImageNette 数据。若在 WSL 中遇到 `CERTIFICATE_VERIFY_FAILED`，应检查 WSL 内的代理和 CA 信任（Windows 的证书信任不会自动解决 WSL 内 Python 的证书错误）；可将 `Training:CaBundle` 配置为 WSL 内可读取的 PEM 证书包路径，重启服务后 Anomalib 训练子进程会继承该配置。不要通过关闭 TLS 证书校验解决。

### 📤 上传中心

🧭 所有上传入口（工程图片、分类图片、YOLO ZIP、验证图片/视频、ONNX 模型）共用一套常驻上传通道：

| 特性 | 说明 |
|---|---|
| 🔄 **切页不中断** | 上传任务由服务持有，切换页面、切回、页面重绘都不会中断，也不会丢进度 |
| 📊 **进度可见** | 横幅显示当前文件名、字节数、百分比与「已完成 / 总数」，完成后自动收起 |
| ⏹️ **可取消** | 取消立即生效：正在复制的文件会中断（流式读写带取消标记），已完成的部分自动清理 |
| 🧹 **失败可续** | 单个文件失败只提示该文件，不影响同批次其它文件 |

### 🖼️ 验证页

📤 一次最多上传 100 个图片或视频；每个模型各有一份独立的文件列表与识别结果，刷新浏览器后仍可恢复：

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
| 🔥 **后台预热** | 上传成功后后台悄悄生成预览（约 1~3 秒/张，不阻塞上传、失败不影响识别）✓ |
| 🖼️ **页面显示** | 文件列表、主视图、画布叠加全部用预览（**75 MB 的 BMP → 约 370 KB**），浏览器不再解码 5120×5120 大位图 ✓ |
| 🔍 **双击查看器** | **直接用原图**（要看就是看清细节，放大后依然清晰）；原图万一取不到才退回预览 ✓ |
| 🗑️ **删除** | 原图与预览一起删除；应用停止时会清理本进程创建的验证文件 ✓ |

> 💡 为什么不能"只靠 CSS 缩小显示"：浏览器必须先**解码整张位图**才会缩小 —— 5120×5120 单张就要约 100 MB 内存，列表里几张就够把主线程卡住（心跳发不出去还会被判定断线）。预览把解码成本从 100 MB 降到几 MB，这才是"丝滑"的关键。

| 配置（`appsettings.json`） | 默认 | 说明 |
|---|---|---|
| `Validation:Preview:Enabled` | `true` | 关掉就直接显示原图（不生成预览） |
| `Validation:Preview:MaxEdge` | `1600` | 预览最长边 |
| `Validation:Preview:TargetBytes` | `409600` | 预览体积上限（400 KiB） |
| `Validation:Preview:StartQuality` / `MinQuality` | `82` / `60` | 预览 JPEG 质量区间 |

> 🔗 列表状态写进地址栏：项目详情的**页码、搜索词、展开的类别文件夹**（`?page=3&q=…&folder=…`）与用户管理的搜索词（`?q=…`）刷新后都会恢复，链接也能直接分享；标注页当前是第几张图本来就在路由里（`/labeling/{工程}/{序号}`）。
> 📌 覆盖范围：**所有"看一眼"的位置**都用预览（验证页列表与主视图、项目详情的图片表格与文件夹封面、分类图片网格）；**标注页画布与查看器大图仍加载原图**（标注需要像素级精度）。识别本身始终用原图。
> ⏳ 加载反馈：验证页查看器、标注页首次打开与「上一页/下一页」在解码大图期间都会显示加载动画（标注页还会预加载相邻图片，切页通常瞬间完成）。
> ⚠️ 识别框的 `Position` 坐标是**原图像素**空间（例如 5120），而画布放的是预览图（1600）：前端会按原图尺寸把框**等比缩放**到画布上，所以叠加框位置准确；页面同时把原图地址作为兜底，预览取不到时会自动退回原图继续绘制。
### 📥 YOLO ZIP 导入（可反复增量上传）

把 `classes.txt` + `images/` + `labels/` 打成 ZIP，上传到检测工程的「导入 YOLO ZIP」。包内规则会逐个校验，不符合直接拒绝：

| 要求 | 说明 |
|---|---|
| 📄 `classes.txt` | 每行一个类名，或 `索引 名称`；索引必须从 0 连续 |
| 🖼️ `images/` | jpg / jpeg / png / gif / webp / bmp，与标注**一一对应**（多一个少一个都拒绝） |
| 🏷️ `labels/` | 与图片同名的 `.txt`，每行 `类别 cx cy w h`（归一化 0~1，空文件 = 纯背景图） |
| 📏 体积 | 单图 ≤ 100 MiB、图片数 ≤ 10000、单次上传 ≤ 1 GiB（超了请分包） |

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

| 特性 | 说明 |
|---|---|
| 🔢 **默认 300 轮** | 小数据集在 50 轮时只有几十次参数更新，模型学不到东西；Ultralytics 会按 `patience` 自动早停 |
| 🎯 **默认不切验证集** | 小数据集再切掉 10% 会明显影响训练；需要客观指标时在训练配置里勾选「使用验证集」 |
| 🩺 **训练前体检** | 日志直接给出每类实例数、图片数、目标像素尺寸、验证集大小，并对「注定识别不到」的数据逐条告警 |
| 🔬 **训练后自检** | 读取 `results.csv` 的 mAP；验证集过小时自动在**训练集**上按界面默认置信度复验，明确告知模型是否真的学到了东西 |
| 📥 **权重下载命令** | 检测到证书/下载类失败时，按当前系统生成可直接复制的 `curl` 命令（自动带上 `Training:Proxy` / `Training:CaBundle`），下完即被自动复用 |

🐍 训练环境（Python + venv + torch + ultralytics）由 Tasks 自动检测与搭建，代理与 CA 通过 `Training` 配置节统一控制。GPU 训练不会只判断“是否安装 torch”，还会核验 `torch.version.cuda` 与 `torch.cuda.is_available()`：Pascal / Volta / Turing 使用兼容面更广的 CUDA 11.8 wheel，Ampere 及更新架构在驱动满足 CUDA 12 要求时使用 CUDA 12.8 wheel；旧驱动自动选择兼容通道，无法安全使用 CUDA 的旧卡明确回退 CPU。

### 🎬 视频验证的 FFmpeg 部署

🎥 视频解码需要 `ffmpeg` 和 `ffprobe`（图片验证不依赖它们）。**上传视频时会自动自检**，缺失时：

| 平台 | 行为 |
|---|---|
| 🪟 **Windows** | 弹窗让用户选择：**手动指定路径**（填 `ffmpeg.exe` 或所在目录）或 **静默下载安装**（从 [GyanD/codexffmpeg](https://github.com/GyanD/codexffmpeg/releases) 取最新版，解压到程序目录 `tools/ffmpeg/win-<arch>/`），并在页面上显示下载/解压进度 |
| 🐧 **Linux（Ubuntu/Debian）** | 不弹窗，直接 `sudo -n apt-get install -y ffmpeg` 全局安装并显示进度；索引过期会自动 `apt-get update` 后重试；**失败才弹窗**（附手动指定路径兜底） |
| 🍎 **macOS / 其它** | 弹窗手动指定路径（或自行 `brew install ffmpeg` 后自动发现） |

📌 安装完成后路径会记入 `tools/media-tools.json`，视频解析直接复用；中文字体缺失时会随同一次流程安装（`fonts-noto-cjk`），保证视频标注里的中文不会画成方框。**下载或安装失败只做顶部提示，不影响图片上传与识别**。

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

🗄️ 工作台使用 SQLite 保存工程、用户和标注元数据。工程、标注任务、验证模型及数据库查询均按登录用户隔离；上传图片保存到 `wwwroot/data/uploads/<用户名>/`，ONNX 模型保存到 `wwwroot/onnxs/<用户名>/`，训练数据和产物保存到 `train/users/<用户名>/`，仅 `train/.env` 训练环境由所有用户共享。升级前已有的无归属数据自动归入 `snet`。删除任务或工程时会同步清理当前用户的对应文件。认证采用服务端 Cookie，会话页面、上传文件、模型下载和训练 Hub 均要求登录，普通用户不显示且不能访问用户管理页。

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

# 按部署硬件选择执行提供程序
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
| `ASPNETCORE_URLS` | 服务监听地址 | `http://localhost:5157` |
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

🚨 `YoloPoseViewModel` 集成**实时跌倒检测算法**（`FallDetector`），基于 17 个人体关键点进行多维度分析：

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
| | YOLOv11 | | | |
| | YOLOv12 | | | |
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

🎮 CUDA Tasks 在每次开始识别前确认当前构建与 GPU 环境。Windows / Linux x64 缺少 CUDA 12 与 cuDNN 9 时，程序通过 NVIDIA 官方 pip wheel 安装到应用私有目录 `train/cuda-runtime/`，不会修改系统驱动、`PATH` 或 `LD_LIBRARY_PATH`；按钮在准备期间显示进度并禁止重复点击。系统驱动仍由管理员维护：Windows 使用 NVIDIA 官方驱动，Ubuntu/Debian、Fedora/RHEL、SUSE、Arch 使用各发行版对应的 NVIDIA 驱动仓库；WSL 只更新 Windows 宿主驱动与 `wsl --update`，不要在 WSL 内安装 Linux 显卡驱动；容器还需要 NVIDIA Container Toolkit。macOS 不支持 CUDA，使用 CPU 或 MPS/CoreML 构建。详见 [ONNX Runtime CUDA 要求](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html)、[NVIDIA CUDA Windows 安装](https://docs.nvidia.com/cuda/cuda-installation-guide-microsoft-windows/) 与 [CUDA on WSL](https://docs.nvidia.com/cuda/wsl-user-guide/)。

## 💡 ONNX 模型导出

### 🐍 使用 Python (Ultralytics)

```bash
pip install ultralytics
python Snet.Py/Snet.Py.py
```

### ⌨️ 手动导出

```bash
# YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

> 📌 使用正确的 opset 版本可确保与 ONNX Runtime 的最佳兼容性与推理性能。

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
# CPU Tasks Web 工作台
docker run -d --name snet-yolo-tasks-cpu -p 8080:8080 \
  -v snet-tasks-data:/app/wwwroot/data \
  -v snet-tasks-db:/app/wwwroot/db \
  -v snet-tasks-train:/app/train \
  snet-yolo-tasks-cpu

# 确认镜像内 ffmpeg 和 ffprobe 都可用
docker exec snet-yolo-tasks-cpu ffmpeg -version
docker exec snet-yolo-tasks-cpu ffprobe -version

# CPU API
docker run -d -p 8080:8080 \
  -v /path/to/models:/app/wwwroot/onnxs \
  -v /path/to/data:/app/wwwroot \
  snet-yolo-api-cpu

curl http://localhost:8080/health   # 健康检查
curl http://localhost:8080/Operate/QueryAllAsync
```

> 📝 Linux Tasks 镜像中的 Debian `ffmpeg` 包同时提供 `ffmpeg` 和 `ffprobe`。CUDA 容器运行时需要 NVIDIA Container Toolkit 与可用 GPU。

## 🧪 测试

```bash
# 🧪 单元测试（xUnit）：上传中心、训练编排、数据集导出/体检、验证结果、媒体工具与 FFmpeg 安装等
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj

# Release 编译
dotnet build VisualIdentity.sln -c Release
```

## 🔒 安全特性

| 特性 | 实现方式 | 配置 |
|------|---------|------|
| 🌐 **CORS 控制** | `RestrictedOrigins` 策略 | `appsettings.json` → `AllowedOrigins` |
| 🛡️ **CSRF 防护** | Tasks 的 Cookie 会话表单使用 Antiforgery Token；独立 API 保持无状态客户端兼容 | 登录、退出等浏览器表单 |
| ⏱️ **速率限制** | 固定窗口算法 | `RateLimit` 配置节 |
| 🔐 **安全响应头** | 中间件自动注入 | X-Content-Type-Options / X-Frame-Options / CSP 等 |
| 📁 **文件名净化** | 过滤路径遍历字符 + GUID 唯一化 | 上传处理逻辑 |
| 📏 **文件大小限制** | Kestrel + FormOptions 双重限制 | API 默认图片 100 MiB、模型 1 GiB；Tasks 请求体 1 GiB |
| 🧹 **数据自动清理** | `HistoryFileHandler` 定时任务 | `RetentionDays`（默认 30 天） |

## 📈 性能优化

| 优化项 | 说明 |
|--------|------|
| 🔄 **模型实例缓存** | 配置不变时复用模型实例，避免重复加载 |
| 🧵 **异步全链路** | HTTP → GPU 推理 → 磁盘写入全链路 `async/await` |
| 🖼️ **并行写盘** | 原图 / 标注图 / JSON 详情 `Task.WhenAll` 并行写入 |
| 💾 **资源生命周期** | WPF `BitmapSource.Freeze()` 支持跨线程显示，Skia/ONNX 对象按所有权及时释放 |

> 📌 实际延迟取决于模型、输入尺寸、执行提供程序、GPU、TensorRT 配置与存储性能；仓库不声明脱离具体硬件和模型的固定毫秒数。

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
