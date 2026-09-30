# Snet.VisualIdentity.JointVerificationDemo

独立的 .NET 10 控制台解决方案，通过 `Snet.Yolo.Tasks.Core → Snet.Yolo.Server` 引用链执行联合验证，不启动 Tasks 网站，不请求 HTTP API。执行提供程序通过项目引用自动还原；无需手工安装 NuGet 包。需要在本仓库中打开此解决方案，Core 和执行提供程序为相邻项目引用。

## 准备与运行

1. 修改 `demo.json`，设置 Anomalib 的 `model.onnx`、配套 `model.manifest.json`，YOLO 的 ONNX 文件和待识别图片。可以填写绝对路径；相对路径相对于配置文件目录。
2. Anomalib 模型使用本系统导出的模型包，解压得到上述两个文件；摘要、预处理和输出契约仍由 Server 校验。不要自行猜测输入尺寸或归一化。YOLO 必须为目标检测或实例分割 ONNX，配置中的 Type 必须与模型一致。
3. 默认 CPU 运行：

以下命令在本 Demo 目录内执行；也可用 Visual Studio 打开同目录的 `Snet.VisualIdentity.JointVerificationDemo.sln`。

```powershell
dotnet run --project Snet.VisualIdentity.JointVerificationDemo.csproj -- "D:\models\demo.json"
```

CUDA 构建（需要已安装兼容的 NVIDIA 驱动、CUDA/cuDNN，GpuId 指定显卡）：

```powershell
dotnet run --project Snet.VisualIdentity.JointVerificationDemo.csproj -p:UseCuda=true -- "D:\models\demo.json"
```

切换硬件发布到不同目录，避免 CPU/GPU 原生运行库混放：

```powershell
dotnet publish -c Release -p:UseCuda=false -o publish-cpu
dotnet publish -c Release -p:UseCuda=true -o publish-cuda
```

未指定配置路径时读取程序集目录的 `demo.json`。示例不提供或自动下载业务模型；路径无效会提示中文错误并返回非零退出码。CUDA 依赖沿用仓库的 GPU 包版本；不保证新显卡可用，请按主 README 的兼容性说明调整依赖。

## 配置与输出

- Anomalib：PixelThreshold 默认 0.8，MinimumArea 默认 4（异常图像素）。这两个参数仅过滤区域，不改变整图异常判定。
- YOLO：Confidence、Iou；Segmentation 模式额外使用 PixelConfidence。分割实例按外接框关联异常，与联合验证页面相同，不输出实例掩码覆盖图。
- Mode：Joint、AnomalibOnly、YoloOnly。Joint 先定位异常；整图正常则跳过 YOLO，异常时对原图识别，再调用 Server 的 JointValidationMatcher 关联区域。未匹配缺陷的异常区域仍为“类型未识别”，不是正常。
- FontPath：可选中文字体。Windows 默认匹配微软雅黑；Linux 推荐配置 Noto Sans CJK 的实际字体路径，否则中文标签可能显示方框。
- IncludeHeatmap：默认 true；视频平台可设为 false，省去每帧热图 PNG/base64 编码，此时不保存 heatmap.png。
- 输出在 `AppContext.BaseDirectory/result/时间戳-唯一标识/`：annotated.png（红色异常框、蓝色 YOLO 框）、heatmap.png（有 Anomalib 时保存）、result.json（原图坐标、类别、分数、匹配关系与耗时）。原图和业务模型不会被修改。JSON 不重复保存热力图 base64。

## 代码层次与视频平台接入

```text
Program.cs                         配置读取、取消、日志与流程编排
Configuration/DemoOptions.cs       两类模型、识别参数、路径与校验
Inference/RuntimeFactory.cs        CPU/CUDA 硬件创建
Inference/JointVerificationEngine.cs  会话复用、级联推理、空间关联与释放
Output/ResultWriter.cs             结果绘图与文件保存
```

视频平台在启动时创建一个引擎，在循环中调用 `IdentifyAsync`，退出时停止接收帧、等待已有任务结束并 `await DisposeAsync()`。YOLO 提供程序提前加载；Anomalib 首帧校验并缓存会话。一个实例串行处理，不无限并行提交帧；建议外部使用有界队列、丢弃过期帧，并根据实际帧率与显存决定每路或每 GPU 的实例数。

本 Demo 复用底层 Server 推理与匹配组件，**不调用页面的 JointValidationService**：后者含用户模型查询，且 YOLO 每次请求创建会话；实时处理不应逐帧查询数据库或重复加载模型。Demo 直接使用明确配置的本地模型，不能替代对外接口的鉴权与用户隔离。

当前 Anomalib 公共推理接口接收图片文件路径，所以这是图片文件接入示范，**不是已完成的视频解码、零拷贝帧输入或实时吞吐保障**。视频平台若只有内存帧，需要适配 Server 的内存帧入口或受控临时文件桥接，并测量其 I/O 成本；不能把逐帧写盘当作高吞吐方案。绘图可直接使用 `ResultWriter.Render` 返回的位图，不必逐帧调用 SaveAsync；热力图编码与输出写盘也应按需关闭或降低频率。首帧冷启动耗时不等于稳定帧耗时，需真实模型、分辨率、设备压测。

原生推理内核不能被 CancellationToken 强制打断，取消会在进入推理前或内核返回后生效。CPU/GPU 是构建选择，不混装两个 ONNX Runtime 原生包；单实例不会自动把一帧拆分到多个 GPU。
