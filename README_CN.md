# 路遥智控

一个运行在用户自己 Windows 电脑上的小智 MCP 控制客户端。用户粘贴自己的 MCP 接入点地址、选择允许的权限并点击连接后，小智即可调用已授权的 Windows 工具。

## 用户实际怎么用

1. 解压 `LooyWindowsController-win-x64.zip`。
2. 双击 `LooyWindowsController.exe`。
3. 在小智智控台打开对应智能体的“编辑功能”，复制 MCP 接入点地址。
4. 将形如下面的完整地址粘贴进程序：

   ```text
   wss://你的服务器/mcp_endpoint/mcp/?token=你的专属Token
   ```

5. 在“授权管理”中只勾选需要的能力。
6. 在“应用管理”中启用允许路遥操作的程序。
7. 点击“连接路遥”。运行记录显示“已向小智注册 N 个工具”即接入完成。

不要把带 Token 的 MCP 地址截图、发视频或分享给别人。

## 支持的能力

| 工具 | 作用 | 默认状态 |
| --- | --- | --- |
| `windows.system_status` | 读取电脑名称、系统和时间 | 开启 |
| `windows.list_apps` | 列出允许操作的应用 | 开启 |
| `windows.open_app` | 打开白名单应用 | 开启 |
| `windows.close_app` | 请求白名单应用正常关闭 | 开启 |
| `windows.app_action` | 应用搜索、微信/QQ 发消息、记事本新建与写入、媒体控制 | 开启（需要时弹出键盘授权） |
| `windows.diagnose_apps` | 检查应用路径和运行窗口，不读取聊天内容或 Token | 开启 |
| `windows.open_url` | 打开 http/https 网页 | 开启 |
| `windows.web_search` | 百度、Bing 或 Google 浏览器搜索 | 开启 |
| `windows.media_control` | 音量、静音、播放暂停、切歌 | 开启 |
| `windows.type_text` | 向当前输入框键入文字 | 首次调用弹窗授权 |
| `windows.hotkey` | 按下受支持的快捷键 | 首次调用弹窗授权 |
| `windows.cursor_position` | 读取鼠标位置 | 首次调用弹窗授权 |
| `windows.move_mouse` | 移动鼠标 | 首次调用弹窗授权 |
| `windows.click` | 单击或双击鼠标 | 首次调用弹窗授权 |
| `windows.scroll` | 页面滚动 | 首次调用弹窗授权 |
| `windows.screenshot` | 截取全部屏幕并保存到本机 | 关闭 |

程序刻意不提供任意命令行、删除文件、安装软件或管理员提权能力。安全说明见 [SECURITY.md](SECURITY.md)。

## 可以对小智说什么

- “路遥，打开记事本。”
- “新建一个记事本，写入今天的待办事项。”
- “打开 Edge，搜索郑州明天的天气。”
- 开启微信和键盘权限后：“用微信给文件传输助手发送：测试完成。”
- 开启 QQ 和键盘权限后：“用 QQ 给我的手机发送：测试完成。”
- 开启网易云音乐、键盘和媒体权限后：“在网易云搜索周杰伦。”、“暂停网易云音乐。”、“网易云下一首。”
- “把电脑静音。”
- “暂停音乐。”
- 开启键盘权限后：“按下 Ctrl 加 L。”
- 开启鼠标权限后：“把鼠标移动到 600、400，然后单击。”

自然语言能否准确触发工具，还会受到你所使用的大模型工具调用能力影响。建议在智能体提示词中加入：

```text
当用户要求操作 Windows 电脑时，优先使用 windows.* 工具。
打开应用前，如果不确定别名，先调用 windows.list_apps。
任何鼠标、键盘操作都应先确认目标窗口，禁止猜测坐标。
```

## 开发者：在 Windows 一键构建

普通用户优先下载并运行 GitHub Actions 生成的 `LooyWindowsController-Setup-0.3.1.exe`，不需要执行下面的源码构建脚本。安装程序默认安装到当前用户目录，不要求管理员权限。

项目需要 Windows 10/11。建议先双击根目录中的英文诊断启动器：

```text
START_BUILD_DIAGNOSTIC.cmd
```

它会在真正构建之前先暂停，方便判断 Windows 是否阻止了脚本。也可以使用 `构建Windows版本.bat`。

脚本会检查 .NET 8 SDK；若系统支持 `winget`，缺少时会提示并自动安装。构建完成后生成：

```text
dist\win-x64\LooyWindowsController.exe
dist\LooyWindowsController-win-x64.zip
```

生成的程序为 Windows x64、自包含、单文件发布，普通用户不需要另装 .NET。

构建窗口无论成功或失败都会停留。发生错误时，同一目录会生成 `build.log`；把这个文件发给开发者即可继续定位。

如果双击构建脚本时提示找不到 `scripts\build-windows.ps1`，说明你是在压缩包预览里直接运行。请先对压缩包选择“全部解压”，再从解压后的完整文件夹运行。

也可以手动执行：

```powershell
dotnet publish src\LooyWindowsController\LooyWindowsController.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

## GitHub 自动生成 Windows 包

仓库内已提供 `.github/workflows/build-windows.yml`。

- 手动进入 GitHub Actions 运行 `Build Windows release`；或
- 推送形如 `v0.3.1` 的标签。

构建结束后，在该次 Actions 页面下载 `LooyWindowsController-Windows-v0.3.1` artifact 即可。

## 默认应用列表

默认启用：记事本、计算器、文件资源管理器、Windows 设置、Microsoft Edge。

Chrome、微信、QQ、抖音、网易云音乐和 VS Code 已提供示例别名，但默认关闭。安装后先在“应用管理”中点击“自动检测路径”，再勾选需要的应用。程序会检查正在运行的进程、Windows 注册表和常见安装目录；仍未找到时再双击对应行选择实际 `.exe` 路径。

微信兼容新版 `Weixin.exe` 和旧版 `WeChat.exe`，QQ 兼容新版 QQNT 常见安装目录；两者均支持激活、搜索联系人和发送消息。网易云音乐优先直接启动真实程序，避免自定义协议确认页，并支持搜索、播放/暂停、上一首和下一首。首次需要键盘或鼠标时，电脑会弹出授权窗口，可选择“仅本次连接”或“始终允许”。

应用配置和加密后的 MCP 地址保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\settings.json
```

截图保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\Screenshots
```

如果第三方应用仍然打不开或不能执行动作，请在“运行记录”中点击“导出诊断”。报告保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\Diagnostics
```

诊断报告只包含应用配置路径、检测结果、进程号和窗口句柄，不包含 MCP 接入点、Token、聊天内容或窗口标题。把该 `.txt` 文件发给开发者即可继续适配你的实际安装版本。

## 当前限制

- 电脑休眠、关机、断网或程序退出后无法接收调用。
- 普通权限程序无法控制管理员权限窗口或 UAC 安全桌面。
- 部分游戏、反作弊软件和安全软件会阻止模拟输入。
- 目前是 Windows x64 版本，暂未打包 ARM64。
- 截图只保存在电脑本地，没有把屏幕图片上传给大模型。
- 正式商业分发前建议购买代码签名证书，降低 Windows SmartScreen 的未知发布者提示。

## 项目结构

```text
src/LooyWindowsController/   Windows 桌面客户端源码
scripts/build-windows.ps1   Windows 一键发布脚本
.github/workflows/          GitHub 自动构建流程
SECURITY.md                 权限与安全边界
```
