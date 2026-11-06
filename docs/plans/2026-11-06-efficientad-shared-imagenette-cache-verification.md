# EfficientAD ImageNette 共享缓存修复与验证

日期：2026-11-06。

## 问题与原因

EfficientAD 的 ImageNette 是训练辅助数据，不是工程内的正常图片。Anomalib 2.6.2 默认使用相对目录 `./datasets/imagenette`，而 Tasks 为每次训练新建 `anomalib-run-<标识>` 工作目录。因此，不同运行不能自动复用上一次的数据。上游下载流程还会先创建目录；连接失败留下的空目录不能代表数据已准备完成。

现场日志的 `Connection refused` 表示下载连接被拒绝，不能归因于正常样本数量、显卡或前面的弃用警告。本次没有通过关闭 TLS 校验或绕过代理解决网络问题。

## 修改范围

- 配置通过绝对路径指定程序目录下的 `train/anomalib/.cache/imagenette`，跨用户、工程和运行共用。
- 保留完整 `imagenette2.tgz`，验证 Anomalib 固定下载信息中的官方 SHA-256；在临时目录中调用 Anomalib 的安全解压方法，检查 ImageFolder 后写入完成标记，再发布到 `dataset/`。
- 已完成的数据直接复用，不联网、不重新解压；完整归档也可离线重建数据目录。
- 文件锁串行准备缓存；失败不发布半成品，下一次准备清理取消操作留下的解压暂存目录。连接失败、HTTP 503、响应中断等临时错误最多尝试三次；永久 HTTP 错误直接报告。
- 将 `.cache` 加入训练共享保留目录，防止同名用户名占用缓存；普通用户名的路径不变。
- `.env` 重置仅重建算法环境，单工程删除只处理工程目录。现有 Linux/Windows Docker 的 `train` 卷及写权限覆盖此缓存，不需要增加新卷。
- PaDiM、PatchCore 不准备 ImageNette；EfficientAD 的教师—学生训练、早停、最佳权重恢复、导出和门禁逻辑不变。
- 中英文 README 同步说明缓存位置、离线放包、Docker 持久化及新任务生效边界。

## 自动化回归

先执行失败回归：缺少共享准备函数时，9 项 Python 缓存测试全部失败；未传共享路径时，新增 .NET 配置测试失败。另验证 `.cache` 和 `.CACHE` 两个用户名会命中共享目录，修复后重新通过。

| 检查 | 结果 | 范围 |
|---|---|---|
| .NET 定向回归 | 70/70 通过 | AnomalibTrainingTests、TrainingEnvironmentResetTests、TrainingStoragePathTests、TasksHostRegressionTests |
| 服务器 Python 回归 | 37/37 通过 | 包含新增 9 项共享缓存测试，以及既有早停、正常阈值、ONNX 导出和逐图校验测试 |
| 服务器 CPU/Gloo 双进程 | 通过 | 两个进程均在第 4 轮结束，恢复第 2 轮最佳权重，最佳 AUROC=1.0；受控模型，不代表业务准确率 |
| 解决方案 Release 编译 | 通过，0 错误 | 12 条现有 NuGet 漏洞警告，均指向 System.Security.Cryptography.Pkcs 9.0.18；本次未修改依赖版本 |

缓存测试使用极小 JPEG 归档和回环 HTTP 服务，实际覆盖下载、503 重试、连接拒绝、截断响应、错误 SHA、损坏归档、解压失败后离线重建、跨工程工作目录复用和并发准备，不下载真实大数据包。

本地仅运行 .NET 配置/文件流程回归，没有运行业务模型训练或推理。Python 测试和实际缓存准备均在用户服务器执行；测试工具装入隔离的 `/var/tmp/snet-imagenette-regression/site`，没有改变生产 Python 环境中的依赖。

复现命令：

```powershell
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-restore --artifacts-path Snet.Yolo.Test/bin/calibration-release --filter "FullyQualifiedName~AnomalibTrainingTests|FullyQualifiedName~TrainingEnvironmentResetTests|FullyQualifiedName~TrainingStoragePathTests|FullyQualifiedName~TasksHostRegressionTests"
dotnet build VisualIdentity.sln -c Release --no-restore --artifacts-path Snet.Yolo.Test/bin/calibration-release
```

服务器在对应测试目录和隔离测试工具路径下执行：

```bash
PYTHONPATH=/var/tmp/snet-imagenette-regression/site PYTEST_DISABLE_PLUGIN_AUTOLOAD=1 CUDA_VISIBLE_DEVICES='' OMP_NUM_THREADS=1 MKL_NUM_THREADS=1 \
  train/anomalib/.env/bin/python -m pytest /var/tmp/snet-imagenette-regression/Snet.Yolo.Test/Python -q

PYTHONPATH=/var/tmp/snet-imagenette-regression/site CUDA_VISIBLE_DEVICES='' OMP_NUM_THREADS=1 MKL_NUM_THREADS=1 \
  train/anomalib/.env/bin/python /var/tmp/snet-imagenette-regression/Snet.Yolo.Test/Python/anomalib_early_stopping_ddp.py \
  --artifacts /var/tmp/snet-imagenette-regression/ddp
```

## 服务器真实缓存检查

没有停止用户训练或重启服务。现场下载的完整归档已保留到程序的共享缓存目录；只有完整文件的摘要与官方值相同后才发布为正式缓存归档，没有替换生产程序。

| 项目 | 实测值 |
|---|---|
| 归档 | `train/anomalib/.cache/imagenette/imagenette2.tgz` |
| 大小 | 1,557,161,267 字节 |
| SHA-256 | `6cbfac238434d89fe99e651496f0812ebc7a10fa62bd42d6874042bf01de4efd` |
| 可枚举图片 | 13,394 张 JPEG |
| 第一次准备 | 从已有归档校验并解压，8.930 秒 |
| 第二次准备 | 直接复用，0.010 秒 |
| 两次联网请求 | 0；检查中拦截 `urllib.request.urlopen`，若联网立即报错 |

检查使用当前源码生成的隔离脚本，只准备辅助数据，没有启动真实 EfficientAD 拟合、导出或业务推理。以上耗时是该服务器本次检查值，不是性能承诺。

## 交付边界

部署更新后的 Tasks 程序，新启动的 EfficientAD 训练才会将 `imagenet_dir` 指向共享数据。服务器旧进程和正在运行的旧任务不会被源码修改自动更新。缓存复用不能保证外部网络可用，也不能替代模型训练效果验收；教师预训练权重仍由原有 Anomalib 缓存机制管理。
