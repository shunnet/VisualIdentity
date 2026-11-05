# EfficientAD 自动早停验证记录

日期：2026-11-05。范围：EfficientAD 训练配置、Python 训练回调、最佳检查点恢复、ONNX 导出和一致性门禁；不改变 YOLO、PaDiM、PatchCore 的训练策略。

## 实现依据与行为

- 核对 PyPI 官方 `anomalib==2.6.2` wheel 的 SHA-256 及其中的 EfficientAD、Evaluator、Engine、ModelCheckpoint 实现。该版本默认未配置验证指标；只添加 Lightning EarlyStopping 无法得到可监控的验证 AUROC。
- 为 EfficientAD 的现有 Evaluator 增加像素级 AUROC，每轮验证一次。使用 256 个固定阈值的流式统计，避免为这个新增指标保留所有验证像素。验证数据沿用产品现有的合成异常划分，不需要用户提供缺陷标注。
- 默认启用早停，耐心 50 轮、最小改善幅度 0.001；最大轮数仍是上限。界面、训练请求、状态快照和落盘 Python 配置传递同一套参数。关闭早停后仍保存验证 AUROC 最好的模型，但运行至轮数上限。
- 使用 Anomalib 自己的 ModelCheckpoint 子类，避免 Engine 额外注入默认检查点回调。训练结束后恢复最佳检查点中的权重，再使用该内存模型完成 ONNX 导出和校准一致性验证，防止“保存最佳模型、实际导出最后一轮模型”的偏差。
- 仅加载本次训练产物目录内生成的检查点；载入前核对路径和文件存在性。Lightning 检查点含预处理模块超参数，因此可信本地恢复使用 `weights_only=False`，不会接受外部上传的训练检查点。
- 监控指标缺失、NaN 或无穷值时失败，不导出或注册模型；正常结束仍需通过原有 ONNX 一致性、正常图误报率门禁。
- 日志包含早停配置、停止原因、已完成轮数和最佳轮次。当前已运行的旧 Python 任务不会被修改，更新后新启动的任务才使用此逻辑。

## 验证结果

| 层级 | 结果 | 具体覆盖 |
|---|---|---|
| 修改前回归 | 预期失败 | 最初 6 项 C# 用例暴露缺少配置/校验；2 项 Python 用例暴露缺少训练回调和恢复函数，确认测试可以识别旧行为 |
| 解决方案构建 | 通过，0 错误 | `VisualIdentity.sln` 全解决方案；294 项已有依赖、裁剪及 xUnit 分析警告 |
| C# 定向回归 | 15/15 通过 | 默认值、状态 Clone、启用/关闭、参数边界、NaN/正负无穷值、真实训练服务写入 Python 配置和状态回调、失败时不注册 |
| C# 全量回归 | 734/734 通过 | 0 Errors、0 Failed、0 Skipped、0 Not Run，用时 55.321 秒；覆盖仓库现有上传、验证、认证、硬件、训练编排等回归 |
| Python 回归 | 16/16 通过 | 真实 Lightning Trainer、Anomalib Evaluator/ModelCheckpoint/EarlyStopping；耐心和最小增量、关闭早停、上限先到、异常指标、可信路径边界、最佳权重恢复、真实 EfficientAD 检查点的严格载入 |
| 完整导出和门禁 | 通过 | 产品实际 Python 入口与真实 Anomalib Engine；受控轻量模型第 2 轮最佳、第 4 轮早停，恢复后真实导出 ONNX，ONNX Runtime 1.31.1 对 5 张正常校准图推理，一致性通过且误报 0 |
| 分布式停止 | 通过 | 两个真实 CPU/Gloo DDP 进程分别确认第 4 轮结束；全局进程 0 恢复第 2 轮最佳权重，非模拟线程或伪造 world_size |
| 浏览器配置交互 | 通过 | 隔离 Tasks 实例中实际登录、新建工程、上传 12 张不同图片、打开训练配置；默认启用/50/0.001，开关显隐、25/0.005 编辑、模型切换后草稿保留，PaDiM/PatchCore 隐藏早停项，最大轮数上限 10000 |
| 浏览器布局和本地化 | 通过 | Chrome 1440×1000 桌面、390×844 窄屏；中文亮色、英文暗色；窄屏无横向溢出，弹窗正文可滚动，未发生 pageerror；未点击开始训练或下载业务权重 |
| Tasks Release 发布 | 通过 | `dotnet publish Snet.Yolo.Tasks/Snet.Yolo.Tasks.csproj -c Release`，独立输出目录，退出码 0；包含宿主程序集和静态资源 |
| 文档与发布校验 | 已接入 | 中英文 README 同步说明默认值、最大轮数、最佳检查点、指标含义和生效范围；Release 的 validate 作业加入 Python 回归及双进程 DDP 检查 |

