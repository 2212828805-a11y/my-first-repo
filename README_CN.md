# 路遥智控

一个运行在用户自己 Windows 电脑上的小智 MCP 控制客户端。用户粘贴自己的 MCP 接入点地址、选择允许的权限并点击连接后，小智即可调用已授权的 Windows 工具。

## 用户实际怎么用

1. 解压 `LooyWindowsController-win-x64.zip`。
2. 双击 `LooyWindowsController.exe`，在欢迎页后输入管理员生成的激活码，并主动勾选同意隐私说明。
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
| `windows.resource_status` | 读取 CPU、内存、磁盘和网络瞬时使用情况 | 开启 |
| `windows.read_clipboard_text` | 读取剪贴板中的纯文字，最多 4000 字符 | 关闭 |
| `windows.list_apps` | 列出允许操作的应用 | 开启 |
| `windows.open_app` | 打开白名单应用 | 开启 |
| `windows.close_app` | 正常关闭白名单应用；逐一处理 Chrome/Edge 多窗口并复核结果 | 开启 |
| `windows.app_action` | 应用搜索、微信/QQ 消息准备、记事本新建与写入、媒体控制 | 开启（需要时弹出授权） |
| `windows.prepare_chat_message` | 重新搜索并核对 QQ/微信联系人，只填入消息、不发送 | 需要键盘、识屏和鼠标授权 |
| `windows.confirm_chat_send` | 用户后续明确确认后复核并发送一次 | 需要准备步骤返回的一次性编号 |
| `windows.netease_music_task` | 连续完成网易云客户端打开、搜索、识屏和播放第 N 个结果 | 按动作请求键盘、识屏和鼠标授权 |
| `windows.diagnose_apps` | 检查应用路径和运行窗口，不读取聊天内容或 Token | 开启 |
| `windows.open_url` | 打开 http/https 网页 | 开启 |
| `windows.web_search` | 百度、Bing 或 Google 浏览器搜索 | 开启 |
| `windows.media_control` | 设置准确音量、静音、播放暂停、切歌 | 开启 |
| `windows.type_text` | 向当前输入框键入文字 | 首次调用弹窗授权 |
| `windows.hotkey` | 按下受支持的快捷键 | 首次调用弹窗授权 |
| `windows.verified_screen_search` | 先输入并核对搜索词，再按画面选择点击按钮或回车 | 需要键盘、识屏和鼠标授权 |
| `windows.find_text` | 在当前文档/网页显示查找框，输入并核对后查找 | 需要键盘、识屏和鼠标授权 |
| `windows.show_desktop` | 显示 Windows 桌面 | 需要键盘授权 |
| `windows.presentation_control` | 控制前台 PowerPoint/WPS 上一页、下一页和放映 | 需要键盘授权 |
| `windows.cursor_position` | 读取鼠标位置 | 首次调用弹窗授权 |
| `windows.move_mouse` | 移动鼠标 | 首次调用弹窗授权 |
| `windows.click` | 单击或双击鼠标 | 首次调用弹窗授权 |
| `windows.scroll` | 页面滚动 | 首次调用弹窗授权 |
| `windows.inspect_screen` | 本机 OCR 识别前台窗口并返回带编号的可见文字 | 首次调用弹窗授权 |
| `windows.open_screen_text` | 识别当前画面上的软件名称并直接单击或双击打开 | 需要屏幕识别和鼠标授权 |
| `windows.click_screen_item` | 核对快照后单击或双击指定文字编号 | 需要屏幕识别和鼠标授权 |
| `windows.system_control` | 切换主题、设置本机壁纸、取消关机/重启计划 | 关闭 |
| `windows.prepare_power_action` | 准备锁定、关机或重启，不立即执行 | 关闭 |
| `windows.confirm_power_action` | 用户后续明确确认后执行一次电源操作 | 需要一次性编号 |
| `windows.screenshot` | 截取全部屏幕并保存到本机 | 关闭 |

程序刻意不提供任意命令行、删除文件、安装软件或管理员提权能力。安全说明见 [SECURITY.md](SECURITY.md)。

## 可以对小智说什么

