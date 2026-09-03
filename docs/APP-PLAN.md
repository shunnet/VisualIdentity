# Snet.VisualIdentity 前端应用开发规划

> 目标：在现有标注/训练 Web 应用 (Snet.Yolo.Tasks) 基础上，扩展为一个现代、美观的多页 Web 控制台——含左侧可折叠导航、登录/用户管理，以及 首页(监控看板)/项目/验证/用户 四个页面。复用 Snet.Yolo.Server 底层与 Snet.Yolo.Api.* 接口契约。

---

## 0. 目标与范围

| 页 | 内容 |
|---|---|
| 登录 | 登录页（用户名/密码），未登录跳登录；登录后进入主页 |
| 首页 | 监控看板：系统资源(CPU/内存/GPU)、项目统计、训练进度、模型数、识别活动；多折线图/柱状图 |
| 项目 | 复用现有项目页（列表/详情/标注/训练），唯一改动：把标注页顶部导航栏功能下移到底部工具栏 |
| 验证 | 模型管理(增删改查、上传 .onnx、选类型) + 推理控制台(选模型/上传图/参数→检测/分割/姿态/OBB/分类→结果+标注图+详情)；兼容调用 Snet.Yolo.Api.* 或直接引用 Snet.Yolo.Server |
| 用户 | 用户管理(增删改查/角色) + 登录认证 |

界面：现代化、好看、明暗/中英；左侧导航可折叠展开；未存在的页面布局与现有项目页一致。

---

## 1. 现有解决方案（F:/Snet/VisualIdentity）

- **Snet.Yolo.Tasks**：现有标注/训练 Web 应用（Blazor Server + Bootstrap 5.3.8 + SQLite）。含 Home/ProjectDetails/Editor/TrainPage。
- **Snet.Yolo.Tasks.Core**：标注/训练领域逻辑。
- **Snet.Yolo.Server**：YOLO 推理底层库。ManageOperate(Add/Update/Delete/Query 模型)、IdentityOperate(RunAsync(Classification/ObjectDetection/Segmentation/PoseEstimation/ObbDetection))、OnnxType(ObjectDetection/Segmentation/Classification/PoseEstimation/ObbDetection)、models(OnnxData/IdentityData/各种Data/ResultData/OperateResult)、handlers(Pose/Result/Public)。
- **Snet.Yolo.Api.Cuda / Cpu / CoreML / DirectML / OpenVino**：5 个可独立部署的推理 API，均继承 OperateBaseController，Tag 不同(执行提供器)。端点：
  - POST AddAsync / UpdateAsync / DeleteAsync（模型管理）
  - GET QueryAsync / QueryAllAsync（模型列表）
  - POST IdentityAsync / IdentityDrawCoreAsync（识别 / 识别并绘制标注图）
  - GET GetOriginalImage / GetMarkImage / GetImageDetails
  - 路由 [controller]/[action]（如 /Operate/QueryAll）
- **Snet.Yolo.Api.Shared**：OperateBaseController、ConfigModel(BasePath/命名格式/RetentionDays)、Handler(Image/HistoryFile)、AllowedFileTypeAttribute。
- **Snet.Yolo.Tool**：WPF 桌面工具（YoloDetect/Classify/OBB/Pose/Segment 视图 + 数据统一），作为验证页 UI 的参考。
- **Snet.Yolo.Test**：控制台测试。Snet.Py：Python 相关。

---

## 2. 整体架构（扩展 Snet.Yolo.Tasks）

    登录页 (Login.razor)
       -> 认证成功 -> 主壳 (AppShellLayout)
            +  左侧可折叠导航：首页 / 项目 / 验证 / 用户
            +  内容区(与现有项目页一致)
                 + Dashboard.razor           (首页)
                 + Projects = 现有 Home/ProjectDetails/Editor 复用
                 + Validation.razor           (验证：模型管理+推理控制台)
                 + Users.razor                (用户管理)

- 扩展点：新增 AppShellLayout(左导航) 作为主布局；保留 MainLayout(项目页用)。新增登录、Dashboard、Validation、Users 页面；现有 Home 改挂到"项目"导航。
- 认证：新增 UserStore(EF Core 用户表) + AuthService(登录/哈希校验) + 登录页 + 路由守卫。用户存 SQLite(data/snet.db)。
- 引用：Snet.Yolo.Tasks 增加对 Snet.Yolo.Server 的引用（验证页直接调用 ManageOperate/IdentityOperate）。

---

