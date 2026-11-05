# ONNX Runtime 执行提供程序与 Tasks 宿主合并计划

## 目标与边界

1. 将两个 YOLO 执行提供程序及 TensorRT 配置移入 `Snet.Yolo.Server/inference`，命名空间统一为 `Snet.Yolo.Server.inference`，删除两个独立执行提供程序项目。
2. 合并为 `Snet.Yolo.Tasks/Snet.Yolo.Tasks.csproj`，程序集、启动配置、测试宿主及发布产物同步去掉 `.Cpu/.Cuda` 后缀。沿用 Tasks.Core、Tasks.Shared、Server 的现有职责，不把业务推理搬进页面。
3. API 保留 `Snet.Yolo.Api.Cpu` 和 `Snet.Yolo.Api.Cuda`；各宿主直接固定引用对应的一个原生包，不读取 Tasks 设置。
4. 设置页增加管理员管理的、全站持久化的 ONNX Runtime CPU/CUDA 选择及单个 GPU 选择。普通用户可查看，不能修改全站推理硬件。
5. 用户已确认：此设置控制 YOLO、Anomalib 和联合验证；SAM 仍保留标注页独立设备选择，Python 训练也保持独立。
6. 新建/迁移的类、函数、参数、返回值、属性补齐中文 XML 注释；每个类型独立文件，保留原作者的许可证与版权标记。

## 已核实的现状与关键约束

- 当前 CPU、GPU、Server Managed 已统一为 ONNX Runtime 1.31.1，但两套原生包仍有同名库；版本统一不允许把两个原生包混放到同一个应用目录。
- GPU 运行库包含默认 CPU 执行提供程序。统一 Tasks 的 x64 包仅引用 GPU 原生包，由设置决定是否追加 CUDA 提供程序；默认 CPU，不会因打开首页就下载 CUDA 环境。
- NuGet GPU 1.31.1 的平台依赖仅提供 Windows x64、Linux x64 原生库。原有 Linux ARM64 支持继续保留：同名 Tasks 项目按目标 RID 引用 CPU 包，界面禁止选择不可用的 CUDA。不能发布没有对应原生库的 ARM64 CUDA 包。
- Server 只引用 Managed 依赖；API、Tasks、WPF、Demo 等最终宿主负责且只负责选择一套原生包。WPF/Demo 原有 CPU/CUDA 构建选择继续有效，改为直接引用对应原生包。
- 现有 YOLO 验证按调用创建执行提供程序；Anomalib 按模型路径缓存会话。切换硬件必须处理 Anomalib 缓存，避免设置已变而旧会话仍运行在原设备。
- 统一硬件设置只选择执行提供程序，不在运行中替换 onnxruntime 原生库。已有推理继续使用其会话，新请求读取新配置。
- CUDA 配置显式失败时返回实际原因；不把失败的 GPU 配置伪装成成功的 CPU 配置。ONNX Runtime 对个别不支持算子的正常 CPU 分配不等于整个 CUDA 会话初始化失败。

## 实现方案

### 1. Server 执行提供程序

- 迁移 `CpuExecutionProvider`、`CudaExecutionProvider`、TensorRT 类型及 GPU 辅助方法到 Server，更新所有 using、项目引用及解决方案条目。
- 提取两种提供程序共用的输入校验、会话释放、Float16 输出缓冲池为独立类型，避免继续维护两套相同的内存所有权逻辑；保持 `IExecutionProvider` 对外契约不变。
- 保留 session 锁、取消语义、输出类型校验、原生结果所有权与缓冲区复用。初始化失败释放已分配资源；Dispose 幂等，不能在 Run 使用会话时释放它。
- Server 开启必要的 unsafe 编译，Managed 版本固定为 1.31.1；不把原生包传递强加给 API CPU 宿主。

验收：旧独立提供程序项目无引用；CPU 原生小模型能运行；Float32/Float16/UInt8、错误输入及并发释放回归测试通过；API 两种发布目录各只包含目标原生运行库。

### 2. Tasks 合并与运行设置

- 保留一个 Web 宿主 `Snet.Yolo.Tasks`，继续导入现有 Shared 源码；统一启动配置、程序集身份及静态资源清单。
- 新增独立的不可变硬件配置类型和原子文件存储，位置为现有持久化卷内 `wwwroot/data/settings/onnx-runtime.json`。默认 CPU；校验枚举值、GPU 编号和损坏配置，写入失败保留旧快照。
- 合并 Tasks 的执行提供程序工厂和 Anomalib/SAM 会话选项工厂，使用同一硬件配置快照。CUDA 环境准备复用现有 installer，按选中 GPU 校验驱动、架构和运行库。
- 保存 CUDA 设置前验证平台能力与所选设备；失败不保存。硬件选择不会重装 Python 训练环境，也不会影响训练设备选择。
- Anomalib 工厂提供不可变配置快照及缓存标识；每次获取会话时核对标识，变化后退役旧条目，已有租约结束后释放，新请求创建对应新硬件的会话。禁止持锁等待推理完成。
- SAM 的 CPU/指定 GPU 路径独立于全站验证设置；统一 x64 宿主保留 GPU 能力，ARM64 明确拒绝 GPU 请求。

验收：保存/重启后设置保留；未授权用户不能写入；CPU 不探测或安装 CUDA；GPU 失败不覆盖旧配置；切换硬件不复用旧 Anomalib 会话、不打断已有租约；SAM 与训练选择保持独立。

### 3. 设置页