- “路遥，打开记事本。”
- “新建一个记事本，写入今天的待办事项。”
- “打开 Edge，搜索郑州明天的天气。”
- 开启微信及所需权限后：“用微信给文件传输助手准备消息：测试完成。”在屏幕核对无误后，后续单独说：“确认发送。”
- 开启 QQ 及所需权限后：“用 QQ 给我的手机准备消息：测试完成。”换联系人时会重新搜索；核对无误后再单独说：“确认发送。”
- 开启网易云音乐和所需权限后：“打开网易云音乐。”、“在网易云搜索晴天并播放第二个结果。”、“暂停网易云音乐。”
- 当前桌面或菜单显示软件名称时：“打开屏幕上的微信。”同名项目不唯一时，路遥会先返回位置供选择，不会猜测。
- “在网页搜索抖音美食视频，然后打开第二个视频。”搜索完成后路遥会先识别结果，再按编号单击。
- “把电脑静音。”
- “把音量设置为 35%。”
- “暂停音乐。”
- “显示桌面。”
- 打开 PowerPoint/WPS 演示后：“下一页。”、“从当前页开始放映。”
- “查看 CPU、内存和磁盘使用情况。”
- 开启系统设置权限后：“切换为深色主题。”
- 开启系统设置权限后：“准备 5 分钟后关机。”核对无误后，后续单独说：“确认关机。”
- 开启键盘权限后：“按下 Ctrl 加 L。”
- 开启鼠标权限后：“把鼠标移动到 600、400，然后单击。”

自然语言能否准确触发工具，还会受到你所使用的大模型工具调用能力影响。建议在智能体提示词中加入：

```text
当用户要求操作 Windows 电脑时，优先使用 windows.* 工具。
打开应用前，如果不确定别名，先调用 windows.list_apps。
任何鼠标、键盘操作都应先确认目标窗口，禁止猜测坐标。
所有可见搜索框都应优先调用 windows.verified_screen_search，或使用带相同核对流程的应用专用工具：先聚焦输入框并输入，OCR 核对搜索词无误后，屏幕有唯一搜索按钮就点击，否则才按回车；禁止先提交再输入。
QQ/微信消息必须先调用 windows.prepare_chat_message；该工具只填入不发送。不得在同一条用户请求中调用 windows.confirm_chat_send。只有用户在后续消息中明确说“确认发送”时，才使用最近的一次性确认编号发送一次。
锁定、关机和重启必须先调用 windows.prepare_power_action；不得在同一条用户请求中调用 windows.confirm_power_action。只有用户在后续消息中明确确认时，才使用最近的一次性确认编号执行一次。
网易云请求始终优先调用 windows.netease_music_task；不要使用 windows.web_search。只有用户明确要求“用网页/浏览器搜索网易云”时才能使用网页搜索并设置 force_browser=true。
其他网页结果需要点击时，先调用 windows.inspect_screen，再根据返回的标题文字编号调用 windows.click_screen_item。需要打开当前画面中的软件名称时，调用 windows.open_screen_text。
```

## 开发者：在 Windows 一键构建

普通用户优先下载并运行 GitHub Actions 生成的 `LooyWindowsController-Setup-0.7.0.exe`，不需要执行下面的源码构建脚本。安装程序默认安装到当前用户目录，不要求管理员权限。

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
- 推送形如 `v0.7.0` 的标签。

构建结束后，在该次 Actions 页面下载 `LooyWindowsController-Windows-v0.7.0` artifact 即可。

## 默认应用列表

默认启用：记事本、计算器、文件资源管理器、Windows 设置、Microsoft Edge。

Chrome、微信、QQ、抖音、网易云音乐和 VS Code 已提供示例别名，但默认关闭。安装后先在“应用管理”中点击“自动检测路径”，再勾选需要的应用。程序会检查正在运行的进程、Windows 注册表和常见安装目录；仍未找到时再双击对应行选择实际 `.exe` 路径。

微信兼容新版 `Weixin.exe` 和旧版 `WeChat.exe`，QQ 兼容新版 QQNT 常见安装目录；两者均支持激活、重新搜索并核对联系人、准备消息和二次确认发送。网易云音乐优先直接启动真实程序，避免自定义协议确认页，并支持搜索、播放/暂停、上一首和下一首。首次需要键盘、鼠标或屏幕文字识别时，电脑会弹出授权窗口，可选择“仅本次连接”或“始终允许”。

0.5.0 在 0.4.1 的 64 位键鼠修复和真实自检基础上新增本机 OCR 屏幕文字识别。搜索结果显示后，`windows.inspect_screen` 会在内存中截取当前前台窗口并返回带编号的文字；`windows.click_screen_item` 会在 90 秒内再次核对同一窗口、窗口位置和目标文字，再移动鼠标完成单击或双击。截图不会保存到磁盘或上传，只有识别出的文字会返回给当前连接的路遥。页面滚动、窗口移动、文字消失或目标窗口改变时，程序会拒绝点击并要求重新识别。

0.5.1 增加 `windows.netease_music_task`，把网易云的打开、搜索、识别结果与双击播放合并为一个连续任务；普通网页搜索会主动拒绝误接网易云应用请求。`windows.open_screen_text` 可在当前画面中查找指定软件文字并执行打开操作；多个同名结果必须明确选择序号，点击前仍会再次核对窗口、位置和文字。

