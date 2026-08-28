# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.5.1.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.5.1 新增网易云桌面客户端专用连续任务，避免把“打开网易云”误送到浏览器，并可在一次调用中完成打开、应用内搜索、屏幕识别和播放第 N 个结果。还可根据当前画面识别出的软件名称直接单击或双击打开。版本继续保留本机 OCR、点击前复核和 64 位键鼠输入修复。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
