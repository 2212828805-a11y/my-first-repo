using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Looy.WindowsController;

internal sealed class WindowsController
{
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const int ExpectedInputSizeX86 = 28;
    private const int ExpectedInputSizeX64 = 40;
    private const uint GetAncestorRootOwner = 3;

    private readonly Func<string, bool> _permissionEnabled;
    private readonly Func<string, string, CancellationToken, Task<bool>> _requestInputPermission;
    private readonly Func<IReadOnlyList<AppEntry>> _getApps;
    private readonly SettingsStore _settingsStore;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    internal static bool IsNativeInputLayoutValid =>
        Marshal.SizeOf<Input>() == (IntPtr.Size == 8 ? ExpectedInputSizeX64 : ExpectedInputSizeX86);

    internal static bool IsNativeInputEngineValid
    {
        get
        {
            var keyboard = CreateUnicodeInput('路', false);
            var hotkey = CreateVirtualKeyInput(0x41, true);
            var mouse = CreateMouseInput(MouseEventLeftDown, 12, 34, 56);
            return IsNativeInputLayoutValid
                   && keyboard.Type == InputKeyboard
                   && keyboard.Data.Keyboard.ScanCode == '路'
                   && keyboard.Data.Keyboard.Flags == KeyEventUnicode
                   && hotkey.Type == InputKeyboard
                   && hotkey.Data.Keyboard.VirtualKey == 0x41
                   && (hotkey.Data.Keyboard.Flags & KeyEventKeyUp) != 0
                   && mouse.Type == InputMouse
                   && mouse.Data.Mouse.X == 12
                   && mouse.Data.Mouse.Y == 34
                   && mouse.Data.Mouse.MouseData == 56
                   && mouse.Data.Mouse.Flags == MouseEventLeftDown;
        }
    }

    internal ToolExecutionResult TypeTextForSelfTest(string text) => TypeText(text);

    internal ToolExecutionResult MoveMouseForSelfTest(int x, int y) => MoveMouse(x, y);

    internal ToolExecutionResult ClickLeftForSelfTest() => SendMouseButton("left", 1);

