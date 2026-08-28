# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.4.1.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.4.1 在 0.3.2 的 64 位键盘修复上继续统一键盘与鼠标输入接口，检查每一次移动、点击和滚动是否真的被 Windows 接收，增强后台线程激活前台窗口的稳定性，并提供应用内“检测键盘与鼠标”真实自检。诊断报告不会包含 MCP Token 或消息内容。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
