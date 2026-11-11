# YOLO 训练显存方案验证（2026-11-11）

目标：单卡 YOLO 训练在不损失精度的前提下降低显存占用，并且不再因为训练尺寸稍大就显存不足。先查官方文档与源码，再用本机真实训练对照。

## 改动前的情况

单卡训练不传 `batch`，一律使用 Ultralytics 默认的 16，不管训练尺寸和显卡大小；界面上也没有批大小可调。显存占用大致与 `批大小 × 尺寸²` 成正比，训练尺寸从 640 提到 1280，同样的批大小要多用约 4 倍显存。

## 官方文档与源码能确认的事

| 结论 | 出处 |
|---|---|
| `batch` 可以是整数、`-1`（AutoBatch，目标约 60% 显存）或 0–1 的小数（显存占比，如 `batch=0.70`） | Ultralytics 文档 Train 页「Note on Batch-size Settings」、`reference/utils/autobatch` |
| AutoBatch 用批大小 1、2、4、8、16（显存 ≥ 16 GB 时到 64）试跑并线性拟合；估算时已计入多尺度的最大尺寸、数据集里单张图最多的目标数（×4 马赛克）和训练集图片数 | `ultralytics/utils/autobatch.py`、`engine/trainer.py` 的 `auto_batch`（本机 8.4.102 源码核对） |
| 第一个轮次内出现显存不足时，训练器自动把批大小减半重试，最多 3 次；只对单卡有效，多卡（DDP）直接报错 | Train 页同一段说明；`engine/trainer.py` 的 `_oom_retries`（本机 8.4.102 已含） |
| 梯度累积是内置的：名义批大小 `nbs=64`，每 `round(nbs / batch)` 个批次更新一次，权重衰减按 `batch × accumulate / nbs` 缩放 | 文档 `usage/cfg` 训练参数表；`engine/trainer.py` 的 `_build_train_pipeline` |
| AMP 默认开启（`amp=True`）；启动时做一次自检，自检失败才退回 FP32 | 文档 `guides/model-training-tips`「Mixed Precision Training」；`utils/checks.py` 的 `check_amp` |
| `expandable_segments` 是 PyTorch 分配器的实验性选项，用于减少张量大小不断变化带来的碎片；`PYTORCH_ALLOC_CONF` 的旧别名 `PYTORCH_CUDA_ALLOC_CONF` 仍可用 | PyTorch 文档 `notes/cuda`「Memory management」 |

## 逐条核对给出的方案

| 方案 | 结论 |
|---|---|
| `batch=-1` / `batch=0.7` | 官方功能，采用 `batch=-1`（单卡）。 |
| OOM 自动重试 | 官方内置，不需要再做；只覆盖第一个轮次。 |
| 保持 Ultralytics 更新 | 本机 8.4.102 已含 OOM 重试；「v8.4.42 专门改进显存清理」没能在官方文档里核实，不作为依据。 |
| AMP | 本来就默认开启，不需要改；本项目已把 `yolo26n.pt` 预先放进 Ultralytics 权重目录，离线时 AMP 自检也能做。 |
| BF16（`amp="bf16"`） | 最新文档有，本机 8.4.102 没有；BF16 与 FP16 同为 2 字节，不比 AMP 更省显存，不采用。 |
| 梯度累积 | Ultralytics 已按 `nbs=64` 自动累积，不需要另配；这正是「调小批大小不降精度」的依据。 |
| 冻结层 | 官方参数，但有精度代价，只适合迁移学习，不自动启用。 |
| 降低 `imgsz` | 有精度代价（小目标），不自动启用；显存仍不够时在错误提示里建议用户调小。 |
| 多尺度 | 默认关闭；AutoBatch 已按最大尺度估算。 |
| `expandable_segments` | 采用，但只在原生 Linux 单卡训练上启用（见下）。 |

## 代码改动

- 单卡 GPU 训练传 `batch=-1`；多卡（DDP）不支持 AutoBatch，仍传明确的全局批大小；CPU 与 Apple 芯片不传（AutoBatch 在这些设备上只会警告后退回 16）。
- 原生 Linux 单卡训练给子进程加 `PYTORCH_CUDA_ALLOC_CONF=expandable_segments:True`：Windows 的 PyTorch 不支持（本机实测 2.7.1 会打印 `expandable_segments not supported on this platform` 后忽略），WSL2 的能力不确定，多卡与 NCCL 的组合没有验证；运行环境里已经配置过 `PYTORCH_CUDA_ALLOC_CONF` 或 `PYTORCH_ALLOC_CONF` 时不覆盖。Anomalib 流水线早已默认启用同一选项并在 Linux 服务器上跑通。
- 显存不足的提示改为用户能照着做的内容：已经自动选批大小，能调的是训练尺寸与模型；`AutoBatch: no usable batch size found` 与 `CUBLAS_STATUS_ALLOC_FAILED` 也归入这一类。

