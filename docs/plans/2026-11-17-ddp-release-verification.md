# Release 双进程早停检查修复

## 本次失败原因

用户提供的第二份日志实际运行了 `c50cf4741e5d8818fab9ca18439aaf960ea989e6`。Windows 编译、1066 项 .NET 测试和 48 项 Python pytest 均通过，随后 `anomalib_early_stopping_ddp.py` 在两个 rank 上断言失败。

该脚本未传入 `earlyStoppingEnabled`，产品默认值为 False，因此 Trainer 正常执行到 `max_epochs=8`；脚本却按启用早停的场景断言 `current_epoch==4`、`stopped_epoch==3`。这是回归脚本的配置与测试目标不一致，不能通过改回产品默认值、放宽轮数断言或跳过 DDP 门禁解决。

## 修复范围

- DDP 回归显式传入 `earlyStoppingEnabled=True`，与单进程早停测试的配置一致。
- 在启动 Engine 前确认早停回调确实存在；轮次断言失败时打印实际 world_size、current_epoch 和 stopped_epoch。
- 保留双 CPU/Gloo 进程、最佳第 2 轮检查点、恢复权重及第 4 轮停止的全部检查。
- 不修改产品默认值、训练实现、Python 依赖或 workflow 的测试门禁。中英文 README 补充显式测试模式说明。

## GitHub 原环境验证

通过临时分支 `codex/verify-ddp-early-stopping-20261117` 的 `workflow_dispatch` 实际运行现有 `release.yml`。触发前检查 WPF、packages、docker-linux、release 四个作业的非 main 手动执行保护；该次运行仅验证，不创建版本标签或发布产物。

运行：[GitHub Actions 44047642363](https://github.com/shunnet/VisualIdentity/actions/runs/44047642363)，被测代码提交 `a1424bb313a8fb5587f615f0d516f2099d4e0528`。

| 检查 | 结果 |
| --- | --- |
| Windows 解决方案编译 | 通过 |
| Windows .NET | 1066 通过，0 失败，0 跳过 |
| Windows Python 3.12 / PyTorch 2.7.1 CPU 回归 | 48 通过 |
| Windows CPU/Gloo 双进程早停 | 通过，两个进程于第 4 轮结束 |
| Ubuntu Node 24 / Chromium | 24 通过，0 失败 |
| 诊断产物 | release-validation-1、release-browser-1 上传成功 |
| Python 语法、git diff --check | 通过 |

下载并读取本次云端 `release-validation-1` 产物中的 `ddp-result.json`，得到：

```json
{"worldSize":2,"epochs":4,"stoppedEpoch":3,"bestEpoch":2,"bestScore":1.0,"restoredWeight":2.0}
```

`stoppedEpoch` 为 Lightning 从零开始的内部轮次，其余轮次为人可读计数。源码修复已在真实 GitHub Windows runner 上验证，不以静态检查或 Linux 运行结果替代。本次没有在用户本机执行训练，也没有改动生产服务器环境。

正式安装包、Docker 多架构构建和 GitHub Release 发布需另从包含修复的 main 提交运行；本次安全验证不声明这些发布步骤已执行。
