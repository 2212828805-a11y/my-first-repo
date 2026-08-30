# 路遥智控

通过用户自己的小智 MCP 接入点，在明确授权范围内控制 Windows 10/11 电脑。

## 下载与安装

每次 GitHub Actions 构建会生成两个文件：

- `LooyWindowsController-Setup-0.6.2.exe`：Windows 安装程序。
- `LooyWindowsController-win-x64.zip`：免安装便携版。

安装后打开“路遥智控”，粘贴自己的 MCP 接入点，选择允许的权限和应用，然后点击“连接路遥”。

0.6.0 在保留网易云、通用识屏搜索和 QQ/微信二次确认发送的基础上，吸收公开 [xiaozhi-MCPTools](https://github.com/ZongZiTongXue/xiaozhi-MCPTools) 的可解释控制思路，新增资源监控、剪贴板文字读取、显示桌面、文档查找、PPT/WPS 演示控制、准确音量、主题/壁纸设置，以及二次确认的锁定/关机/重启。没有引入任意 CMD、任意文件写入、自动加入杀毒白名单或固定 Tab 次数盲点等高风险实现。

0.6.1 修复 QQ/微信/网易云搜索框在已有旧关键词时无法再次定位的问题：会记忆已核对的搜索位置，并为桌面应用使用搜索快捷键后再通过 OCR 核对输入位置。联系人、会话和歌曲结果改为按实际出现时间动态等待；OCR 点击复核允许轻微识别波动，网页和普通应用文字默认单击，避免重复打开。

0.6.2 增加路遥智伴欢迎页、网易云音乐 2.x/3.x 搜索框安全候选定位，以及浏览器关闭复核。程序只有在确认前台进程确为 CloudMusic 后才尝试顶部候选框，输入后必须通过 OCR 核对，失败即撤销；Edge 的协议启动会映射到真实 `msedge.exe`，Chrome、Edge 等多窗口浏览器会逐一正常关闭可见顶层窗口，不强杀后台进程。

详细说明请查看 [README_CN.md](README_CN.md)，安全边界请查看 [SECURITY.md](SECURITY.md)。
