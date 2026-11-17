# Release 失败修复与验证

## 原始失败

用户提供的 2026-11-17 日志显示：解决方案编译为 0 Error，测试 1065 项中 1 项失败。失败是 `PersistentTransferTests.Upload_RestoresOffsetsAndImportsExactlyOnce(ProjectImages)` 检查上传暂存 `.part` 应清空时仍发现文件，发生在发布包及 Docker 构建之前。Node 20 Action 警告不是这次退出码 1 的直接原因。

原实现先公开 `completed`，随后在 finally 保存并删除分块，测试与页面均可能在清理前看到终态。新增回归通过只读共享锁使真实清理进入日志分支，同步观察 API 状态和页面快照：旧代码稳定得到 `completed`，修复后清理时仍为 `processing`，清理步骤结束后才公开终态。保留原有分块清理断言，没有放宽断言或跳过用例。直接取消上传也改为先清理再公开取消状态。

## Workflow 调整

- Checkout、setup-dotnet、setup-python、Artifact 上传/下载改用已确认的 Node 24 兼容主版本，SDK、Python 和训练测试依赖不变。官方定义：[checkout v5](https://raw.githubusercontent.com/actions/checkout/v5/action.yml)、[setup-dotnet v5](https://raw.githubusercontent.com/actions/setup-dotnet/v5/action.yml)、[setup-python v6](https://raw.githubusercontent.com/actions/setup-python/v6/action.yml)、[upload-artifact v6](https://raw.githubusercontent.com/actions/upload-artifact/v6/action.yml)、[download-artifact v7](https://raw.githubusercontent.com/actions/download-artifact/v7/action.yml)。
- 并行增加 Node 24/Chromium 浏览器门禁；WPF、Tasks/API 包和 Docker 构建均依赖 .NET/Python 与浏览器检查通过。Artifact 下载仍只匹配 `Snet.Yolo.*` 安装包，不会把诊断日志附入 Release。
- 编译日志、测试日志、TRX、Python JUnit 和浏览器合成截图由 `if: always()` 保存 7 天。只上传明确的诊断路径，不打包整个 bin、数据库或训练目录。
- PowerShell 记录日志后显式保留失败退出码；pip/pytest 的首条命令失败后立即退出，不允许后续命令覆盖失败结果。浏览器日志管道显式使用 `shell: bash`，启用 pipefail。
- ZIP/tar.gz 上传 Artifact 时设置 compression-level 0，减少重复压缩；发布目标、架构、权限和标签保护不变。

干净目录验证还暴露了测试收尾的顺序依赖：非 LOGO 用例不再清理未创建的 LOGO 草稿目录。完整测试中的缩略图/SAM 临时图片在 Windows 删除时出现短暂共享冲突，具体占用者未确认；仅测试自建临时文件收尾改为最多约 2 秒等待 Win32 32/33，其他异常立即抛出，持续锁超过上限仍失败。不重跑业务操作或断言，也不修改生产图像处理代码。

## 本地验证

| 项目 | 结果 |
| --- | --- |
| 新回归修改前 | 按预期失败：清理时原状态为 completed |
| 最终完整 .NET Release 测试 | 1066 通过、0 失败、0 跳过，生成 release-complete.trx |
| 浏览器全套 | 24 通过、0 失败 |
| actionlint 1.7.12 | 通过，工具 ZIP 校验 SHA-256 后使用 |
| 内嵌脚本 | 17 段 PowerShell/Bash 语法检查通过 |
| 日志管道失败保持 | PowerShell 模拟退出 7、Bash 管道模拟退出 1，均保留原失败码 |
| 测试清理边界 | 真实临时文件短暂锁释放后清理成功；持续锁超过上限仍抛 IOException |
| git diff --check | 通过 |

中间完整检查先后遇到缩略图和 SAM 测试的临时图片删除占用，未宣称这些轮次通过；加入有限的测试收尾处理后最终全套通过。日志、TRX、工具和一次性检查程序位于忽略目录 `Snet.Yolo.Test/bin/`，不提交生成数据。

本次未执行真实 GPU 训练、完整 Python 训练回归、Docker/ARM64 构建或远端 GitHub 发布；保留原 Python 门禁。没有创建/移动版本标签，也没有上传生产安装包。需从包含本次修复的新提交触发发布；重跑旧标签仍执行旧提交代码。
