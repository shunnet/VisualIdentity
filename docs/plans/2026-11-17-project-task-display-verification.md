# YOLO 工程任务类型显示一致性修复

## 原因与范围

新建 OBB 工程正确保存 `<View yoloTask="obb">` 和 `RectangleLabels`。训练页通过 `LabelingConfigParser`、`YoloTaskRegistry.FromConfig` 读取显式 OBB 标记，因此任务和权重选择正确。工程列表、详情页却分别使用字符串 Contains 判断 XML 控件，将同一配置误判成普通矩形检测。

同类检查发现：画刷分割、Labels 分类配置会回退到检测；混合控件的优先级与训练不同；XML 注释中的控件名称也会影响显示。详情页分类布局只检查 Choices，同样可能与最终任务类型不一致。

新增 `YoloTaskPresentation` 共用解析和图标/本地化键映射。列表、详情标题、项目信息、分类布局和训练页均使用该规则。沿用既有任务注册表的优先级与旧版 EllipseLabels 兼容，不修改配置格式、数据库、训练任务、模型权重规则或已有标注。已有 OBB 工程无需重新创建；部署新版后按原配置正确显示。

## 自动验证

- 修复前运行新测试：13 项中 8 项失败，复现 OBB 显示为普通检测，以及画刷、分类、混合控件、注释和损坏 XML 的显示差异。
- 修复后类型回归：14 项全部通过，直接调用真实编译的 Razor 页面逻辑，覆盖五种创建模板、旧控件、混合优先级、分类布局和无效配置回退。
- 完整 .NET 回归：1080 通过、0 失败、0 跳过；测试项目及 Tasks 宿主构建成功。存在仓库原有分析器警告，没有编译错误。
- 扫描共享 UI：原先按 XML Contains 猜测任务类型的两处逻辑均已移除，统一交由配置解析器处理。
- `git diff --check` 通过。

命令：

```powershell
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj --filter FullyQualifiedName~ProjectTaskDisplayTests
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj --no-build --logger "trx;LogFileName=obb-full.trx" --results-directory Snet.Yolo.Test/bin/obb-results
```

本地诊断保留于忽略目录 `Snet.Yolo.Test/bin/obb-red.log`、`obb-green.log`、`obb-full.log` 和 `obb-results/obb-full.trx`。

## 实际浏览器验证

在原本没有运行时数据库的 Tasks Debug 构建输出中启动仅监听 `127.0.0.1:5182` 的临时测试宿主，使用 Edge 检查实际 Blazor 页面。

| 类型 | 列表卡片 | 详情标题/项目信息 | 训练页及默认模型 |
| --- | --- | --- | --- |
| 目标检测 | 正确 | 正确 | 目标检测，yolo26n.pt |
| 实例分割 | 正确 | 正确 | 实例分割，yolo26n-seg.pt |
| 图像分类 | 正确 | 正确，分类布局 | 图像分类，yolo26n-cls.pt |
| 姿态估计 | 正确 | 正确 | 姿态估计，yolo26n-pose.pt |
| OBB | 正确 | 正确 | 旋转框检测（OBB），yolo26n-obb.pt |

OBB 和姿态工程通过真实创建弹窗生成；随后逐一进入五种类型的详情与训练页，检查任务名称和默认模型，再刷新列表确认五种标识保持正确。最终页面截图确认图标和名称吻合。测试宿主已停止，未启动实际训练、业务图片推理或操作生产服务器。