## 3. 登录与用户管理

- 用户表(User)：Id、Username、PasswordHash、Role(Admin/User)、CreatedAt、Active。
- 登录页：username + password -> 校验 -> 写认证 Cookie；未登录跳 /login。
- 用户页：增删改查用户、设角色、重置密码。首个用户默认 Admin。
- 密码用 PasswordHasher(PBKDF2)。明暗/中英与现有一致。

---

## 4. 首页（监控看板）

统一"监控"呈现，尽量多图表：
- 系统资源：CPU%、内存%、GPU%、显存(已用/总量) —— 采用 SystemMetrics(已存在)，每 1s 采样，折线图滚动展示近 60 点。
- 项目统计：项目总数、任务总数、类别数 —— 柱状图(每项目任务/类别)。
- 训练进度：进行中训练任务、完成/失败数 —— 卡片 + 进度条。
- 模型/验证：模型总数(ManageOperate.QueryAll)、按 OnnxType 分布 —— 柱状图。
- 识别活动：最近推理记录(时间/类型/耗时) —— 列表/折线。

图表库：建议 Chart.js（本地引入，离线可用）或纯 CSS/SVG 自绘轻量图表（无外部依赖）。需确认。

---

## 5. 项目页（复用，仅编辑器工具栏下移）

- 项目页(列表/详情/标注/训练) 原样复用，不改造逻辑。
- 唯一改动：把标注页 Editor.razor 顶部 <header class="ls-topbar"> 的导航功能（撤销/重做、缩小/放大/适应/实际尺寸 等）移到页内底部工具栏（ls-bottombar），如图。顶部保留返回/工程名。
- 其余页面布局与项目页一致（同一壳/顶栏）。

---

## 6. 验证页（模型管理 + 推理控制台）—— 重点设计

### 6.0 后端模式选择（两个单选）
- **本机运行**：进程内直接引用 Snet.Yolo.Server（默认），选 Cuda(GPU) 执行提供器，ManageOperate/IdentityOperate 直接调用，离线可用。
- **API运行**：勾选后**弹窗让用户填写 API 的 IP/端口**（如 http://192.168.x.x:5000），保存为配置；之后所有验证操作走该 Snet.Yolo.Api.*（HTTP），HttpClient 调 /Operate/QueryAll、/Operate/IdentityAsync 等，支持远程独立部署。
- 模式切换 + API 地址持久化(localStorage 或项目配置)。切到 API运行 前校验 URL 可达。

### 6.1 模型管理
- 模型表格：索引/名称/类型(OnnxType)/描述/操作(更新/删除/查询)。
- 上传 .onnx + 选类型 + 描述 -> 添加。本机运行走 ManageOperate；API运行走 AddAsync。