0.5.2 统一修正可见搜索流程：先聚焦输入框、输入并 OCR 核对搜索词，再根据屏幕是否存在唯一独立搜索按钮决定鼠标点击或回车。QQ 和微信消息改为两步操作：每次换人都会重新搜索唯一联系人并核对会话标题，只清除旧草稿和填入新消息；用户后续明确“确认发送”后，程序再次核对联系人与草稿并只发送一次。确认编号两分钟失效且不可重复使用。

0.6.0 参考公开 [xiaozhi-MCPTools](https://github.com/ZongZiTongXue/xiaozhi-MCPTools) 的功能清单重新实现安全系统控制：新增资源监控、剪贴板文字读取、显示桌面、文档查找、PowerPoint/WPS 演示控制、准确音量、主题和本机壁纸，以及准备/确认两步的锁定、关机和重启。没有复制任意 CMD、任意文件写入、直接自动发送、自动修改杀毒白名单或依赖固定等待和固定 Tab 次数的实现。

0.6.1 重点修复连续控制不稳定：QQ、微信和网易云在搜索框已经存在旧关键词时，会优先使用上次核对位置或应用搜索快捷键，输入新内容后再通过 OCR 核对顶部搜索区域；核对失败会撤销本次输入，不会提交。联系人、会话、草稿和网易云歌曲结果改为动态等待；诊断报告会包含最近 80 条本机控制结果和耗时，便于继续定位偶发问题。

0.6.2 增加路遥智伴欢迎页，并增强网易云音乐 2.x/3.x 搜索框定位：当空框、旧关键词或主题导致 OCR 看不到“搜索”占位文字，程序会在确认前台进程确为 cloudmusic 后依次尝试两个顶部安全候选区；每次都先输入、再 OCR 核对，失败即撤销，只有核对成功才提交。同步修复 Edge 协议启动后无法定位真实进程的问题；Chrome、Edge 等多窗口浏览器会逐一请求正常关闭并等待复核，后台常驻不再误报失败，权限级别不一致时会提示使用已有管理员模式，不会强杀进程。收费与设备管理能力预留在独立后台，收费总开关默认关闭。

0.7.0 正式接入路遥智伴设备管理后台。首次启动时会在欢迎页之后显示设备绑定窗口，只有用户阅读并勾选隐私说明、输入有效激活码后才进入主程序。每台电脑生成独立 P-256 设备密钥，私钥与授权凭证由 Windows 当前用户加密保存；联网请求带时间戳、随机数和签名，防止复制凭证或重放请求。程序每 15 分钟复核一次授权，后台封禁、激活码停用或到期会停止连接；授权服务短暂不可用时，从最后成功校验起最多允许 72 小时离线宽限。

0.6.2 的正式构建还会执行基础代码混淆、字符串隐藏和 IL 优化，不打包源码、PDB 或混淆映射。它能明显增加直接复制和反编译修改的难度，但任何交付到用户电脑的软件都无法保证绝对不可逆向；真正用于验证发布者与发现二次篡改仍需配置受信任的 Windows 代码签名证书。

应用配置和加密后的 MCP 地址保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\settings.json
```

设备公钥、Windows 加密后的私钥和授权凭证保存在：

```text
%LOCALAPPDATA%\LOOY\WindowsController\device-license.json
```

不要删除或复制该文件。删除后会生成新的设备身份，并可能额外占用激活码的设备名额。

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
- 首次设备绑定必须联网；绑定后授权服务短暂不可用时最多使用 72 小时离线宽限。
- 普通输入模式无法控制管理员权限窗口；用户可在授权管理页手动确认并切换管理员输入模式，但 UAC 安全桌面仍无法自动操作。
- 部分游戏、反作弊软件和安全软件会阻止模拟输入。
- 目前是 Windows x64 版本，暂未打包 ARM64。
- 普通截图只保存在电脑本地，没有把屏幕图片上传给大模型；屏幕文字识别截图则只在内存中短暂处理且不会保存，识别文字会返回给当前连接的路遥。
- 屏幕文字识别依赖 Windows 已安装的 OCR 语言；若提示没有可用语言，请在 Windows“语言选项”中安装当前语言的“基本输入”。
- 正式商业分发前建议购买代码签名证书，降低 Windows SmartScreen 的未知发布者提示。

## 项目结构

```text
src/LooyWindowsController/   Windows 桌面客户端源码
scripts/build-windows.ps1   Windows 一键发布脚本
.github/workflows/          GitHub 自动构建流程
SECURITY.md                 权限与安全边界
```