    public WindowsController(
        Func<string, bool> permissionEnabled,
        Func<string, string, CancellationToken, Task<bool>> requestInputPermission,
        Func<IReadOnlyList<AppEntry>> getApps,
        SettingsStore settingsStore,
        Action<string> log)
    {
        _permissionEnabled = permissionEnabled;
        _requestInputPermission = requestInputPermission;
        _getApps = getApps;
        _settingsStore = settingsStore;
        _log = log;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            return toolName switch
            {
                "windows.system_status" => RequirePermission(PermissionKeys.SystemStatus, GetSystemStatus),
                "windows.list_apps" => RequirePermission(PermissionKeys.Applications, ListApps),
                "windows.open_app" => RequirePermission(
                    PermissionKeys.Applications,
                    () => OpenApp(RequiredString(arguments, "app"))),
                "windows.close_app" => RequirePermission(
                    PermissionKeys.Applications,
                    () => CloseApp(RequiredString(arguments, "app"))),
                "windows.app_action" => !_permissionEnabled(PermissionKeys.Applications)
                    ? ToolExecutionResult.Fail("用户尚未在路遥智控中授权此项操作。")
                    : await AppActionAsync(
                        RequiredString(arguments, "app"),
                        RequiredString(arguments, "action"),
                        OptionalString(arguments, "query", string.Empty),
                        OptionalString(arguments, "recipient", string.Empty),
                        OptionalString(arguments, "message", string.Empty),
                        OptionalString(arguments, "text", string.Empty),
                        cancellationToken),
                "windows.diagnose_apps" => RequirePermission(PermissionKeys.Applications, DiagnoseApps),
                "windows.open_url" => RequirePermission(
                    PermissionKeys.Web,
                    () => OpenUrl(RequiredString(arguments, "url"))),
                "windows.web_search" => RequirePermission(
                    PermissionKeys.Web,
                    () => WebSearch(
                        RequiredString(arguments, "query"),
                        OptionalString(arguments, "engine", "baidu"))),
                "windows.type_text" => await RequireInputPermissionAsync(
                    PermissionKeys.Keyboard,
                    "向当前窗口输入文字",
                    () => TypeText(RequiredString(arguments, "text")),
                    cancellationToken),
                "windows.hotkey" => await RequireInputPermissionAsync(
                    PermissionKeys.Keyboard,
                    "向当前窗口发送键盘快捷键",
                    () => PressHotkey(RequiredString(arguments, "keys")),
                    cancellationToken),
                "windows.cursor_position" => await RequireInputPermissionAsync(
                    PermissionKeys.Mouse,
                    "读取当前鼠标位置",
                    GetCursorPosition,
                    cancellationToken),
                "windows.move_mouse" => await RequireInputPermissionAsync(
                    PermissionKeys.Mouse,
                    "移动鼠标指针",
                    () => MoveMouse(RequiredInt(arguments, "x"), RequiredInt(arguments, "y")),
                    cancellationToken),
                "windows.click" => await RequireInputPermissionAsync(
                    PermissionKeys.Mouse,
                    "点击鼠标",
                    () => Click(arguments),
                    cancellationToken),
                "windows.scroll" => await RequireInputPermissionAsync(
                    PermissionKeys.Mouse,
                    "滚动当前窗口",
                    () => Scroll(RequiredInt(arguments, "amount")),
                    cancellationToken),
                "windows.media_control" => RequirePermission(
                    PermissionKeys.Media,
                    () => MediaControl(
                        RequiredString(arguments, "action"),
                        OptionalInt(arguments, "steps", 2))),
                "windows.screenshot" => RequirePermission(PermissionKeys.Screenshot, TakeScreenshot),
                _ => ToolExecutionResult.Fail($"未知工具：{toolName}")
            };
        }
        catch (OperationCanceledException)
        {
            return ToolExecutionResult.Fail("操作已取消。");
        }
        catch (ArgumentException exception)
        {
            return ToolExecutionResult.Fail(exception.Message);
        }
        catch (Exception exception)
        {
            _log($"工具执行失败 [{toolName}]：{exception.Message}");
            return ToolExecutionResult.Fail($"执行失败：{exception.Message}");
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private ToolExecutionResult RequirePermission(string permission, Func<ToolExecutionResult> action)
    {
        return _permissionEnabled(permission)
            ? action()
            : ToolExecutionResult.Fail("用户尚未在路遥智控中授权此项操作。");
    }

    private async Task<ToolExecutionResult> RequireInputPermissionAsync(
        string permission,
        string reason,
        Func<ToolExecutionResult> action,
        CancellationToken cancellationToken)
    {
        if (!_permissionEnabled(permission)
            && !await _requestInputPermission(permission, reason, cancellationToken))
        {
            return ToolExecutionResult.Fail("用户没有授权本次键盘或鼠标操作，操作已取消。");
        }
        if (!_permissionEnabled(permission))
        {
            return ToolExecutionResult.Fail("键盘或鼠标授权当前不可用，操作已取消。");
        }
        return action();
    }

    private static ToolExecutionResult GetSystemStatus()
    {
        var message = string.Join(
            Environment.NewLine,
            $"电脑名称：{Environment.MachineName}",
            $"当前用户：{Environment.UserName}",
            $"系统：{Environment.OSVersion}",
            $"64 位系统：{Environment.Is64BitOperatingSystem}",
            $"键盘输入层级：{(WindowsInputAccess.IsElevated ? "管理员模式" : "普通模式")}",
            $"键鼠输入组件：{(IsNativeInputEngineValid ? "正常" : "异常")}",
            $"当前时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            "控制器状态：在线");
        return ToolExecutionResult.Ok(message);
    }

    private ToolExecutionResult ListApps()
    {
        var enabledApps = _getApps()
            .Where(app => app.Enabled)
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(app =>
            {
                var resolved = InstalledAppResolver.TryResolvePath(app);
                var state = resolved is null ? "未自动找到路径" : "已检测";
                return $"{app.Alias}（{app.DisplayName}，{state}，动作：{InstalledAppResolver.GetSupportedActions(app)}）";
            })
            .ToArray();

        return enabledApps.Length == 0
            ? ToolExecutionResult.Fail("用户尚未启用任何应用。")
            : ToolExecutionResult.Ok("允许的应用：" + string.Join("、", enabledApps));
    }

    private ToolExecutionResult OpenApp(string alias)
    {
        var app = ResolveApp(alias);
        if (app is null)
        {
            return ToolExecutionResult.Fail($"应用 {alias} 不在白名单中或尚未启用。请先调用 windows.list_apps。");
        }

        var target = InstalledAppResolver.ResolveForLaunch(app);
        var processNames = InstalledAppResolver.GetProcessNames(app, target);
        var existingWindow = FindMainWindow(processNames);
        if (existingWindow != IntPtr.Zero)
        {
            var activated = TryBringWindowToFront(existingWindow, processNames);
            _log(activated
                ? $"应用已经运行，已激活窗口：{app.DisplayName}"
                : $"应用已经运行，但 Windows 未允许切换前台：{app.DisplayName}");
            return activated
                ? ToolExecutionResult.Ok($"{app.DisplayName} 已经运行，现已切换到前台。")
                : ToolExecutionResult.Ok($"{app.DisplayName} 已经运行，但 Windows 没有允许自动切到前台；请点击一次任务栏中的应用图标。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        };
        if (!InstalledAppResolver.IsProtocol(target) && Path.IsPathRooted(target))
        {
            startInfo.WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty;
        }
        Process.Start(startInfo);
        _log($"已向 Windows 请求打开应用：{app.DisplayName}");
        return ToolExecutionResult.Ok($"Windows 已接收 {app.DisplayName} 的打开请求。");
    }

    private ToolExecutionResult CloseApp(string alias)
    {
        var app = ResolveApp(alias);
        if (app is null)
        {
            return ToolExecutionResult.Fail($"应用 {alias} 不在白名单中或尚未启用。");
        }

        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        if (string.IsNullOrWhiteSpace(resolvedTarget) || InstalledAppResolver.IsProtocol(resolvedTarget))
        {
            return ToolExecutionResult.Fail("该应用使用系统协议启动，无法安全确定对应进程。请手动关闭。");
        }

        var requested = 0;
        foreach (var processName in InstalledAppResolver.GetProcessNames(app, resolvedTarget))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.CloseMainWindow())
                        {
                            requested++;
                        }
                    }
                    catch
                    {
                        // Continue checking other user-visible instances.
                    }
                }
            }
        }

        if (requested == 0)
        {
            return ToolExecutionResult.Fail($"没有找到可正常关闭的 {app.DisplayName} 窗口；程序可能未运行或权限更高。");
        }

