# API 宿主及 ONNX Runtime 硬件统一改造自检

日期：2026-11-05。接续 Tasks 合并后的 API 改造；此前计划中“API 保留两个固定硬件宿主”的要求由本次用户指令替代。

## 实现结果

- API 合并为 `Snet.Yolo.Api`，命名空间仍为 `Snet.Yolo.Api`；删除 CPU/CUDA 两个旧宿主及重复控制器、工厂。开发端口统一为 HTTP 5157 / HTTPS 7257。
- 全新部署默认 `Cuda` / GPU 0；未初始化 CUDA 也能启动、查询配置及切换 CPU。有效持久配置优先于 `appsettings.json` 和环境变量中的部署默认值。
- 公共接口 `GET /api/runtime/hardware` 查询配置，`PUT /api/runtime/hardware` 接收必填 `Hardware`（`Cpu`/`Cuda`）及可选非负 `GpuId`。沿用现有匿名 API 和限流方式，作用于整个 API 实例的 YOLO 与 Anomalib。
- API 与 Tasks 复用 Server 中的不可变配置、严格校验和原子文件存储；API 保存到 `wwwroot/settings/onnx-runtime.json`，Tasks 保持自己的默认 CPU 和原存储路径。两个进程的设置独立。
- x64 宿主只引用 GPU 1.31.1 原生包，同时提供 CPU/CUDA 执行路径；ARM64 使用 CPU 原生包。Server 保持 Managed 依赖，不叠加两套原生运行库。
- CUDA 保存前创建指定 GPU 的微型原生会话；参数、初始化、取消或文件写入失败不覆盖原配置。没有自动改用 CPU，也没有给 API 引入 Tasks 的 Python/CUDA 自动安装器。
- YOLO 一次请求固定硬件及缓存代次；切换时退役缓存，活动租约完成后释放。切换前开始但延迟加载的请求只用临时会话，不能重新填充新缓存。
- Anomalib 支持非终止性缓存清理，并在缓存锁内捕获设备，避免切换与清理期间重新缓存过时配置。SAM 和 Python 训练设备仍保持独立。
- 原 YOLO `gpuid` 表单字段由全局接口替代；CUDA 模式保留安全限制后的 TensorRT 配置，CPU 拒绝显式 TensorRT 请求。仅在真正提交对应表单字段时启用 TensorRT。
- Anomalib 模型响应改为明确 DTO，修复只读匿名属性被 JSON 配置忽略、HTTP 返回空对象的问题；模型信息不包含本地路径，导入错误仍保留 `ResourceKey`。
- API 的模型注册、硬件配置和 TensorRT 缓存统一放入持久化 `wwwroot`。README 说明旧目录需要迁移，不自动删除或搬移部署数据。
- 新增及迁移类型分别独立文件，补齐中文函数、参数和属性注释；缓存内部条目与租约也拆为独立类型。

## 验证记录

| 项目 | 结果 |
| --- | --- |
| 旧缓存回归 | 新测试先在旧实现下失败，再在非终止性缓存清理和租约保护实现下通过 |
| TensorRT 表单回归 | 真实 HTTP 的普通/绘制识别先复现误判，修复后两种请求通过 |
| Anomalib 响应回归 | 真实 HTTP 先复现模型编号缺失，响应 DTO 修复后通过 |
| 全解决方案构建 | 通过，0 错误；保留既有警告 |
| 最终 .NET 测试 | **705 / 705 通过，0 失败、0 跳过** |
| 硬件接口 | 默认 GPU 查询、CPU 保存及重载、缺少必填字段、未知枚举、负 GPU 编号、GPU 不可用时返回 503 并保留原配置 |
| 缓存及存储 | 配置快照、代次隔离、旧请求临时会话、活动租约、Anomalib 清理后继续识别、原子写入失败保留旧快照 |
| 独立发布包 HTTP | 全新部署默认 GPU；保存 CPU 后真正重启并恢复；Swagger 成功生成 |
| YOLO HTTP 全流程 | 上传、查询、原生 CPU 推理、绘制、删除通过，使用微型 ONNX 固件，无业务权重下载 |
| Anomalib HTTP 全流程 | ZIP 导入、返回模型编号、原生 CPU 识别、热图、重置缓存后继续识别、删除通过 |
| Tasks 浏览器回归 | CPU 保存/刷新、GPU 展开、错误提示切换清理、键盘、明暗主题、英文及 320–2307px 布局通过 |
| API 发布 | `win-x64`、`linux-x64`、`linux-arm64` 三种真实 `dotnet publish` 通过 |
| 原生资产 | 三个平台原生库与对应 NuGet 原文件 SHA-256 一致；x64 有 CUDA 文件，ARM64 没有；依赖图只选择一种原生包 |
| 发布配置 | 6 种 Tasks/API 包组合、2 个 Linux 多架构镜像，版本读取、归档和发布说明已同步 |
| Docker 静态检查 | 四个 Dockerfile 的 COPY 路径、入口、架构分支、持久化卷及 Windows 非 root 目录权限检查通过 |
| 文档及结构 | 中英文 README、路由、配置及升级说明同步；头部、Star History、License 未改；清除旧项目和共享导入 GUID |
| 中文注释与类型 | Roslyn 检查新增/迁移类型、函数/参数/属性注释及每文件单类型，0 遗漏 |

## 实测边界

- 本机 GTX 1060 未用于证明 RTX 30/40/50 的 CUDA 模型执行；已实测 CPU 原生推理和 GPU 不可用的失败回滚路径。
- Linux/ARM64 验证为交叉发布、依赖图及原生资产检查，未在对应硬件运行。
- 本机没有 Docker CLI；未实际构建/运行容器，也未触发正式 GitHub Release 或发布镜像。
- 构建保留既有 Pkcs 9.0.18 的 NU1902/NU1903，以及裁剪分析和部分 xUnit 警告；没有顺带升级无关依赖。
- 自检曾有一次 SAM 临时 JPEG 删除时文件被占用；单独复测和最终全套均通过，尚未确认持有文件的进程，未因此修改 SAM 生产代码。
- 迁移共享 ONNX 测试固件时，发现 Demo 旧测试通过反射依赖私有常量，已改为直接复用共享测试固件。
