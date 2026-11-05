# WPF 运行时硬件切换自检记录

## 改造结果

- `Snet.Yolo.Tool` Windows x64 统一使用 ONNX Runtime GPU 1.31.1 原生包，其中包含 CPU 执行提供程序；删除 `UseCuda` / `USE_CUDA` 编译分支。
- 窗口顶部通过 `Snet.Windows.Controls.ComboBoxControl` 选择 CPU 或 NVIDIA GPU，GPU 编号使用 `TextBoxControl`，“应用硬件”使用 `ButtonControl`。沿用现有图标、语言资源及绑定方式。
- 默认 CPU，GPU 编号从 0 开始，一次推理使用一张指定的 GPU；CPU 模式隐藏 GPU 参数。
- 在后台创建目标 CUDA 探测会话，再原子保存配置；检查、取消或写入失败均保留原配置和缓存。界面区分待应用选择与当前设备，应用期间禁止重复操作。
- 配置持久化到 `%LOCALAPPDATA%\Snet\VisualIdentity\Tool\onnx-runtime.json`，独立于 Tasks/API，重启后恢复。
- 检测、分类、分割、姿态和定向检测都经共同基类直接调用 Server，使用相同的已保存设备快照。
- 每种任务只缓存最近使用的模型，同模型复用；路径、文件时间或长度变化后替换会话。活动识别通过租约保护，退役后等最后一个使用者归还再释放，避免切换时中断识别或积累旧显存。
- 运行服务同时持有执行提供程序，首张图片尚未运行或模型初始化失败时也能释放原生资源。
- 中英文 README 同步发布包、硬件选择、配置位置与 CUDA 前提；发布工作流注明使用统一包。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 全解决方案构建 | 通过，0 错误 |
| 新增桌面运行服务回归 | 8 项通过 |
| 全量 xUnit | 719 项通过，0 失败、0 跳过 |
| Windows x64 Release 发布 | 通过 |
| 真实 WPF 窗口与 Snet 控件 STA 检查 | 通过 |
| 发布目录原生 ONNX Runtime | 仅一份 onnxruntime.dll，SHA-256 与 GPU Windows 1.31.1 包一致 |
| 发布依赖与旧程序集 | 无 CPU 原生包重复依赖，无 YoloDotNet.dll |
| 中英文资源和 README | 硬件资源键一致，无重复；头部、License、Star History 未改动 |
| Git 差异检查 | 通过 |

8 项服务回归使用同一份不依赖 WPF 的运行服务源码，在现有跨平台测试宿主中编译，覆盖：配置重建后恢复、同设备同模型复用、不可用 GPU 回退、活动租约退役、关闭时保护使用者、无效模型替换、保存失败、并发获取及原生 CPU 推理。未增加新的解决方案项目。

WPF 检查加载真实窗口、主题、控件和视图模型，以独立临时配置验证：Snet 下拉框选中项双向绑定、GPU 参数显示与隐藏、编号校验、应用期间禁用、失败提示及原配置保留、CPU 保存和重建、错误清理，以及 1400 / 850 宽度的紧凑布局。窗口在屏幕外测试，不写入用户的实际硬件设置。

## 检查命令

```powershell
dotnet build VisualIdentity.sln --artifacts-path Snet.Yolo.Test/bin/tool-runtime-build -v:q
dotnet Snet.Yolo.Test/bin/tool-runtime-build/bin/Snet.Yolo.Test/debug/Snet.Yolo.Test.dll
dotnet publish Snet.Yolo.Tool/Snet.Yolo.Tool.csproj -c Release -r win-x64 --self-contained false -o <全新发布目录>
```

## 边界

- 本机 GTX 1060 不属于当前 CUDA 13 推理目标；已验证真实 CPU 推理和 GPU 失败回退，未声称完成 RTX 30 / 40 / 50 实机 GPU 推理。
- WPF 不调用 Tasks 的 CUDA 自动安装器；GPU 部署仍须提供兼容驱动及可加载的 CUDA 13 / cuDNN 9。CPU 不初始化 CUDA。
- 保留既有 Pkcs NU1902 / NU1903、裁剪和 xUnit 分析告警，没有压制告警；未进行真实业务模型精度或吞吐基准测试。
- 未触发 GitHub Release 或 Docker 构建；本次修改不改变 Web API、Tasks、SAM 或 Python 训练设备配置。