        _log($"已请求关闭应用：{app.DisplayName}");
        return ToolExecutionResult.Ok($"已请求 {app.DisplayName} 正常关闭，共 {requested} 个窗口。");
    }

    private async Task<ToolExecutionResult> AppActionAsync(
        string alias,
        string action,
        string query,
        string recipient,
        string message,
        string text,
        CancellationToken cancellationToken)
    {
        var app = ResolveApp(alias);
        if (app is null)
        {
            return ToolExecutionResult.Fail($"应用 {alias} 不在白名单中或尚未启用。");
        }

        var normalizedAction = action.Trim().ToLowerInvariant();
        if (!InstalledAppResolver.SupportsAction(app, normalizedAction))
        {
            return ToolExecutionResult.Fail(
                $"{app.DisplayName} 不支持动作 {normalizedAction}。可用动作：{InstalledAppResolver.GetSupportedActions(app)}。");
        }
        if (normalizedAction == "search")
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200)
            {
                return ToolExecutionResult.Fail("搜索关键词不能为空且不能超过 200 个字符。");
            }
        }
        else if (normalizedAction == "send_message")
        {
            if (!app.Alias.Equals("wechat", StringComparison.OrdinalIgnoreCase)
                && !app.Alias.Equals("qq", StringComparison.OrdinalIgnoreCase))
            {
                return ToolExecutionResult.Fail("发送消息动作目前只支持微信和 QQ。");
            }
            recipient = recipient.Trim();
            message = message.Trim();
            if (recipient.Length is < 1 or > 80)
            {
                return ToolExecutionResult.Fail("联系人不能为空且不能超过 80 个字符。");
            }
            if (message.Length is < 1 or > 1000)
            {
                return ToolExecutionResult.Fail("消息不能为空且不能超过 1000 个字符。");
            }
            if (recipient.IndexOfAny(['\r', '\n']) >= 0 || message.IndexOfAny(['\r', '\n']) >= 0)
            {
                return ToolExecutionResult.Fail("为避免聊天软件把换行误当成发送键，联系人和消息目前只支持单行文字。");
            }
        }
        else if (normalizedAction is "write_text" or "new_and_write")
        {
            text = text.TrimEnd();
            if (text.Length is < 1 or > 4000)
            {
                return ToolExecutionResult.Fail("写入内容不能为空且不能超过 4000 个字符。");
            }
        }
        else if (normalizedAction is "play_pause" or "previous" or "next")
        {
            if (!_permissionEnabled(PermissionKeys.Media))
            {
                return ToolExecutionResult.Fail("媒体动作需要先在“授权管理”中开启媒体权限。");
            }
        }
        else if (normalizedAction is not "activate" and not "new_document")
        {
            return ToolExecutionResult.Fail("不支持的应用动作。");
        }

        if (normalizedAction is "search" or "send_message" or "write_text" or "new_document" or "new_and_write")
        {
            var reason = normalizedAction switch
            {
                "search" => $"在 {app.DisplayName} 中搜索",
                "send_message" => $"在 {app.DisplayName} 中选择联系人并发送消息",
                "write_text" or "new_and_write" => "在记事本中创建并写入内容",
                _ => "在记事本中新建文档"
            };
            if (!_permissionEnabled(PermissionKeys.Keyboard)
                && !await _requestInputPermission(PermissionKeys.Keyboard, reason, cancellationToken))
            {
                return ToolExecutionResult.Fail("用户未授权键盘操作，本次调用已取消。");
            }
        }

        var activation = await ActivateAppWindowAsync(app, cancellationToken);
        if (!activation.Result.Success)
        {
            return activation.Result;
        }

        if (normalizedAction == "activate")
        {
            return activation.Result;
        }

        var processNames = activation.ProcessNames;
        var handle = activation.Handle;
        if (handle == IntPtr.Zero)
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 已收到打开请求，但暂时没有可操作窗口。请等应用显示完成后重试。");
        }

        await Task.Delay(350, cancellationToken);
        if (normalizedAction == "search")
        {
            return await SearchInAppAsync(app, handle, processNames, query.Trim(), cancellationToken);
        }

        if (normalizedAction == "send_message")
        {
            return await SendChatMessageAsync(app, handle, processNames, recipient, message, cancellationToken);
        }

        if (normalizedAction == "new_document")
        {
            return await NewNotepadDocumentAsync(handle, processNames, cancellationToken);
        }

        if (normalizedAction == "write_text")
        {
            return await WriteNotepadTextAsync(handle, processNames, text, cancellationToken);
        }

        if (normalizedAction == "new_and_write")
        {
            var newResult = await NewNotepadDocumentAsync(handle, processNames, cancellationToken);
            if (!newResult.Success)
            {
                return newResult;
            }
            await Task.Delay(220, cancellationToken);
            return await WriteNotepadTextAsync(handle, processNames, text, cancellationToken);
        }

        var mediaResult = MediaControl(normalizedAction, 1);
        if (mediaResult.Success)
        {
            _log($"已对 {app.DisplayName} 执行：{normalizedAction}");
        }
        return mediaResult;
    }

    private readonly record struct AppActivation(
        ToolExecutionResult Result,
        IntPtr Handle,
        IReadOnlyList<string> ProcessNames);

    private async Task<AppActivation> ActivateAppWindowAsync(
        AppEntry app,
        CancellationToken cancellationToken)
    {
        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        var processNames = InstalledAppResolver.GetProcessNames(app, resolvedTarget);
        var handle = FindMainWindow(processNames);
        var launched = false;
        if (handle == IntPtr.Zero)
        {
            var openResult = OpenApp(app.Alias);
            if (!openResult.Success)
            {
                return new AppActivation(openResult, IntPtr.Zero, processNames);
            }
            launched = true;
            if (processNames.Count == 0)
            {
                return new AppActivation(openResult, IntPtr.Zero, processNames);
            }
            var maxAttempts = app.Alias is "netease_music" or "wechat" or "qq" ? 60 : 40;
            for (var attempt = 0; attempt < maxAttempts && handle == IntPtr.Zero; attempt++)
            {
                await Task.Delay(250, cancellationToken);
                if (attempt % 4 == 3)
                {
                    resolvedTarget = InstalledAppResolver.TryResolvePath(app);
                    processNames = InstalledAppResolver.GetProcessNames(app, resolvedTarget);
                }
                handle = FindMainWindow(processNames);
            }
        }

        if (handle == IntPtr.Zero)
        {
            var result = launched
                ? ToolExecutionResult.Ok($"Windows 已接收 {app.DisplayName} 的打开请求；应用可能已经显示，但暂时没有获得可操作窗口。")
                : ToolExecutionResult.Fail($"没有找到 {app.DisplayName} 的可操作窗口。");
            return new AppActivation(result, IntPtr.Zero, processNames);
        }

        var activated = false;
        for (var attempt = 0; attempt < 3 && !activated; attempt++)
        {
            activated = TryBringWindowToFront(handle, processNames);
            if (!activated)
            {
                await Task.Delay(180, cancellationToken);
            }
        }
        if (!activated)
        {
            return new AppActivation(
                ToolExecutionResult.Ok($"{app.DisplayName} 已打开，但 Windows 没有允许自动切到前台。需要操作时请点击一次应用窗口。"),
                handle,
                processNames);
        }
        _log($"已激活应用窗口：{app.DisplayName}");
        return new AppActivation(ToolExecutionResult.Ok($"已打开并激活 {app.DisplayName}。"), handle, processNames);
    }

    private async Task<ToolExecutionResult> SearchInAppAsync(
        AppEntry app,
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string query,
        CancellationToken cancellationToken)
    {
        if (!EnsureTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 没有保持在前台，已取消输入，避免把搜索词发到错误窗口。");
        }

        PressHotkey("esc");
        await Task.Delay(100, cancellationToken);
        var hotkeyResult = PressHotkey("ctrl+f");
        if (!hotkeyResult.Success)
        {
            return hotkeyResult;
        }

        var focusDelay = app.Alias.Equals("netease_music", StringComparison.OrdinalIgnoreCase) ? 650 : 420;
        await Task.Delay(focusDelay, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 的窗口失去前台，已停止输入，请重试。");
        }

        var selectAllResult = PressHotkey("ctrl+a");
        if (!selectAllResult.Success)
        {
            return selectAllResult;
        }
        await Task.Delay(80, cancellationToken);
        if (!_permissionEnabled(PermissionKeys.Keyboard))
        {
            return ToolExecutionResult.Fail("键盘授权已撤回，搜索已停止。");
        }
        var typeResult = TypeText(query);
        if (!typeResult.Success)
        {
            return typeResult;
        }

        if (app.Alias.Equals("netease_music", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(320, cancellationToken);
            if (!IsTargetWindowForeground(handle, processNames))
            {
                return ToolExecutionResult.Fail("网易云音乐在提交搜索前失去焦点，已取消操作。");
            }
            var enterResult = PressHotkey("enter");
            if (!enterResult.Success)
            {
                return enterResult;
            }
        }

        _log($"已在 {app.DisplayName} 中搜索：{query}");
        return ToolExecutionResult.Ok($"已在 {app.DisplayName} 中搜索：{query}。");
    }

    private async Task<ToolExecutionResult> SendChatMessageAsync(
        AppEntry app,
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string recipient,
        string message,
        CancellationToken cancellationToken)
    {
        if (!EnsureTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 没有保持在前台，已取消发送，避免消息进入错误窗口。");
        }

        PressHotkey("esc");
        await Task.Delay(120, cancellationToken);
        var searchResult = PressHotkey("ctrl+f");
        if (!searchResult.Success)
        {
            return searchResult;
        }
        var searchDelay = app.Alias.Equals("qq", StringComparison.OrdinalIgnoreCase) ? 700 : 500;
        await Task.Delay(searchDelay, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 的窗口失去前台，已取消发送。");
        }

        var selectAllResult = PressHotkey("ctrl+a");
        if (!selectAllResult.Success)
        {
            return selectAllResult;
        }
        await Task.Delay(80, cancellationToken);
        var recipientResult = TypeText(recipient);
        if (!recipientResult.Success)
        {
            return recipientResult;
        }

        var resultDelay = app.Alias.Equals("qq", StringComparison.OrdinalIgnoreCase) ? 1100 : 900;
        await Task.Delay(resultDelay, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"等待 {app.DisplayName} 联系人结果时窗口失去前台，消息未发送。");
        }
        var openConversationResult = PressHotkey("enter");
        if (!openConversationResult.Success)
        {
            return openConversationResult;
        }

        await Task.Delay(600, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"打开 {app.DisplayName} 会话后窗口失去前台，消息未发送。");
        }
        if (!_permissionEnabled(PermissionKeys.Keyboard))
        {
            return ToolExecutionResult.Fail("键盘授权已撤回，消息未输入。");
        }
        var messageResult = TypeText(message);
        if (!messageResult.Success)
        {
            return messageResult;
        }

        await Task.Delay(120, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 在发送前失去前台，消息内容已输入但没有按下发送键。");
        }
        if (!_permissionEnabled(PermissionKeys.Keyboard))
        {
            return ToolExecutionResult.Fail("键盘授权已撤回，消息内容已输入但没有按下发送键。");
        }
        var sendResult = PressHotkey("enter");
        if (!sendResult.Success)
        {
            return sendResult;
        }

        _log($"已向 {app.DisplayName} 联系人 {recipient} 执行发送（{message.Length} 个字符，内容未写入日志）。");
        return ToolExecutionResult.Ok($"已在 {app.DisplayName} 联系人“{recipient}”的会话中按下发送键。");
    }

    private async Task<ToolExecutionResult> NewNotepadDocumentAsync(
        IntPtr handle,
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken)
    {
        if (!EnsureTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail("记事本没有保持在前台，已取消新建文档。");
        }
        var result = PressHotkey("ctrl+n");
        if (!result.Success)
        {
            return result;
        }
        await Task.Delay(300, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail("记事本新建文档后失去前台，已停止后续输入。");
        }
        _log("已在记事本中新建文档。");
        return ToolExecutionResult.Ok("已在记事本中新建文档。");
    }

    private async Task<ToolExecutionResult> WriteNotepadTextAsync(
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string text,
        CancellationToken cancellationToken)
    {
        if (!EnsureTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail("记事本没有保持在前台，已取消写入，避免内容进入错误窗口。");
        }
        var escapeResult = PressHotkey("esc");
        if (!escapeResult.Success)
        {
            return escapeResult;
        }
        await Task.Delay(180, cancellationToken);
        if (!IsTargetWindowForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail("记事本在写入前失去前台，内容未写入。");
        }
        if (!_permissionEnabled(PermissionKeys.Keyboard))
        {
            return ToolExecutionResult.Fail("键盘授权已撤回，内容未写入。");
        }
        var result = TypeText(text);
        if (!result.Success)
        {
            return result;
        }
        _log($"已向记事本写入 {text.Length} 个字符（内容未写入日志）。");
        return ToolExecutionResult.Ok($"已向记事本写入 {text.Length} 个字符。");
    }

    private static IntPtr FindMainWindow(IReadOnlyList<string> processNames)
    {
        var processIds = new HashSet<uint>();
        var candidates = new List<(IntPtr Handle, long Score)>();
        foreach (var processName in processNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        process.Refresh();
                        processIds.Add((uint)process.Id);
                        if (process.MainWindowHandle != IntPtr.Zero
                            && IsWindowVisible(process.MainWindowHandle)
                            && GetWindowRect(process.MainWindowHandle, out var mainRect))
                        {
                            var mainWidth = Math.Max(0, mainRect.Right - mainRect.Left);
                            var mainHeight = Math.Max(0, mainRect.Bottom - mainRect.Top);
                            candidates.Add((process.MainWindowHandle, 2_000_000_000L + (long)mainWidth * mainHeight));
                        }
                    }
                    catch
                    {
                        // Continue checking other instances.
                    }
                }
            }
        }

        if (processIds.Count > 0)
        {
            EnumWindows((windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle))
                {
                    return true;
                }
                GetWindowThreadProcessId(windowHandle, out var processId);
                if (!processIds.Contains(processId) || !GetWindowRect(windowHandle, out var rect))
                {
                    return true;
                }
                var width = Math.Max(0, rect.Right - rect.Left);
                var height = Math.Max(0, rect.Bottom - rect.Top);
                if (width < 160 || height < 100)
                {
                    return true;
                }
                var titleBonus = GetWindowTextLength(windowHandle) > 0 ? 1_000_000_000L : 0;
                candidates.Add((windowHandle, titleBonus + (long)width * height));
                return true;
            }, IntPtr.Zero);
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Handle)
            .FirstOrDefault();
    }

    private bool EnsureTargetIsForeground(IntPtr handle, IReadOnlyList<string> processNames)
    {
        return IsTargetWindowForeground(handle, processNames) || TryBringWindowToFront(handle, processNames);
    }

    private static bool IsTargetWindowForeground(IntPtr handle, IReadOnlyList<string> processNames)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }
        if (foreground == handle)
        {
            return true;
        }
        var foregroundRoot = GetAncestor(foreground, GetAncestorRootOwner);
        var targetRoot = GetAncestor(handle, GetAncestorRootOwner);
        if (foregroundRoot != IntPtr.Zero && foregroundRoot == targetRoot)
        {
            return true;
        }
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0)
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool TryBringWindowToFront(IntPtr handle, IReadOnlyList<string> processNames)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return false;
        }

        if (IsIconic(handle))
        {
            ShowWindowAsync(handle, 9);
        }
        else
        {
            ShowWindowAsync(handle, 5);
        }

        // Tool calls arrive on a WebSocket worker thread. AttachThreadInput fails
        // when that thread has no Win32 message queue, so create the queue before
        // trying to transfer foreground ownership.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        if (_permissionEnabled(PermissionKeys.Keyboard))
        {
            PulseAltKey();
        }

        var currentThread = GetCurrentThreadId();
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(handle, out _);
        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }
            if (targetThread != 0 && targetThread != currentThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            SetActiveWindow(handle);
            SetFocus(handle);
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        Thread.Sleep(140);
        if (IsTargetWindowForeground(handle, processNames))
        {
            return true;
        }

        // Some Windows builds ignore the first foreground request while another
        // application is processing activation. A second request after the input
        // queues are detached is safe and fixes that transient race.
        BringWindowToTop(handle);
        SetForegroundWindow(handle);
        Thread.Sleep(120);
        return IsTargetWindowForeground(handle, processNames);
    }

    private static void PulseAltKey()
    {
        if (!IsNativeInputLayoutValid)
        {
            return;
        }

        var inputs = new[]
        {
            CreateVirtualKeyInput(0x12, false),
            CreateVirtualKeyInput(0x12, true)
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private ToolExecutionResult DiagnoseApps()
    {
        return ToolExecutionResult.Ok(InstalledAppResolver.BuildDiagnosticReport(_getApps()));
    }

    public ToolExecutionResult ExportDiagnosticReport()
    {
        try
        {
            Directory.CreateDirectory(_settingsStore.DiagnosticsDirectory);
            var path = Path.Combine(
                _settingsStore.DiagnosticsDirectory,
                $"app-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var report = string.Join(
                Environment.NewLine,
                $"键盘输入层级：{(WindowsInputAccess.IsElevated ? "管理员模式" : "普通模式")}",
                $"键盘输入结构：{Marshal.SizeOf<Input>()} 字节（预期 {(IntPtr.Size == 8 ? ExpectedInputSizeX64 : ExpectedInputSizeX86)} 字节）",
                $"键鼠输入组件：{(IsNativeInputEngineValid ? "正常" : "异常")}",
                string.Empty,
                InstalledAppResolver.BuildDiagnosticReport(_getApps()));
            File.WriteAllText(path, report);
            _log($"应用诊断报告已导出：{path}");
            return ToolExecutionResult.Ok($"诊断报告已保存到：{path}");
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"导出诊断报告失败：{exception.Message}");
        }
    }

    private static ToolExecutionResult OpenUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ToolExecutionResult.Fail("只允许打开完整的 http 或 https 网页地址。");
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return ToolExecutionResult.Ok($"已在默认浏览器打开：{uri.Host}");
    }

    private static ToolExecutionResult WebSearch(string query, string engine)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolExecutionResult.Fail("搜索内容不能为空。");
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var url = engine.Trim().ToLowerInvariant() switch
        {
            "bing" => $"https://www.bing.com/search?q={encodedQuery}",
            "google" => $"https://www.google.com/search?q={encodedQuery}",
            _ => $"https://www.baidu.com/s?wd={encodedQuery}"
        };
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return ToolExecutionResult.Ok($"已把“{query.Trim()}”交给默认浏览器搜索。");
    }

    private ToolExecutionResult TypeText(string text)
    {
        if (text.Length == 0)
        {
            return ToolExecutionResult.Fail("输入文字不能为空。");
        }

        if (text.Length > 4000)
        {
            return ToolExecutionResult.Fail("单次输入不能超过 4000 个字符。");
        }

        var pendingInputs = new List<Input>(128);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
                pendingInputs.Add(CreateVirtualKeyInput(0x0D, false));
                pendingInputs.Add(CreateVirtualKeyInput(0x0D, true));
            }
            else
            {
                pendingInputs.Add(CreateUnicodeInput(character, false));
                pendingInputs.Add(CreateUnicodeInput(character, true));
            }

            if (pendingInputs.Count >= 120)
            {
                var batchResult = SendKeyboardInputs(pendingInputs, "文字输入");
                if (!batchResult.Success)
                {
                    return batchResult;
                }
                pendingInputs.Clear();
            }
        }

        if (pendingInputs.Count > 0)
        {
            var batchResult = SendKeyboardInputs(pendingInputs, "文字输入");
            if (!batchResult.Success)
            {
                return batchResult;
            }
        }

        return ToolExecutionResult.Ok($"已输入 {text.Length} 个字符。");
    }

    private static ToolExecutionResult GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? ToolExecutionResult.Ok($"当前鼠标位置：x={point.X}, y={point.Y}。")
            : ToolExecutionResult.Fail("Windows 未能返回当前鼠标位置。");
    }

    private ToolExecutionResult PressHotkey(string keys)
    {
        var parts = keys
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .ToArray();
        if (parts.Length == 0 || parts.Length > 5)
        {
            return ToolExecutionResult.Fail("快捷键格式不正确，例如 ctrl+l 或 ctrl+shift+s。");
        }

        var virtualKeys = new List<byte>();
        foreach (var part in parts)
        {
            if (!TryResolveVirtualKey(part, out var virtualKey))
            {
                return ToolExecutionResult.Fail($"不支持的按键：{part}");
            }
            virtualKeys.Add(virtualKey);
        }

        var inputs = new List<Input>(virtualKeys.Count * 2);
        foreach (var virtualKey in virtualKeys)
        {
            inputs.Add(CreateVirtualKeyInput(virtualKey, false));
        }
        for (var index = virtualKeys.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateVirtualKeyInput(virtualKeys[index], true));
        }

        var result = SendKeyboardInputs(inputs, $"快捷键 {string.Join('+', parts)}");
        return result.Success
            ? ToolExecutionResult.Ok($"已按下快捷键：{string.Join('+', parts)}")
            : result;
    }

    private ToolExecutionResult SendKeyboardInputs(IReadOnlyCollection<Input> inputs, string operation)
    {
        return SendNativeInputs(inputs, operation, "键盘");
    }

    private ToolExecutionResult SendNativeInputs(
        IReadOnlyCollection<Input> inputs,
        string operation,
        string inputKind)
    {
        var inputSize = Marshal.SizeOf<Input>();
        var expectedInputSize = IntPtr.Size == 8 ? ExpectedInputSizeX64 : ExpectedInputSizeX86;
        if (!IsNativeInputLayoutValid)
        {
            _log($"{inputKind}输入组件尺寸异常：实际 {inputSize}，预期 {expectedInputSize}。");
            return ToolExecutionResult.Fail($"{inputKind}输入组件异常，请安装最新版路遥智控后重试。");
        }

        var inputArray = inputs as Input[] ?? inputs.ToArray();
        if (inputArray.Length == 0)
        {
            return ToolExecutionResult.Fail($"{operation}没有可发送的输入事件。");
        }

        Marshal.SetLastPInvokeError(0);
        var sent = SendInput((uint)inputArray.Length, inputArray, inputSize);
        if (sent == (uint)inputArray.Length)
        {
            return ToolExecutionResult.Ok($"已完成{operation}。");
        }

        var errorCode = Marshal.GetLastWin32Error();
        _log(
            $"Windows 未完成{inputKind}输入：{operation}，已发送 {sent}/{inputArray.Length}，"
            + $"错误码 {errorCode}，输入尺寸 {inputSize}，管理员模式 {WindowsInputAccess.IsElevated}。");
        return WindowsInputAccess.IsElevated
            ? ToolExecutionResult.Fail(
                $"Windows 或目标应用阻止了{operation}（已发送 {sent}/{inputArray.Length}，错误码 {errorCode}）。UAC、安全软件和受保护窗口无法自动操作。")
            : ToolExecutionResult.Fail(
                $"目标窗口阻止了{operation}（已发送 {sent}/{inputArray.Length}，错误码 {errorCode}）。如果目标应用以管理员身份运行，请在路遥智控“授权管理”中点击“管理员模式重启”。");
    }

    private ToolExecutionResult MoveMouse(int x, int y)
    {
        var screen = SystemInformation.VirtualScreen;
        if (x < screen.Left || x >= screen.Right || y < screen.Top || y >= screen.Bottom)
        {
            return ToolExecutionResult.Fail(
                $"坐标超出屏幕范围。当前范围：x={screen.Left}..{screen.Right - 1}, y={screen.Top}..{screen.Bottom - 1}");
        }

        if (screen.Width <= 1 || screen.Height <= 1)
        {
            return ToolExecutionResult.Fail("Windows 返回的虚拟屏幕尺寸无效，无法移动鼠标。");
        }

        var normalizedX = (int)Math.Round((x - screen.Left) * 65535d / (screen.Width - 1));
        var normalizedY = (int)Math.Round((y - screen.Top) * 65535d / (screen.Height - 1));
        var input = CreateMouseInput(
            MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
            normalizedX,
            normalizedY);
        var sendResult = SendNativeInputs([input], "鼠标移动", "鼠标");
        if (!sendResult.Success)
        {
            return sendResult;
        }

        Thread.Sleep(25);
        if (!GetCursorPos(out var actual))
        {
            return ToolExecutionResult.Fail("Windows 已接收鼠标移动事件，但无法读取移动后的坐标。");
        }
        if (Math.Abs(actual.X - x) > 2 || Math.Abs(actual.Y - y) > 2)
        {
            _log($"鼠标移动校验失败：目标 ({x}, {y})，实际 ({actual.X}, {actual.Y})。");
            return ToolExecutionResult.Fail(
                $"鼠标没有到达目标位置。目标 ({x}, {y})，实际 ({actual.X}, {actual.Y})。远程桌面、锁屏或安全软件可能阻止了输入。");
        }

        return ToolExecutionResult.Ok($"鼠标已移动到 ({actual.X}, {actual.Y})。");
    }

    private ToolExecutionResult Click(JsonElement arguments)
    {
        var hasX = TryGetInt(arguments, "x", out var x);
        var hasY = TryGetInt(arguments, "y", out var y);
        if (hasX != hasY)
        {
            return ToolExecutionResult.Fail("点击坐标必须同时提供 x 和 y。");
        }

        if (hasX)
        {
            var moveResult = MoveMouse(x, y);
            if (!moveResult.Success)
            {
                return moveResult;
            }
        }

        return SendMouseButton(
            OptionalString(arguments, "button", "left").ToLowerInvariant(),
            Math.Clamp(OptionalInt(arguments, "clicks", 1), 1, 2));
    }

    private ToolExecutionResult SendMouseButton(string button, int clicks)
    {
        var flags = button switch
        {
            "left" => (MouseEventLeftDown, MouseEventLeftUp),
            "right" => (MouseEventRightDown, MouseEventRightUp),
            "middle" => (MouseEventMiddleDown, MouseEventMiddleUp),
            _ => (0u, 0u)
        };
        if (flags.Item1 == 0)
        {
            return ToolExecutionResult.Fail("鼠标按键只支持 left、right 或 middle。");
        }

        for (var index = 0; index < clicks; index++)
        {
            var sendResult = SendNativeInputs(
                [CreateMouseInput(flags.Item1), CreateMouseInput(flags.Item2)],
                $"鼠标{button}键{(clicks == 2 ? "双击" : "单击")}",
                "鼠标");
            if (!sendResult.Success)
            {
                return sendResult;
            }
            if (clicks == 2 && index == 0)
            {
                Thread.Sleep(80);
            }
        }

        return ToolExecutionResult.Ok($"已{(clicks == 2 ? "双击" : "单击")}{button}键。");
    }

    private ToolExecutionResult Scroll(int amount)
    {
        if (amount is < -20 or > 20 || amount == 0)
        {
            return ToolExecutionResult.Fail("滚动量必须是 -20 到 20 之间的非零整数。");
        }

        var result = SendNativeInputs(
            [CreateMouseInput(MouseEventWheel, mouseData: unchecked((uint)(amount * 120)))],
            $"鼠标滚动 {amount} 格",
            "鼠标");
        return result.Success
            ? ToolExecutionResult.Ok($"已滚动 {amount} 格。")
            : result;
    }

    private ToolExecutionResult MediaControl(string action, int steps)
    {
        const byte volumeUp = 0xAF;
        const byte volumeDown = 0xAE;
        const byte volumeMute = 0xAD;
        const byte mediaPlayPause = 0xB3;
        const byte mediaPrevious = 0xB1;
        const byte mediaNext = 0xB0;

        var normalizedAction = action.Trim().ToLowerInvariant();
        var virtualKey = normalizedAction switch
        {
            "volume_up" => volumeUp,
            "volume_down" => volumeDown,
            "mute" => volumeMute,
            "play_pause" => mediaPlayPause,
            "previous" => mediaPrevious,
            "next" => mediaNext,
            _ => (byte)0
        };
        if (virtualKey == 0)
        {
            return ToolExecutionResult.Fail("不支持的媒体动作。");
        }

        var repeat = normalizedAction is "volume_up" or "volume_down" ? Math.Clamp(steps, 1, 10) : 1;
        for (var index = 0; index < repeat; index++)
        {
            var result = SendKeyboardInputs(
                [CreateVirtualKeyInput(virtualKey, false), CreateVirtualKeyInput(virtualKey, true)],
                $"媒体操作 {normalizedAction}");
            if (!result.Success)
            {
                return result;
            }
        }

        return ToolExecutionResult.Ok($"已执行媒体操作：{normalizedAction}。");
    }

    private ToolExecutionResult TakeScreenshot()
    {
        Directory.CreateDirectory(_settingsStore.ScreenshotDirectory);
        var bounds = SystemInformation.VirtualScreen;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        var path = Path.Combine(
            _settingsStore.ScreenshotDirectory,
            $"screen-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        bitmap.Save(path, ImageFormat.Png);
        _log($"屏幕截图已保存：{path}");
        return ToolExecutionResult.Ok($"截图已保存到：{path}");
    }

    private AppEntry? ResolveApp(string alias)
    {
        return _getApps().FirstOrDefault(app =>
            app.Enabled && app.Alias.Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string RequiredString(JsonElement arguments, string property)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"缺少必填参数：{property}");
        }
        return value.GetString()!.Trim();
    }

    private static string OptionalString(JsonElement arguments, string property, string defaultValue)
    {
        return arguments.ValueKind == JsonValueKind.Object
               && arguments.TryGetProperty(property, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private static int RequiredInt(JsonElement arguments, string property)
    {
        if (!TryGetInt(arguments, property, out var value))
        {
            throw new ArgumentException($"缺少或无法读取整数参数：{property}");
        }
        return value;
    }

    private static int OptionalInt(JsonElement arguments, string property, int defaultValue)
    {
        return TryGetInt(arguments, property, out var value) ? value : defaultValue;
    }

    private static bool TryGetInt(JsonElement arguments, string property, out int value)
    {
        value = 0;
        return arguments.ValueKind == JsonValueKind.Object
               && arguments.TryGetProperty(property, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value);
    }

    private static Input CreateUnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = character,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    private static Input CreateVirtualKeyInput(byte virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = 0,
                Flags = keyUp ? KeyEventKeyUp : 0,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    private static Input CreateMouseInput(
        uint flags,
        int x = 0,
        int y = 0,
        uint mouseData = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput
            {
                X = x,
                Y = y,
                MouseData = mouseData,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    private static bool TryResolveVirtualKey(string key, out byte virtualKey)
    {
        var namedKeys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = 0x11,
            ["control"] = 0x11,
            ["shift"] = 0x10,
            ["alt"] = 0x12,
            ["win"] = 0x5B,
            ["enter"] = 0x0D,
            ["tab"] = 0x09,
            ["esc"] = 0x1B,
            ["escape"] = 0x1B,
            ["space"] = 0x20,
            ["backspace"] = 0x08,
            ["delete"] = 0x2E,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["pageup"] = 0x21,
            ["pagedown"] = 0x22,
            ["left"] = 0x25,
            ["up"] = 0x26,
            ["right"] = 0x27,
            ["down"] = 0x28
        };
        if (namedKeys.TryGetValue(key, out virtualKey))
        {
            return true;
        }

        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            virtualKey = (byte)char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length is 2 or 3 && key[0] == 'f' && int.TryParse(key[1..], out var functionKey)
            && functionKey is >= 1 and <= 12)
        {
            virtualKey = (byte)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        // INPUT is a native union. Including its largest member is required so
        // Marshal.SizeOf<Input>() is 40 bytes on x64 (28 on x86). A keyboard-only
        // union is too small and makes SendInput fail with ERROR_INVALID_PARAMETER.
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public CursorPoint Point;
        public uint Private;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint filterMinimum,
        uint filterMaximum,
        uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);
}
