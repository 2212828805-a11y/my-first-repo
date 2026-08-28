# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.5.0.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.5.0 新增本机屏幕文字识别：搜索完成后可读取前台窗口的可见文字，按短期快照中的编号安全单击抖音视频或双击网易云歌曲。点击前会再次核对窗口、位置和文字；截图只在内存中处理，不保存、不上传。版本同时保留 0.4.1 的 64 位键鼠输入修复和真实自检。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
