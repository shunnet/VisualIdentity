# YOLO 核心迁入 Server 与设置页页头自检记录

## 范围与结果

- 将独立 YoloDotNet 的 108 个运行时代码文件迁入 `Snet.Yolo.Server/yolo`，保留原目录分层。
- 命名空间统一为 `Snet.Yolo.Server.yolo.*`，同步 Server、API、Tasks、WPF、联合验证 Demo 和测试的 using、类型限定名及反射字符串。
- 删除独立项目、主解决方案中的项目配置及嵌套关系；四份 Dockerfile 移除旧项目 COPY。
- SkiaSharp 4.154.0 和裁剪分析配置随核心迁入 Server；原生 ONNX Runtime 的平台选包规则不变。
- 原 GlobalUsings 转为按需文件 using，避免将 YOLO 的导入传播到 Server 其他模块；删除仅供旧独立测试程序集使用的 AssemblyInfo，沿用 Server 现有测试可见性配置。
- 设置页页头沿用 YOLO 项目页的结构、标题、图标和小尺寸主按钮；保存逻辑及设置内容不变。
- 中英文 README 同步架构、调用链和集成示例；头部、Star History、License 及上游致谢保持原样。

## 迁移完整性

迁移前保留全部源文件字节快照，逐文件核对：

1. 108 个文件全部迁入，未遗漏任务模块、绘图、跟踪或视频代码。
2. 映射命名空间、排除 using 与注释空白后，Roslyn 声明和执行代码 token 全部一致。
3. 文件 using 通过语义分析清理，未使用的导入不保留。
4. 除命名空间及导入外，原始文件字节、编码、注释和 SPDX 行保持一致。
5. 活动代码没有旧命名空间、旧反射类型根；项目、解决方案和 Docker 不再引用旧 csproj。
6. 全新发布目录不包含 `YoloDotNet.dll`，对应 deps.json 不包含独立 YoloDotNet 依赖。

`YoloDotNetException` 等既有类型名称、上游链接及版权标记保留，其命名空间已更新。

## 构建与回归

| 检查 | 结果 |
| --- | --- |
| `VisualIdentity.sln` 全解决方案构建 | 通过，0 错误 |
| 新增程序集归属与旧程序集依赖回归 | 迁移前 2 项失败，迁移后 2 项通过 |
| 全量 xUnit | 711 项通过，0 失败、0 跳过 |
| WPF Release、UseCuda=true | 构建通过 |
| Demo Release、UseCuda=true | 构建通过 |
| API、Tasks Windows x64 | 全新目录发布通过 |
| API Linux x64、Tasks Linux ARM64 | 交叉发布通过 |
| 四份 Dockerfile COPY 源路径 | 静态核验通过 |
| 中英文 README 与受保护章节 | 核验通过 |

首轮全量测试有一项预览并发测试在 finally 删除临时 BMP 时遇到 Windows 文件占用。没有修改测试或生产逻辑；相关 16 项单独复测通过，随后全量默认并发复测 711 项全部通过。

## 原生推理与 HTTP 检查

启动全新 Windows API 发布包，使用真实 ONNX Runtime CPU 提供程序和最小有效 ONNX 测试模型完成：

- 默认 GPU 配置读取、切换 CPU、非法 GPU 编号校验、不可用 GPU 设置失败时保留原配置、Swagger 文档生成。
- YOLO 上传、查询、PUT 更新、推理、绘图、原图及标注图 URL 访问、历史详情和 DELETE 删除。
- Anomalib ZIP 导入、推理、热力图、会话缓存重建及删除。

上述检查验证了程序集迁移后的加载及端到端调用，没有对真实业务数据集精度作新结论。

## 浏览器检查

Chrome 实测对比设置页与 YOLO 项目页，在明暗两种主题、320 / 390 / 1650 像素宽度下，以下计算样式一致，页面无横向溢出：

- 标题字体、字号、字重与字间距。
- 图标尺寸、字号、颜色、背景、边框与圆角。
- 按钮字体、字号、高度、内边距、圆角、背景、文字颜色与边框。

1650 像素宽度下，标题 26.4px，图标 34×34px，按钮文字 14px、高度 34.5px。另验证硬件设置的 CPU 保存与刷新、GPU 展开、错误清理、键盘操作、英文和窄屏布局，无浏览器脚本异常。

## 边界与已有告警

- 本机为 GTX 1060，不支持当前 CUDA 13 推理架构；GPU 分支仅完成构建、选包及失败回退检查，未声称完成 RTX 30 / 40 / 50 系列实机推理。
- Linux x64 / ARM64 为交叉发布，未在目标设备运行；本机无 Docker CLI，未构建或运行容器，也未触发 GitHub Release。
- 构建仍有既有 Pkcs NU1902 / NU1903、xUnit 分析告警。裁剪分析随程序集合并后覆盖 Server，暴露原有 JSON / 反射的 IL2026 / IL2075 告警；未启用裁剪发布或压制这些告警。
- 旧项目源码和解决方案项已删除。本地旧目录仅剩忽略的构建缓存；自动审批拒绝了递归清理缓存的操作，故保留本地缓存。它们不进入 Git 提交及全新发布包。
