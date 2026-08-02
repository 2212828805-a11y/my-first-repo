# 路遥电脑控制器

一个运行在用户自己 Windows 电脑上的小智 MCP 控制客户端。用户粘贴自己的 MCP 接入点地址、选择允许的权限并点击连接后，小智即可调用已授权的 Windows 工具。

## 用户实际怎么用

1. 解压 `LooyWindowsController-win-x64.zip`。
2. 双击 `LooyWindowsController.exe`。
3. 在小智智控台打开对应智能体的“编辑功能”，复制 MCP 接入点地址。
4. 将形如下面的完整地址粘贴进程序：

   ```text
   wss://你的服务器/mcp_endpoint/mcp/?token=你的专属Token
   ```

5. 在“权限”页面只勾选需要的能力。
6. 在“应用白名单”页面启用允许小智打开的程序。
7. 点击“连接小智”。日志显示“已向小智注册 N 个工具”即接入完成。

不要把带 Token 的 MCP 地址截图、发视频或分享给别人。

## 第一版支持的能力

| 工具 | 作用 | 默认状态 |
| --- | --- | --- |
| `windows.system_status` | 读取电脑名称、系统和时间 | 开启 |
| `windows.list_apps` | 列出允许操作的应用 | 开启 |
| `windows.open_app` | 打开白名单应用 | 开启 |
| `windows.close_app` | 请求白名单应用正常关闭 | 开启 |
| `windows.open_url` | 打开 http/https 网页 | 开启 |
| `windows.web_search` | 百度或 Bing 搜索 | 开启 |
| `windows.media_control` | 音量、静音、播放暂停、切歌 | 开启 |
| `windows.type_text` | 向当前输入框键入文字 | 关闭 |
| `windows.hotkey` | 按下受支持的快捷键 | 关闭 |
| `windows.move_mouse` | 移动鼠标 | 关闭 |
| `windows.click` | 单击或双击鼠标 | 关闭 |
| `windows.scroll` | 页面滚动 | 关闭 |
| `windows.screenshot` | 截取全部屏幕并保存到本机 | 关闭 |

程序刻意不提供任意命令行、删除文件、安装软件或管理员提权能力。安全说明见 [SECURITY.md](SECURITY.md)。

## 可以对小智说什么

- “路遥，打开记事本。”
- “打开 Edge，搜索郑州明天的天气。”
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

普通用户优先下载并运行 GitHub Actions 生成的 `LooyWindowsController-Setup-0.1.1.exe`，不需要执行下面的源码构建脚本。安装程序默认安装到当前用户目录，不要求管理员权限。

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
- 推送形如 `v0.1.0` 的标签。

构建结束后，在该次 Actions 页面下载 `LooyWindowsController-win-x64` artifact 即可。

## 默认应用白名单

默认启用：记事本、计算器、文件资源管理器、Windows 设置、Microsoft Edge。

Chrome、微信、抖音、网易云音乐和 VS Code 已提供示例别名，但默认关闭。不同电脑的安装路径不同，如果直接启用后打不开，请在“应用白名单”页面填写实际 `.exe` 路径。

应用配置和加密后的 MCP 地址保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\settings.json
```

截图保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\Screenshots
```

## 当前限制

- 电脑休眠、关机、断网或程序退出后无法接收调用。
- 普通权限程序无法控制管理员权限窗口或 UAC 安全桌面。
- 部分游戏、反作弊软件和安全软件会阻止模拟输入。
- 目前是 Windows x64 版本，暂未打包 ARM64。
- 第一版截图只保存在电脑本地，没有把屏幕图片上传给大模型。
- 正式商业分发前建议购买代码签名证书，降低 Windows SmartScreen 的未知发布者提示。

## 项目结构

```text
src/LooyWindowsController/   Windows 桌面客户端源码
scripts/build-windows.ps1   Windows 一键发布脚本
.github/workflows/          GitHub 自动构建流程
SECURITY.md                 权限与安全边界
```
