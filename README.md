# 路遥电脑控制器

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.2.0.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥电脑控制器”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接小智”。

0.2.0 新增微信、网易云音乐等第三方应用的自动路径检测、窗口激活、应用内搜索、媒体控制和本地诊断报告。诊断报告不会包含 MCP Token。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
