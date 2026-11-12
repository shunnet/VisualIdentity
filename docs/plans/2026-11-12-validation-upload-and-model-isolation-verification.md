# 验证页上传状态与 Anomalib 模型队列隔离

日期：2026-11-12。

## 修复内容

联合验证原先只使用 `_busy` 标记上传和推理，而底部提示把全部忙碌状态都显示成“正在识别图片”。上传直接复制文件流，也没有传输字节回调。

- 上传使用独立显示状态，在预览卡片操作按钮下方显示当前文件、实际字节百分比和文件数量。
- 以 256 KiB 缓冲区流式写入，每 100 毫秒最多刷新一次字节进度；文件处理结束前不提前显示 100%。完成或取消后横幅收起。
- 横幅右下角提供取消按钮，每批次使用独立的关联取消源。取消只停止本批上传，删除未完成文件，保留已完成图片；不取消页面后续上传或识别。
- 删除底部的忙碌提示。真正识别时仅在操作按钮旁显示识别状态。上传仍不调用推理。

Anomalib 验证原先每位用户只有一个文件列表，切换模型只清空结果，造成多个模型共用图片。

- 按用户和模型选择键分别保存快照，模型间独立保存文件、选中项、结果、日志和区域参数。
- 切换、刷新及导入后选择模型统一恢复对应快照；新模型显示空队列。修改一个模型的过滤参数不清空其他模型的结果。
- 每个模型分别执行 500 个文件的上限，快照继续深复制，避免页面对象修改缓存。
- 删除当前模型或其他模型的缓存时，不清空其他模型的队列；仍按原有行为清理对应的生成视频并保留源文件。文件 URL 和认证路由保持原状。
- 同步中英文 README 与新增取消、文件数量提示的本地化资源。

状态与 YOLO 相同，仅在应用进程内保存；页面刷新保留，应用重启后清空。本次未增加数据库迁移。

## 自动化验证

先观察失败再修复：

1. 管道流阻塞在实际上传读取期间，中文、英文 Razor 渲染均出现识别提示，两个用例失败。
2. 增加取消要求后，两个用例因渲染树中没有取消按钮失败。
3. 直接执行 Anomalib 页的模型切换，切到第二个模型后仍包含第一模型的图片，用例失败。

最终在干净的主分支副本（基于 `5ee91ac`，只叠加本次修改）执行 **66/66 项相关回归，全部通过，目标框架 net10.0**。使用隔离副本是因为原工作区已有用户的 net11.0 和依赖升级修改；这些原有修改不属于本次提交。

范围包含：

- `JointValidationInteractionTests`：实际上传控制流、双语渲染、上传不推理、取消、清理未完成文件、保留图片、再次上传、切换及删除交互。
- `AnomalibValidationModelIsolationTests`：调用实际页面方法，切换不同模型、恢复结果和选中项、刷新式重建、移除文件及模型时不影响另一模型。
- `AnomalibValidationStateTests`：按用户隔离、按模型独立限制与参数、删除状态及深复制。
- 既有 `ValidationStateTests`、`TasksHostRegressionTests`、`UploadCenterTests`、`UploadLimitTests`、本地化及联合验证相关测试。

复现命令：

```powershell
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --filter "FullyQualifiedName~AnomalibValidation|FullyQualifiedName~JointValidation|FullyQualifiedName~ValidationStateTests|FullyQualifiedName~TasksHostRegressionTests|FullyQualifiedName~UploadCenterTests|FullyQualifiedName~UploadLimitTests|FullyQualifiedName~Localization"
```

编译过程中存在既有 NuGet 与分析器警告，本次没有修改项目目标框架或依赖版本。

## 浏览器检查

使用独立构建目录中的 Tasks 实例，仅监听 `127.0.0.1:5182`，未修改或重启生产服务，也没有执行业务模型训练或推理。

联合验证已实测：

- 两个约 75 MiB 的传输测试文件，观察到 46% → 47% 的实时进度；横幅位于预览按钮下方。
- 中文上传状态与英文 `Uploading and checking the file…` 均显示当前文件、百分比和取消按钮。
- 上传期间没有底部识别提示，也没有按钮旁的识别提示；上传完成后横幅消失。
- 点击取消后日志明确显示上传取消，上传按钮恢复可用；取消后再次上传两张文件成功。
- 再次开始并取消上传，原有两张图片仍在列表中，未完成文件未加入队列。

截图保存在本地忽略目录中，未纳入 Git：

- `Snet.Yolo.Test/bin/joint-upload-progress.jpg`
- `Snet.Yolo.Test/bin/joint-upload-english.jpg`
- `Snet.Yolo.Test/bin/joint-upload-cancelled.jpg`

Anomalib 浏览器实测仅完成两套受控界面模型的显示及选择模型 A 后的空队列检查。随后用户按下 Esc 停止计算机操作，未继续执行模型 A/B 的上传、切换和刷新浏览器流程。这些行为已由前述实际页面组件回归覆盖，不将其表述为完成了完整浏览器验收。

受控模型元数据和传输测试文件仅用于隔离的界面检查，不是已验收的业务模型或准确率样本。临时站点检查完成后停止；更新实际部署后新页面才会加载本次修改。
