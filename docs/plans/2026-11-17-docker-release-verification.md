# Release Docker 目标架构修复与完整构建验证

## 失败原因

用户提供的日志对应 [Release 44049143262](https://github.com/shunnet/VisualIdentity/actions/runs/44049143262)，被测提交为 `1b5a62ed6994339b9be95a1f48b15839f4eec7f8`。API Dockerfile 在首个 FROM 前声明了无默认值的全局 `ARG TARGETARCH`，覆盖 BuildKit 自动注入的全局架构参数，最终的 `FROM runtime-${TARGETARCH}` 展开成 `FROM runtime-`，在解析阶段失败。

## 修复范围

- 删除 API Dockerfile 的全局 `ARG TARGETARCH`，保留 SDK 阶段内部声明，供 RUN 选择 `linux-x64` 或 `linux-arm64` RID。
- API、Tasks 的 SDK 阶段使用 `FROM --platform=$BUILDPLATFORM`，在构建机架构下交叉发布，避免通过 QEMU 执行整套 ARM64 SDK。
- 两个 Dockerfile 在最终镜像中执行 `dotnet --info`，检查对应架构的 .NET 主机与运行时信息；主机命令无法执行时直接阻断构建。
- Docker setup-qemu、setup-buildx、login 升级到 v4，build-push 升级到 v7，使用 Node.js 24；QEMU 仅安装实际需要的 arm64。
- 非 main 分支手动运行 Release 时，实际构建全部安装包和双架构镜像；GHCR 登录、镜像推送、版本标签和 GitHub Release 创建仍受发布条件保护。
- 不修改训练参数、推理代码、安装包名称、正式发布入口或版本号。

首轮临时分支验证 [44053051577](https://github.com/shunnet/VisualIdentity/actions/runs/44053051577) 又捕获到 Windows 图像测试收尾时的共享锁冲突（`AnomalibModelRegistryTests` 的测试 PNG）。将 11 个相关测试文件的 30 处目录收尾接入已有 `TestFileCleanup.DeleteDirectory`：只对 Windows 共享/锁冲突等待，最多两秒；业务操作和断言不重跑，其他错误及持续锁定仍失败。下一轮完整测试通过。

BuildKit 参数作用域参考：[Docker 官方文档](https://docs.docker.com/build/building/variables/)。

## 云端验证

运行：[Release 44054101479](https://github.com/shunnet/VisualIdentity/actions/runs/44054101479)，被测代码提交 `5458bcdc2f47f9c08dc86a8557fbab4469c7d774`，分支 `codex/verify-docker-release-20261117`。

| 检查 | 结果 |
| --- | --- |
| Windows .NET 11 解决方案编译 | 通过 |
| Windows .NET 测试 | 1066 通过，0 失败，0 跳过 |
| Windows Python 3.12 / PyTorch 2.7.1 CPU 回归 | 48 通过，0 失败，0 跳过 |
| Windows CPU/Gloo 双进程早停 | 通过，第 4 轮结束，恢复第 2 轮最佳权重 |
| Ubuntu Node.js 24 / Chromium | 24 通过，0 失败 |
| WPF win-x64 | 编译、压缩及 Artifact 上传通过 |
| Tasks / API × linux-x64、linux-arm64、win-x64 | 6 个安装包编译、压缩及 Artifact 上传通过 |
| Tasks Docker linux/amd64、linux/arm64 | 通过，两种架构的 dotnet --info 通过 |
| API Docker linux/amd64、linux/arm64 | 通过，两种架构的 dotnet --info 通过 |
| 非 main 发布保护 | 两个 GHCR 登录步骤均跳过，push=false，GitHub Release 作业跳过 |
| actionlint 1.7.12、17 段 PowerShell/Bash 脚本语法、git diff --check | 通过 |

下载并读取本轮 `release-validation-1` 的 TRX、Python JUnit 和 `ddp-result.json`，独立核对上述测试计数。DDP 结果为：

```json
{"worldSize":2,"epochs":4,"stoppedEpoch":3,"bestEpoch":2,"bestScore":1.0,"restoredWeight":2.0}
```

`stoppedEpoch` 是 Lightning 从零开始的内部轮次。两个 Docker 作业日志均确认最终运行时分别为 `Architecture: x64 / RID: linux-x64`、`Architecture: arm64 / RID: linux-arm64`。首次构建的缓存导出耗时较长，Tasks 约 547.5 秒，API 约 1223.3 秒；两者均成功结束。这是冷缓存逐层上传耗时，不能误判为构建卡死。整个工作流最终为 success，GitHub Release 作业按非 main 保护条件跳过。

## 验证边界

本次在真实 GitHub Windows/Ubuntu runner 上执行完整构建，未在用户本机或生产服务器上执行训练。非 main 手动运行设置 `push: false`，镜像构建结果保留在 Buildx 缓存，并未推送 GHCR；不会将缓存构建描述成正式镜像发布。没有创建或移动版本标签，没有创建 GitHub Release。真实 GPU 推理、生产容器启动和镜像仓库写入仍需正式发布及部署验收。