- 沿用现有卡片、图标及设备选择样式，增加“ONNX Runtime 运算硬件”卡片。硬件使用独立的“应用运算硬件”按钮；顶部按钮仍保存外观，避免 GPU 环境验证失败时混淆两种设置的保存结果。
- 提供 CPU / NVIDIA GPU 选项，展示检测到的 GPU 名称与显存，并支持单选具体编号及重新检测。
- 展示当前已保存的运算硬件与准备状态；普通用户只读。保存时禁止重复提交，给出 CUDA 环境准备进度和明确失败原因。
- 中英文资源同步；保持设置页布局、移动端显示、登录品牌设置和原有语言/主题功能。

验收：浏览器验证管理员/普通用户、CPU/GPU 选择、失败提示、刷新保持，以及桌面/手机布局；不在本机旧 GPU 上声称完成 RTX 30/40/50 实测。

### 4. 所有宿主与命名空间

- API CPU/CUDA 控制器改用 Server 提供程序，Anomalib 固定工厂行为保持所属 API 硬件版本；Swagger/接口注释及接口行为回归。
- WPF、Demo 改用 Server 命名空间，保留既有 `UseCuda` 构建开关与项目结构，直接声明 CPU/GPU 原生包二选一。
- 更新测试中的 Tasks 宿主程序集、WebApplicationFactory、静态资源定位、解决方案及硬件契约测试；避免只改编译引用而漏掉运行时程序集名。
- 删除的旧项目只删除已纳入版本控制的源码，不清理用户的模型、训练环境、数据库和测试产物。升级部署时按 README 指引保持原有持久化数据目录。

验收：全解决方案构建；Tasks、两种 API、WPF、Demo 的 CPU/CUDA 入口编译与适当发布检查；扫描无遗漏的旧项目名/命名空间。

### 5. 发布与 Docker

- GitHub Release 的 Tasks 矩阵改为单个产品名，发布 `win-x64`、`linux-x64`、`linux-arm64`；API 仍保留 CPU/CUDA 各自支持的 RID。
- Tasks Docker 合并为一个 Linux Dockerfile/镜像名，x64 带 GPU 包、ARM64 带 CPU 包，默认均在 CPU 模式启动。GPU 部署仍需 NVIDIA Container Toolkit、`--gpus` 和兼容驱动；运行库继续可通过 Tasks 的私有 installer 准备。
- 删除旧 Tasks CPU/CUDA Dockerfile，Windows Tasks Dockerfile 默认指向统一项目；两种 API Dockerfile 保留并移除已删除项目的 COPY/引用。
- 同步 Release 的版本读取、归档名、artifact 下载、镜像矩阵、生成说明和启动 DLL；每个发布目标检查实际 native assets，而非仅根据文件名判断硬件支持。
- 持久化卷与非 root 目录权限保留；硬件配置随现有 data 卷持久化。

验收：YAML 解析与矩阵检查、Docker COPY/ENTRYPOINT 路径校验、支持 RID 的真实 dotnet publish 检查；没有 Docker 的本机只报告静态检查，不冒称容器启动成功。

### 6. 文档同步

- 同步 README.md、README.en.md、Demo README 的项目结构、引用示例、安装包名、运行方式、设置权限、硬件生效边界、ARM64 限制、Docker 命令及发布矩阵。
- 将提供程序的必要部署说明迁入现有主 README 的相关章节，删除失效的独立项目链接；保留头部、Star History 与 License。
- 明确“Tasks 设置选择 CPU/CUDA，API 部署选择 CPU/CUDA；SAM/训练独立”，避免把 Python CUDA 通道与 .NET CUDA 13 混为一谈。

验收：中英文语义一致，旧项目路径扫描、链接/锚点及保护章节检查通过。

## 按顺序实施及自测

1. 建立版本控制基线、确认兼容性和改造清单（本文件）。
2. 先增加硬件配置存储与切换缓存的行为测试，验证旧实现缺失的行为；执行提供程序迁移后运行已有原生推理回归。
3. 合并 Tasks 项目与全部宿主引用，分目标修正编译失败，验证不会引入 CPU/GPU 原生依赖冲突。
4. 接入配置工厂、缓存退役与设置页；完成存储失败、非法配置、权限、设备不可用及切换期间租约安全测试。
5. 修改发布、Docker 与文档；执行真实发布检查、矩阵/路径/保护章节扫描。
6. 浏览器完成设置保存/刷新、普通用户只读、现有验证/标注/训练页面的针对性回归。
7. 最终全套 .NET 测试、适用浏览器测试、全解决方案构建、`git diff --check`，逐项对照本计划自检，记录实测与未实测边界。
8. 完成后提交推送，核对远端 HEAD 与本地一致，报告工作区状态。不会触发正式 GitHub Release 或向外发布 Docker 镜像。

## 最终自检重点

- 同一应用目录只能有一种原生 ONNX Runtime 包；Server 新类型不破坏 CPU API 的纯 CPU 依赖图。
- 新设置只由管理员保存，异常/失败写入不改变运行配置，不通过浏览器本地偏好决定服务器设备。
- 缓存切换不提前 Dispose 活跃会话、不泄漏退役会话，配置读取与会话创建使用同一快照。
- 选择的 GPU 编号真正用于环境检查和会话创建，不能仍硬编码 GPU 0。
- 新增及迁移类型各自独立文件，函数/参数/属性中文注释齐全，保留许可证。
- 三种 Tasks 发布 RID、两种 API、WPF、Demo、工作流、Docker、README 均没有遗留的旧提供程序项目引用。