## 本机真实对照

环境：Windows 11，GTX 1060 6 GB（桌面程序占用约 2.5 GB），Python 3.12.10，torch 2.7.1+cu118，ultralytics 8.4.102。数据：合成的目标检测数据集（矩形 / 椭圆两类，含小目标与噪声，240 张训练图、60 张验证图，640 × 640）。模型 `yolo26n.pt`，10 轮，种子 0，AMP 自检通过。

| 配置 | 批大小 × 累积 | 训练显存峰值（PyTorch 保留） | nvidia-smi 增量 | mAP50 | mAP50-95 |
|---|---|---|---|---|---|
| `batch=16`（此前做法） | 16 × 4 | 3.01 GB | 3222 MiB | 0.881 | 0.837 |
| `batch=-1`（AutoBatch） | 9 × 7 | 1.63 GB | 2736 MiB | 0.869 | 0.827 |
| `batch=4` | 4 × 16 | 0.91 GB | 1005 MiB | 0.876 | 0.830 |

- 三种配置的有效批大小都约为 64；显存峰值分别下降 46%（AutoBatch）和 70%（批大小 4）。
- 精度：最终 mAP50-95 相差 0.007–0.010，每个配置的最后三轮自身就有 0.01–0.02 的轮间起伏，没有看到系统性下降。每个配置只跑了一次，**这不构成统计结论**；换种子重复的对照因本机系统可用内存（提交量）吃紧，被我加的保护机制中止，没有完成。
- 速度：批大小越小，每轮越慢。批大小 4 约 27 秒 / 轮，批大小 16 约 14 秒 / 轮（约 1.9 倍）；AutoBatch 那一次与编译任务重叠，耗时不可比。
- 1280 尺寸：AutoBatch 选出批大小 2（预估 3.64 / 6.00 GB），实际保留显存峰值约 1.4 GB，第一个轮次正常跑完。固定 16 在 Windows 上不会报显存不足，驱动会把超出的显存溢出到系统内存（进程私有内存涨到约 12 GB，系统可用提交量只剩约 1 GB，约 3 分钟都没跑完第一个轮次，而 640 尺寸同样的批大小每轮只要约 14 秒），已手动中止；把进程显存上限卡在显卡自身范围内模拟 Linux 的真实 OOM 时，日志出现 `CUDA out of memory with batch=16. Reducing to batch=8 and retrying (1/3).`，说明官方首轮重试生效，之后同样因系统提交量逼近上限被中止。
- 其他任务：分割、定向框、分类各用 `batch=-1` 跑 1 轮，都正常完成（用检测数据集派生的小数据集；分割在 320 尺寸下 AutoBatch 选 28，定向框选 37；分类在 64 尺寸下 AutoBatch 的估算超出安全范围，按官方逻辑退回默认的 16）。姿态任务没有单独跑，它与分割、定向框共用检测训练器里同一段 AutoBatch 代码。
- AutoBatch 的预估偏保守：640 时预估 3.86 GB、实际保留 1.63 GB；1280 时预估 3.64 GB、实际 1.41 GB。它算「可用显存」时用总显存减去**本进程**已占用的部分，不扣除其他程序占用的显存，所以显卡被别的程序占着时仍会按整张卡估算。

## 没有验证的与已知限制

- 没有在 Linux 服务器（RTX 4080）上复测：`expandable_segments` 对 YOLO 的效果、真实 OOM 下的首轮重试行为、AutoBatch 在 16 GB 显卡上选出的批大小，都只有官方文档与本机 Windows 的结果可依据。
- AMP 对显存的作用没有在本机对照，依据官方文档；各次运行里 AMP 自检均通过。
- 精度只做了单次、单种子、合成数据、10 轮的对照，结论仅限于「没有看到明显下降」。
- AutoBatch 选出的批大小很小（例如 1–2）时，BatchNorm 的统计量来自很少的图片，精度可能受影响；这种情况应调小训练尺寸或换更小的模型，而不是继续硬塞。
- 数据体检里「batch 16 时每轮约 N / 16 次参数更新」的说法本来就偏乐观（Ultralytics 按 `nbs=64` 累积，实际约 N / 64 次），这次没有改动。

## 自动化回归

新增 `YoloVramPlanTests`（18 个用例）：单卡传 `batch=-1`、多卡保持明确批大小、CPU / Apple 芯片 / 自动设备不传、Linux 单卡启用可扩展显存段且 Windows / WSL / 多卡 / 已有配置时不启用、三类显存失败都得到可操作的提示且与显存无关的报错不受影响。
