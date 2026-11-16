# 128 GiB YOLO 数据集上传与下载自检

日期：2026-11-16。环境：Windows、.NET 11，使用独立测试用户和工程；没有操作生产数据或部署服务。

## 修改范围

- ZIP 上传上限为 128 GiB，由 Core 的 `YoloWithImagesImporter.MaximumArchiveBytes` 统一定义。上传准入、浏览器读取流、Kestrel 和 multipart 配置复用该值。
- 解压后图片总量上限为 256 GiB。单图 100 MiB、最多 100,000 张图片、目录穿越及标注自检仍保留。
- ZIP 先流式落盘，再自检和逐张解压。上传、自检和解压读写均传递取消令牌；提交前失败回滚本批文件、任务及标签配置。
- 解压时一次建立 ZIP 目录索引，避免为每张图片重新遍历全部条目。
- 临时 ZIP 写入后，再按实际解压图片总量检查目标卷剩余空间；各阶段保留 10% 余量。
- 下载继续使用异步 ZIP64，不缓存完整原图、归档或 Base64，不生成同等大小的服务端临时 ZIP。

Linux 磁盘检查使用实际目录查询所属挂载卷，避免将容器数据卷误判为根分区。实现核对了 [.NET 11 DriveInfo 空间查询](https://raw.githubusercontent.com/dotnet/runtime/v11.0.0/src/libraries/System.IO.FileSystem.DriveInfo/src/System/IO/DriveInfo.Unix.cs)及[路径保留规则](https://raw.githubusercontent.com/dotnet/runtime/v11.0.0/src/libraries/System.IO.FileSystem.DriveInfo/src/System/IO/DriveInfo.UnixOrDefault.cs)；本轮没有在 Linux 容器中运行挂载卷验收。

## 验证结果

| 检查项 | 结果与证据 |
|---|---|
| 上传大小边界 | 50、100、128 GiB 接受；128 GiB + 1 字节拒绝。界面与 Core 上限一致 |
| 实际读取入口 | 小型真实 ZIP 经上传中心完成导入，记录到 `OpenReadStream` 实际使用 128 GiB 上限和可取消令牌 |
| 运行时宿主配置 | 从真实 Tasks 宿主读取 Kestrel 和 multipart Options，两者均为 128 GiB |
| 标注与原有数据 | 新导入的标签、原图尺寸、空标注均正确；原有任务保留 |
| 上传取消与坏图回滚 | 实际上传中心取消或遇到第二张坏图后，原有任务及标签保持，新增文件和任务不残留 |
| 解压总量 | 使用合成 ZIP 中央目录元数据：约 195 GiB 图片总量通过；约 264 GiB 被 256 GiB 闸门拒绝。真实图片内容仍由解压后的解码器检查 |
| 自检与解压取消 | 已取消自检不继续解析；阻塞在图片数据读取时取消，实际解压方法停止并清除 `.upload` 半文件 |
| 磁盘空间不足 | 以超过目标卷总容量的 64 位需求值验证拒绝逻辑，不创建巨型磁盘文件 |
| ZIP64 下载 | 50、128 GiB 合成数据真实流经写入器，输出端不支持同步写入或定位，仅保留首尾目录信息；长度正确，单次写入不超过 128 KiB |
| 大量条目与下载取消 | 超过 65,535 个条目保持完整；取消后源流释放，不继续打开下一张图片 |
| HTTP 下载与归属 | 登录、当前工程权限、原图与标签完整性、缺图及非法路径错误继续通过既有回归 |
| 全量测试 | 1025 项通过，0 失败、0 跳过 |

执行命令（本机使用仓库短路径别名，产物置于被忽略的 `bin` 中）：

```powershell
dotnet test Snet.Yolo.Test/Snet.Yolo.Test.csproj -c Release --no-restore --artifacts-path D:/vri16/Snet.Yolo.Test/bin/i
```

## 验证边界

- 128 GiB 下载使用按需生成的合成流，不在开发机保存同等大小文件；没有进行真实生产 128 GiB 全程上传或公网下载。
- 上传测试覆盖 128 GiB 准入边界、实际读取上限及小型真实 ZIP 全流程，不代表生产网络吞吐保证。
- 上传需同时容纳临时 ZIP 和解压图片；并发写入仍可能改变剩余空间。
- 下载不支持断点续传；中断需重新开始。反向代理的请求体、超时和响应缓冲配置仍须配合部署。
