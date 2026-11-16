# 上传 0% 停滞与公共传输规则检查

## 问题与修复

24.54 GB ZIP 选择后，旧客户端先将整个 File 写入 IndexedDB，再发送第一个 HTTP 分块。大文件本地复制可能长时间没有结束，页内却显示“上传 0 KB / …、0%”。新增浏览器回归在旧代码上未收到首块而失败，修改后已收到首个真实分块。

上传统一经过 `UploadInputs → UploadCenter → transfers.js → PersistentUploadService`：

- 支持的安全上下文优先保存文件句柄，所有五种输入槽位共用此机制。
- HTTP 每批最多缓存 64 MiB File，超预算文件只保存元数据，直接按 4 MiB 发送。不会先复制二十多 GB 的原 ZIP。
- 应用内导航继续读取原 File。完整刷新后，大文件显示“待选原文件”，停止旋转；点击“重新选择原文件”后校验名称、大小、修改时间、文件开头及已确认断点前最多各 64 KiB，再从权威偏移续传。误选被拒绝，偏移不推进。
- 缓存准备独立显示“准备中”，不用上传 0% 代替。缓存事务超过 30 秒明确失败。页内、顶部和传输提示取消均可中断浏览器准备及服务端任务；浏览器电路断开仍执行服务端取消。
- 图片数量、文件类型、所有权、防伪、128 GiB ZIP 总量与现有单图/模型准入限制沿用原规则。来源恢复策略与业务准入分开，不给 LOGO 等小文件套用 ZIP 大小上限。

下载统一经过 `transfers.js → PersistentDownloadService`。生成阶段查询实际落盘字节，只报告“准备/已生成”，不猜测压缩包最终大小或伪造网络百分比。完成后交给浏览器下载管理器，通过稳定鉴权地址和 Range/If-Range/ETag 续传。生成阶段取消后删除浏览器任务提示，不再误显示为“下载暂停”。

## 入口全量核对

| 入口 | 公共链路 |
| --- | --- |
| YOLO 工程图片、分类图片、Anomalib 正常图片 | 常驻 images 槽位与公共上传意图 |
| YOLO/Anomalib 验证图片与视频、联合验证图片 | images 槽位，任务固定用户及模型目标 |
| YOLO 数据集 ZIP | archive 槽位，128 GiB 准入、4 MiB 分块 |
| YOLO ONNX、Anomalib 模型包、品牌 LOGO | onnx/package/logo 槽位，同一来源准备策略 |
| 各数据集导出、YOLO 模型下载、Anomalib 验证/训练包与报告 | 七类下载路由统一调用 `PersistentDownloadService.DownloadAsync` |
| best.pt、训练历史模型/报告 | 同一后台下载服务；训练指标页临时 download 链接也被公共客户端捕获 |

图片预览资源使用普通鉴权图片 GET，不属于文件导出任务。训练环境、预训练权重、SAM/FFmpeg 等服务端安装下载属于后台安装器，不将这些服务器拉取行为改成浏览器文件上传队列。

## 回归与边界

执行命令：

```powershell
node --test Snet.Yolo.Test/Browser/*.test.mjs
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-restore --artifacts-path D:/vri16/Snet.Yolo.Test/bin/i
git diff --check
```

新增或扩展覆盖：

- 声明 24 GiB 来源时，禁止 IndexedDB 保存完整 File，首块仍立即发出。
- 大文件刷新后等待重选，误选同名不同内容被拒绝，正确来源从断点继续，offset=0 不重复。
- 来源事务卡住超时及准备阶段取消，无任何网络分块发出。
- images/archive/onnx/package/logo 五种输入均保存实际文件句柄，并在刷新后恢复，不检查完整文件缓存配额。
- 小文件真实 IndexedDB 缓存、切页/刷新、断网重试机制、用户隔离、取消终态沿用原有回归。
- 下载已生成字节展示、取消、刷新复用同一产物、真实 HTTP Range/If-Range、416 与内容完整性。
- 来源阶段接口与断点校验要求认证所有者、防伪和有限请求体；取消可打断正在等待读取的校验流。
- 页内取消会通知浏览器；模拟电路断开时服务端仍取消。

本地真实 Tasks 页面验收使用隔离测试账号及合成 ZIP 来源，不调用推理或训练。页内已显示 `上传文件 4 MB / 256 MB，1%`；导航到设置后确认偏移又增加 4 MiB；完整刷新后显示 `已上传 8 MB / 256 MB、待选原文件`，无旋转图标且取消按钮可用，浏览器无 pageerror。

声明大小测试未实际传输 24 GiB；真实 Tasks 验收使用 256 MiB 声明与小型合成来源，仅核对首块、导航、刷新及取消。没有完整上传生产二十多 GB ZIP，也没有修改生产监听、证书或部署。旧版已经缓存的大 File 不在本次启动时自动删除；已有任务需完成或取消，随后新建上传才使用新准备规则。

最终结果：

| 检查 | 结果 |
| --- | --- |
| .NET 全套，最终源码重新编译后执行 | 1065 通过，0 失败，0 跳过 |
| 浏览器全套，真实 Chromium / IndexedDB / File / 文件句柄 | 23 通过，0 失败 |
| 真实 Tasks 页面，最终版本重新启动后执行 | 字节/百分比、应用内导航继续、刷新等待、无旋转图标、页内取消同步清除重选提示均通过，0 pageerror |
| `git diff --check` | 通过 |

中间第一次全套为 1062 通过、1 失败：未修改的 `SamLargeImageTests.DecodeImage_HundredMegapixelJpegUsesBoundedWorkingBitmap` 在清理临时 JPEG 时发生 Windows 文件占用。随后补充页内取消回归并重新编译，最终全套 1065 项全部通过；没有修改或跳过该 SAM 测试。构建仍有既存分析器警告，不宣称零警告。

运行日志和合成来源截图在被忽略的 `Snet.Yolo.Test/bin/`：`upload-source-full-final.log`、`upload-source-all-browser-final.log`、`upload-source-live-final.log`，以及 `transfer-browser-fixtures/large-source-uploading.png`、`large-source-waiting.png`、`large-source-live-result.json`。这些是本机验证产物，不作为生产数据提交。
