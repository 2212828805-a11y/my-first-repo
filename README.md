# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.3.2.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.3.2 修正了 64 位 Windows 键盘输入结构尺寸错误，搜索、记事本写入和微信/QQ 消息不再被系统误拦截；授权页也会显示普通/管理员输入模式，并允许用户在本机确认后以管理员模式重启。诊断报告不会包含 MCP Token 或消息内容。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
