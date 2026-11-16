# YOLO ZIP 边界舍入兼容与导出精度修复

## 现场证据

上传约二十多 GB ZIP 后，进度 25% 报 `labels/106.txt 第 4 行的标注框超出图片范围`。用户提供的实际第 4 行为：

```text
7 0.03027 0.34822 0.06055 0.10255
```

该框左边界为 `0.03027 - 0.06055 / 2 = -0.000005`。旧导出保留五位小数，而导入只允许 `0.000001` 的边界误差。中心、宽度分别舍入后，会把贴边的有效框推到图片外；5120 像素宽图片中，本例约为 0.0256 像素。

这是上传完成后的 ZIP 内容自检错误，不是下载连接断开。修复不需要修改图片或重新标注。

## 修复规则

- 导入和检测框导出共用 `YoloBoxNormalizer`。
- 五位小数的中心误差最多 `0.000005`，半宽误差最多 `0.0000025`，合计最多 `0.0000075`；统一采用 `0.00001` 的归一化边界容差。
- 只把容差内的微小越界贴齐图片边缘，重新计算相应中心和宽高；有效框全部保留。导入计划记录调整数量，上传日志记录汇总。
- 超出容差、非有限坐标、非法类别或非正宽高仍拒绝；真正越界显示左、上、右、下边界。文本行号包含空行，定位到实际文件行。
- YOLO 标签输出提高到九位小数，降低贴边误差并保留微小正宽度。内存 ZIP 和流式下载共用同一标签导出器。
- 导出在准备阶段验证检测框，不生成已知无法重新导入的坏框标签；错误包含任务和标注标识。
- 不修改已保存工程中的原始标注，不放宽 ZIP 路径、大小或图片内容验证。

## 回归与自检

新增 `YoloBoundaryRoundTripTests`：用户原始八行数据、四个图边界、两条 ZIP 导出链路与微小正宽度、空行后的真实错误行号、真实坏框导出拒绝，以及无效值/尺寸/类别的保护。现场原始八行数据在修复前稳定触发同一错误，证实回归命中原问题。

针对性命令：

```powershell
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-restore --filter 'FullyQualifiedName~YoloBoundaryRoundTripTests|FullyQualifiedName~YoloImportTests|FullyQualifiedName~YoloLabelExporterTests|FullyQualifiedName~DatasetExportStreamTests|FullyQualifiedName~YoloArchiveUploadTests|FullyQualifiedName~ProjectDatasetDownloadTests' --artifacts-path D:/vri16/Snet.Yolo.Test/bin/i
```

针对性验证 **64 项全部通过**，日志为忽略的 `Snet.Yolo.Test/bin/yolo-boundary-verified.log`，其中新增边界回归 18 项。

全量首次运行 1060 项，1059 项通过；唯一失败是原有 `SamLargeImageTests.DecodeImage_HundredMegapixelJpegUsesBoundedWorkingBitmap` 在清理临时 JPEG 时遇到文件占用。未修改该测试或 SAM 代码，其单独复测通过；记录分别位于 `yolo-boundary-full.log` 与 `yolo-boundary-sam-recheck.log`。随后复跑全量，**1060 项通过、0 失败、0 跳过**，结果见 `yolo-boundary-full-verified.log`。

验证使用当前工作区依赖。期间发现的并行 ONNX Runtime/SkiaSharp 项目文件升级保留原样，不纳入本次上传下载修复提交。

同一轮保留并补充了持久传输策略：HTTPS 文件句柄优先，HTTP 缓存兼容，已知配额不足在缓存和传输首块前拒绝。新增四项真实浏览器回归验证不足配额、估算 API 缺失/抛错以及真实文件句柄跨刷新恢复。全套浏览器 14 项通过，日志为 `transfer-policy-all-browser.log`。文件句柄测试使用真实 OPFS 句柄通过 IndexedDB 保存，原生文件选择器由测试替身返回该句柄；未自动操作用户浏览器的权限弹窗。

本次未传输用户二十多 GB 原包，使用原始标注文本和小型合成图片验证内容闭环；原有 50/128 GiB ZIP64 回归继续覆盖大流长度。生产服务器尚需部署新版本后重新导入原 ZIP。