### 6.2 推理控制台（对照 Snet.Yolo.Tool 的 5 个视图）
    状态栏：[本机运行 (Cuda)] / [API运行  http://ip:port]  (两个单选)
    模型区：选模型(按 OnnxType 过滤) + 参数(Confidence/Iou/PixelConfedence/gpuid)
    上传区：上传图片 -> 选任务(检测/分割/分类/姿态/OBB) -> 识别
    结果区：原始图 + 标注图(GetMarkImage) + 坐标/类别/置信度/KeyPoint + 详情(GetImageDetails)
    按 OnnxType 分 Tab：检测/分割/分类/姿态/OBB

### 6.3 双后端实现
- 统一接口 IInferenceClient：LocalInference(进程内 Server) / RemoteInference(HttpClient Api)。
- 页面只依赖 IInferenceClient，ResolveClient(mode) 按单选切换。
- 进程内用选的 provider(默认 Cuda)；远程用填的 URL。

## 7. 技术要点

- 图表：Chart.js（建议）或自绘 SVG。
- 导航折叠：左栏宽 240px，折叠 64px(仅图标+tooltip)，CSS transition；AppShellLayout 持久化折叠态(localStorage)。
- 明暗中英：沿用现有 ls.css 令牌 + AppResource；新增页全部加资源键。
- 认证：Cookie 认证 + 路由守卫(未登录跳 login)。
- 验证页离线可用：优先进程内 Server（无需部署 Api）。

---

## 8. 里程碑

| M | 内容 | 验收 |
|---|---|---|
| M1 登录+用户 | UserStore/AuthService/登录页/用户页/路由守卫 | 登录->主壳；用户增删改查 |
| M2 主壳+左导航 | AppShellLayout(可折叠导航) + 路由(首页/项目/验证/用户) | 导航折叠展开，四页可切换 |
| M3 首页看板 | SystemMetrics 采样 + 折线/柱状图 + 项目/训练/模型统计 | 图表实时刷新 |
| M4 验证页(进程内) | 模型管理 + 推理控制台(5 任务) 对接 Server | 上传模型/图片->识别->结果/标注图/详情 |
| M5 验证页(Api 调用) | HttpClient 调 Api.*(可配基址/Tag) | 可选远程部署 Api 全接口 |
| M6 编辑器工具栏下移 | Editor 顶部工具按钮->底部工具栏 | 截图对照 |

---

## 9. 已确认决策（你已拍板）
1. **验证页后端**：**两个单选**——本机运行(进程内 Server，默认) / API运行(弹窗填 API IP:端口，之后走该 API 验证)。
2. **进程内推理**：Cuda(GPU) 执行提供器。
3. **登录/用户**：本地 SQLite 用户表，首个 Admin 种子，可增删改查 + 角色(Admin/User)。
4. **图表**：Chart.js(本地引入，离线)。
5. **编辑器工具栏下移**：撤销/重做 + 缩小/放大/适应/实际尺寸 下移到底部工具栏。

---
*规划已按上述决策定稿；确认后进入开发（M1 登录+用户 -> M2 主壳+左导航 -> M3 首页看板 -> M4 验证页(本机) -> M5 验证页(API) -> M6 工具栏下移）。*

---

## 10. 补充需求（第3/4/5点，已纳入）

### 第3点：项目 数据层迁到 Snet.Yolo.Server
- **Snet.Yolo.Server 作为统一数据/业务后端**：项目(工程/任务/标注)的数据库操作全部并入 Server，按 Server 的 `CoreUnify` + `DBData(SQLite)` 习惯开发，文件夹**小写**（如 `model`/`operate`/`interface`）。
- 新增 `ProjectData`(模型) + `ProjectOperate`(增删改查) 于 Server；`ManageOperate`(模型) 已在 Server。
- **移除** Snet.Yolo.Tasks(.Core) 里的 `WorkspaceDbContext`/`WorkspaceStore`/`IWorkspaceStore`/`WorkspaceDocument`，web 端改为调用 `ProjectOperate`。
- 现有 SQLite 数据迁移到 Server 的存储（`DBData` SQLite）。

### 第4点：验证页 布局与行为
- **左右排布**：左侧 = 模型列表(支持模型管理)；右侧 = 上方功能区、中间 = `左原图 | 右识别图`、下方 = 日志。
- **点模型自动识别 YOLO 类型** → 右侧动态显示该类型对应的**模型入参**（如 检测/分割→Confidence、Iou；分割→PixelConfedence；姿态→KeyPoint 参数；分类→Classes；OBB→Confidence/Iou）。
- 全部功能用 Snet.Yolo.Server 的（含模型/项目/用户数据库操作）。

### 第5点：用户管理 在 Snet.Yolo.Server
- 在 Server 新增 `UserData`(用户表) + `UserOperate`，完全按 Server 开发习惯（小写文件夹）。
- 登录/用户页 调用 `UserOperate`（增删改查、角色、密码哈希）。

### 统一原则
- **单一后端**：所有数据(项目/标注/模型/用户/验证)仅经 Snet.Yolo.Server；web 端无独立 DB。
- 开发习惯：与 `Snet.Yolo.Server` 一致（`CoreUnify` 基类、`DBData SQLite`、小写文件夹、`OperateResult` 返回）。

### 最终决策（已确认）
- **项目/标注数据建模**：**B 规范化多表**——在 Snet.Yolo.Server 拆分 `Project/Task/Annotation/Result` 表，严谨、合理、可追溯。
- **旧数据**：现有 `data/snet.db``(EF Core)` **不再需要，不迁移**。
- **代码质量**：高；驼峰命名、注释风格与 Snet.Yolo.Server 完全一致。
- **文件格式/生成**：数据文件、命名等全部按 Snet.Yolo.Server 的约定（`DBData SQLite`、小写文件夹、`CoreUnify`）。
- **开发完成后**：进行**详细审查**，逐按钮/图表/功能块验证正常。
- **里程碑顺序**：M1 登录+用户 → M2 主壳+左导航 → M3 首页看板 → M4 验证页(本机/API) → M5 项目数据迁 Server(规范化多表) → M6 编辑器工具栏下移 → M7 全面审查。
