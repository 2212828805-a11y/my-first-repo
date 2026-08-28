# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.5.2.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.5.2 在网易云连续任务和按屏幕文字打开功能之上，统一改为先输入并 OCR 核对搜索词，再按画面决定点击搜索按钮或回车。QQ 与微信消息采用准备、确认两步流程：换人必定重新搜索并核对联系人，只填入不发送；用户后续明确确认后再次核对并只发送一次。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
