# 路遥智伴公网网关

这是路遥智伴设备服务的 Cloudflare Workers 公网入口。它使用固定源站，不接受用户提供的目标网址，因此不是开放代理。

## 部署

点击 Cloudflare 的部署按钮，在自己的 Cloudflare 账号中确认创建。部署完成后会得到：

`https://looy-public-gateway.<你的子域>.workers.dev`

访问 `/__gateway/health` 可检查网关是否在线；访问 `/` 打开公开服务首页；访问 `/admin` 进入受保护的管理后台。

## 安全设计

- 管理功能仍由源站账号密码和会话保护。
- 跨站请求的 `Origin` 不会被伪装为源站，保留 CSRF 防护。
- 设备 IP 与城市仅通过 Cloudflare 自动标记的 Worker 子请求进行可信转发。
- `CONNECT` 和 `TRACE` 被拒绝，目标源站固定为路遥智伴服务。