Python 最后一次回归：`16 passed, 20 warnings in 11.73s`。警告来自既有 timm/dinov2 弃用、Lightning 预处理模块超参数、CPU 数据加载 worker 以及旧版 ONNX 导出器等，不是回归失败；未通过屏蔽警告掩盖问题。

最终静态核对通过：中英文 README 头部、License、Star History 保持原样；新增双语资源无缺失或重复键；测试路径和发布命令对应实际文件；`git diff --check` 无空白错误。隔离浏览器宿主已停止，测试没有启动或取消用户的业务训练。

双进程结果（`stoppedEpoch` 是 Lightning 从 0 开始的内部轮次，其余轮次为人可读计数）：

```json
{"worldSize":2,"epochs":4,"stoppedEpoch":3,"bestEpoch":2,"bestScore":1.0,"restoredWeight":2.0}
```

## 可复现命令

在独立 Python 3.12 虚拟环境中安装依赖，避免改动训练中的环境：

```powershell
dotnet build VisualIdentity.sln --artifacts-path Snet.Yolo.Test/bin/earlystop-build -v:q
dotnet Snet.Yolo.Test/bin/earlystop-build/bin/Snet.Yolo.Test/debug/Snet.Yolo.Test.dll
python -m pip install torch==2.7.1 torchvision==0.22.1 --index-url https://download.pytorch.org/whl/cpu
python -m pip install -r Snet.Yolo.Test/Python/requirements.txt
$env:PYTHONUTF8='1'
$env:OMP_NUM_THREADS='1'
$env:MKL_NUM_THREADS='1'
$env:USE_LIBUV='0'
python -m pytest Snet.Yolo.Test/Python/test_anomalib_early_stopping.py -q --tb=short
python Snet.Yolo.Test/Python/anomalib_early_stopping_ddp.py --artifacts Snet.Yolo.Test/bin/earlystop-ddp-check
```

Python 测试直接从 `AnomalibPythonPipeline.cs` 提取产品内嵌脚本，不维护复制版本。完整入口测试只替换耗时的业务模型和数据模块，实际 Engine、回调、PyTorch 导出器、ONNX Runtime 和产品门禁均运行；另有实际 EfficientAD Small 的指标接入及包含预处理超参数的检查点恢复测试。

## 验证边界

- 本地 .NET：Windows x64、.NET 10；Python：3.12、PyTorch 2.7.1、Anomalib 2.6.2、Lightning 2.6.6、ONNX Runtime 1.31.1。双进程使用 CPU/Gloo，未宣称完成 Linux/NCCL 实际多 GPU 验收。
- 未使用用户正在训练的环境和数据，未执行业务规模的 EfficientAD 长时间训练，也未用随机或轻量模型的测试结果证明真实缺陷识别效果。
- 合成异常像素 AUROC 只能作为早停和最佳模型选择依据。接近饱和不代表业务缺陷召回达标，应使用独立真实正常图和缺陷图验收。
- GitHub Release 工作流增加了持续回归步骤，本地测试通过不等于远端 Release 工作流已运行；本次推送不创建发布标签。
