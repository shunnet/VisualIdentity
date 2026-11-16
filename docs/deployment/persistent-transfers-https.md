# 大文件续传的 HTTPS 部署

## 推荐方式

使用受信任的 HTTPS 站点访问 Tasks，浏览器优先选择当前稳定版 Edge 或 Chrome。所有上传入口优先保存文件访问句柄，服务端按 4 MiB 分块接收；浏览器无需再保存一份完整大包。刷新后权限仍有效时自动续传，权限需要重新确认时点击传输提示的“继续”。

现有 HTTP 地址继续兼容：每批最多缓存 64 MiB 的 File；超出预算的文件只保存名称、大小和修改时间，随后直接分块上传，避免二十多 GB 文件在浏览器整包缓存时一直显示 0%。应用内切换页面仍继续上传；完整刷新后，大文件来源引用会丢失，需点击“重新选择原文件”，通过名称/大小/修改时间及服务器已有文件开头和断点内容校验后续传。已确认的分块不重传；该模式不承诺刷新后无操作自动续传。

只对实际缓存的小文件检查配额，已知不足立即停止。缓存事务超过 30 秒明确失败；来源准备显示“准备中”，等待重选/授权或暂停时停止转圈，实际上传才显示已确认字节和百分比。准备和上传均可取消。

文件选择权限必须由用户操作授予；应用不修改浏览器安全设置。文件句柄要求安全上下文，参考 [Chrome 官方 File System Access 文档](https://developer.chrome.com/docs/capabilities/web-apis/file-system-access)。缓存估算和按源隔离行为见 [WHATWG Storage Standard](https://storage.spec.whatwg.org/)。

## 证书和访问地址

- 优先使用单位已有的固定域名和受信任证书。
- 继续使用当前 IP 时，证书的 Subject Alternative Name 必须包含实际 IP，例如 `192.168.2.187`，并由客户端正常信任的 CA 签发。
- 以下示例使用 HTTPS 端口 `5443`。域名/IP、证书文件、端口由部署环境替换。证书私钥和密码不放入源码仓库。
- 迁移 HTTP → HTTPS 或更换域名/端口前，先完成或取消当前浏览器的上传/等待下载任务。IndexedDB 和文件权限按 origin 隔离，旧地址下的缓存不会自动搬到新地址。

## Linux 源码发布部署：直接使用 Kestrel TLS

沿用当前应用及数据目录，只为 Kestrel 配置已准备好的 PFX 证书。环境变量可放入现有服务管理器的私有配置文件；下面是变量名称示例：

```sh
export ASPNETCORE_URLS='https://0.0.0.0:5443;http://0.0.0.0:5000'
export ASPNETCORE_HTTPS_PORT=5443
export ASPNETCORE_Kestrel__Certificates__Default__Path=/etc/snet/tls/tasks.pfx
export ASPNETCORE_Kestrel__Certificates__Default__Password='<PFX密码>'
```

保持当前 `Snet.Yolo.Tasks.dll` 的启动命令以及原有其他配置。Kestrel 直接处理 TLS，现有 HTTPS 重定向中间件将 HTTP 5000 重定向到 HTTPS 5443。应用运行账号需要读取 PFX，私钥访问权限只授予所需账号。

HTTPS 证书配置方法参考 [ASP.NET Core Kestrel endpoints](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints)。

## Docker

沿用 README 中的所有数据卷和 GPU 参数，新增：

```sh
-p 5443:8443 \
--env-file /etc/snet/tasks-tls.private.env \
-v /etc/snet/tls/tasks.pfx:/run/secrets/tasks.pfx:ro
```

私有环境文件内容：

```ini
ASPNETCORE_URLS=https://+:8443;http://+:8080
ASPNETCORE_HTTPS_PORT=5443
ASPNETCORE_Kestrel__Certificates__Default__Path=/run/secrets/tasks.pfx
ASPNETCORE_Kestrel__Certificates__Default__Password=<PFX密码>
```

HTTP 可以继续映射为 `-p 5000:8080`。容器运行用户须能读取挂载的证书。继续保留 `/app/transfers` 数据卷及现有数据库、图片、模型、train、sam 卷。

## 验收

1. 用最终域名/IP 访问 `https://<地址>:5443`，浏览器正常信任证书，能登录并加载图片。
2. 选择小型 ZIP，确认模型/数据集输入使用文件选择器且能成功上传；再验证断点刷新。
3. 选择大包时，浏览器优先保存文件句柄；刷新后继续读取同一文件。需要权限时点击“继续”。
4. 下载模型或数据集，在下载管理器中确认进度；页面刷新不取消下载。断点请求支持 Range 和 If-Range。
5. 分别验证用户隔离和取消，检查任务结束后暂存分块清理。
6. HTTP 兼容验收：选择超过 64 MiB 的文件，确认首个 4 MiB 分块直接发出；应用内切页仍上传，完整刷新后显示“重新选择原文件”且停止转圈。重选正确文件从断点继续，误选内容不同的同名文件被拒绝。

本次仓库更新提供应用机制和部署说明；未在生产服务器安装证书或更改现有监听配置。实际 CA 信任与原生文件选择器授权仍需在最终客户端验收。
