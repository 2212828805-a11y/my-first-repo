# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.3.0.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.3.0 使用简约暖白界面和全新应用图标，增强窗口激活与输入前的焦点检查，并改进网易云音乐搜索和微信联系人发消息流程。诊断报告不会包含 MCP Token 或消息内容。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
