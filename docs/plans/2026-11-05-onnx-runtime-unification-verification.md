# ONNX Runtime 与 Tasks 合并自检报告

日期：2026-11-05。对应开发计划：`2026-11-05-onnx-runtime-unification.md`。

## 改造结果

| 检查项 | 结果 |
| --- | --- |
| Server 内置执行提供程序 | CPU、CUDA、TensorRT 配置及公共张量推理核心位于 `Snet.Yolo.Server.inference`；原两个独立项目已移除 |
| Tasks 合并 | 单一 `Snet.Yolo.Tasks` 项目及程序集；旧 CPU/CUDA 宿主和解决方案引用已移除 |
| API 硬件边界 | 保留 CPU/CUDA 两个项目，各自直接引用一种原生包，不依赖 Tasks 设置 |
| WPF / Demo | 命名空间和依赖同步；保留 `UseCuda` 构建选择，CPU/GPU 原生包二选一 |
| 全站硬件配置 | 默认 CPU；管理员独立应用 CPU/CUDA 与单 GPU 设置，原子保存到持久化 data 目录 |
| 权限与失败处理 | 服务端再次校验管理员；非法配置、存储失败、CUDA 准备失败不覆盖旧快照；不静默更换执行设备 |
| 会话切换 | Anomalib 捕获不可变硬件快照；切换后新建对应会话，退役会话等待全部租约结束；删除模型等待所有设备的活动租约 |
| SAM / 训练 | 保留各自独立设备选择，不随全站验证设置切换 |
| 性能与所有权 | 公共推理核心缓存张量元数据，锁保护会话生命周期；半精度输出复用缓冲池且部分初始化失败归还已租缓冲区；CUDA 原生库只缓存成功准备结果 |
| 类型与注释 | 使用 Roslyn 检查本次新增、迁移及缓存改造类型：每文件一个类型，函数/构造函数/属性摘要及函数参数注释检查零遗漏；注释为中文，原许可证标记保留 |
| 发布流程 | Tasks 发布矩阵改为单产品；API 保留两种产品；版本读取、归档、镜像及发布说明同步 |
| Docker | Tasks 合并为单个 Linux 镜像；ARM64 选 CPU 包；修正 Docker amd64 到 .NET x64 的 RID 映射；保留 Linux 卷权限，补齐 Windows Tasks 数据、数据库、训练与 SAM 目录修改权限 |
| 文档 | 中英文 README 与 Demo README 同步；旧项目路径扫描通过；头部、Star History、License 保护检查通过 |

## 已执行验证

- 全解决方案构建通过。WPF、Demo 的 CUDA 构建也单独通过。
- 最终 .NET 测试：**691 项，通过 691，失败 0，跳过 0**。
- 新增原生微型 ONNX 验证 Float32、Float16、UInt8 输出、输入类型和长度校验、重复释放及释放后拒绝执行；不下载业务模型。
- 缓存设备切换测试先在旧缓存策略下失败，再在新实现下通过；真实原生会话证明同设备复用、变更设备重建，以及删除时等待旧/新设备租约。
- 硬件配置测试覆盖默认值、保存后重载、未知硬件类型、负 GPU 编号、损坏文件、写入失败、普通用户越权调用、CPU 路径不调用 CUDA 安装器、快照不随全局配置变化，以及 SAM CPU 与全站 CUDA 配置独立。
- 浏览器验证管理员保存 CPU、刷新恢复、重新启动保留配置、桌面/手机布局、GPU 列表及选择、普通用户控件只读、暗色主题、英文硬件设置和 YOLO/Anomalib/联合验证页面导航。本机 GTX 1060 的 CUDA 13 不支持错误得到明确报告，失败后原 CPU 配置保留。
- 浏览器自检补齐了 GPU 检测中的加载状态，并修正设备单选项的 ARIA 字符串值；最终不保留临时诊断日志。
- 两个 Windows API 发布包均实际启动并成功返回 Swagger 文档，接口路由存在。
- Release YAML 解析通过：8 个有效安装包组合、3 个有效 Docker 镜像定义；全部项目引用路径有效。
- 五个 Dockerfile 的指令、COPY 路径、入口程序集和旧项目移除检查通过。
- `git diff --check` 通过；检查范围内没有遗留旧提供程序项目引用或旧 Tasks 宿主名。

## 真实发布及原生资产核验

以下 8 个组合均执行 `dotnet publish` 成功，并将发布目录中的原生 ONNX Runtime 文件与 NuGet 缓存原文件做 SHA-256 比较，同时核对 CUDA 提供程序文件是否符合产品类型：

| 产品 | RID | 原生包 / CUDA 文件 |
| --- | --- | --- |
| Tasks | win-x64 | GPU 包，包含 CUDA 文件；默认仍选择 CPU 执行 |
| Tasks | linux-x64 | GPU 包，包含 CUDA 文件；默认仍选择 CPU 执行 |
| Tasks | linux-arm64 | CPU 包，不包含 CUDA 文件 |
| API CPU | win-x64 | CPU 包，不包含 CUDA 文件 |
| API CPU | linux-x64 | CPU 包，不包含 CUDA 文件 |
| API CPU | linux-arm64 | CPU 包，不包含 CUDA 文件 |
| API CUDA | win-x64 | GPU 包，包含 CUDA 文件 |
| API CUDA | linux-x64 | GPU 包，包含 CUDA 文件 |

## 验证边界与既有告警

- 本机为 GTX 1060，已验证 CPU 原生推理及旧 GPU 的明确拒绝路径；**未进行 RTX 30/40/50 的 CUDA 模型实机测试**。
- Linux/ARM64 执行的是交叉发布和原生资产核验，未在对应硬件启动程序。
- 本机没有 Docker CLI，Docker 检查为源文件、构建路径、RID、矩阵、入口和权限契约检查，**未实际构建或运行容器**。
- 未触发 GitHub 正式 Release，也未向镜像仓库发布镜像；发布流程按本地验证后的矩阵更新。
- 构建仍有既存 `System.Security.Cryptography.Pkcs 9.0.18` 的 NU1902/NU1903、裁剪分析及部分 xUnit CancellationToken 告警，本次没有顺带升级无关依赖；构建成功不表示无告警。
