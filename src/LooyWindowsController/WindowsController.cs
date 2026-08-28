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
    private readonly Func<string, string, CancellationToken, Task<bool>> _requestPermission;
    private readonly Func<IReadOnlyList<AppEntry>> _getApps;
    private readonly SettingsStore _settingsStore;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private ScreenSnapshot? _screenSnapshot;
    private PendingChatSend? _pendingChatSend;
    private PendingPowerAction? _pendingPowerAction;

    private sealed record PendingChatSend(
        string ConfirmationId,
        string AppAlias,
        string AppDisplayName,
        string Recipient,
        string Message,
        IntPtr WindowHandle,
        IReadOnlyList<string> ProcessNames,
        Rectangle WindowBounds,
        Rectangle HeaderBounds,
        Rectangle MessageBounds,
        DateTimeOffset CreatedAt);

    private sealed record PendingPowerAction(
        string ConfirmationId,
        string Action,
        int DelaySeconds,
        DateTimeOffset CreatedAt);

    private sealed record VerifiedSearchInput(
        ScreenSnapshot Snapshot,
        ScreenTextItem OriginalField,
        ScreenTextItem TypedQuery);

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

    internal void ClearTransientState()
    {
        _screenSnapshot = null;
        _pendingChatSend = null;
        _pendingPowerAction = null;
    }

    public WindowsController(
        Func<string, bool> permissionEnabled,
        Func<string, string, CancellationToken, Task<bool>> requestPermission,
        Func<IReadOnlyList<AppEntry>> getApps,
        SettingsStore settingsStore,
        Action<string> log)
    {
        _permissionEnabled = permissionEnabled;
        _requestPermission = requestPermission;
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
                "windows.resource_status" => RequirePermission(
                    PermissionKeys.SystemStatus,
                    WindowsSystemTools.GetResourceStatus),
                "windows.read_clipboard_text" => RequirePermission(
                    PermissionKeys.Clipboard,
                    WindowsSystemTools.ReadClipboardText),
                "windows.list_apps" => RequirePermission(PermissionKeys.Applications, ListApps),
                "windows.open_app" => RequirePermission(
                    PermissionKeys.Applications,
                    () => OpenApp(RequiredString(arguments, "app"))),
                "windows.close_app" => RequirePermission(
                    PermissionKeys.Applications,
                    () => CloseApp(RequiredString(arguments, "app"))),
                "windows.netease_music_task" => !_permissionEnabled(PermissionKeys.Applications)
                    ? ToolExecutionResult.Fail("用户尚未授权应用操作。")
                    : await NeteaseMusicTaskAsync(
                        RequiredString(arguments, "action"),
                        OptionalString(arguments, "query", string.Empty),
                        OptionalInt(arguments, "result_number", 1),
                        cancellationToken),
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
                "windows.prepare_chat_message" => !_permissionEnabled(PermissionKeys.Applications)
                    ? ToolExecutionResult.Fail("用户尚未在路遥智控中授权应用操作。")
                    : await AppActionAsync(
                        RequiredString(arguments, "app"),
                        "send_message",
                        string.Empty,
                        RequiredString(arguments, "recipient"),
                        RequiredString(arguments, "message"),
                        string.Empty,
                        cancellationToken),
                "windows.confirm_chat_send" => !_permissionEnabled(PermissionKeys.Applications)
                    ? ToolExecutionResult.Fail("用户尚未在路遥智控中授权应用操作。")
                    : await ConfirmChatSendAsync(
                        RequiredString(arguments, "confirmation_id"),
                        cancellationToken),
                "windows.diagnose_apps" => RequirePermission(PermissionKeys.Applications, DiagnoseApps),
                "windows.open_url" => RequirePermission(
                    PermissionKeys.Web,
                    () => OpenUrl(RequiredString(arguments, "url"))),
                "windows.web_search" => RequirePermission(
                    PermissionKeys.Web,
                    () => WebSearch(
                        RequiredString(arguments, "query"),
                        OptionalString(arguments, "engine", "baidu"),
                        OptionalBool(arguments, "force_browser", false))),
                "windows.verified_screen_search" => await VerifiedScreenSearchAsync(
                    RequiredString(arguments, "query"),
                    cancellationToken),
                "windows.find_text" => await VerifiedScreenSearchAsync(
                    RequiredString(arguments, "query"),
                    cancellationToken),
                "windows.show_desktop" => await ShowDesktopAsync(cancellationToken),
                "windows.presentation_control" => await PresentationControlAsync(
                    RequiredString(arguments, "action"),
                    cancellationToken),
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
                "windows.inspect_screen" => await InspectScreenAsync(
                    OptionalInt(arguments, "max_items", 60),
                    cancellationToken),
                "windows.open_screen_text" => await OpenScreenTextAsync(
                    RequiredString(arguments, "text"),
                    OptionalInt(arguments, "occurrence", 0),
                    OptionalInt(arguments, "clicks", 0),
                    cancellationToken),
                "windows.click_screen_item" => await ClickScreenItemAsync(
                    RequiredString(arguments, "snapshot_id"),
                    RequiredInt(arguments, "index"),
                    Math.Clamp(OptionalInt(arguments, "clicks", 1), 1, 2),
                    cancellationToken),
                "windows.media_control" => RequirePermission(
                    PermissionKeys.Media,
                    () => MediaControl(
                        RequiredString(arguments, "action"),
                        OptionalInt(arguments, "steps", 2),
                        OptionalInt(arguments, "level", -1))),
                "windows.system_control" => RequirePermission(
                    PermissionKeys.SystemControl,
                    () => SystemControl(
                        RequiredString(arguments, "action"),
                        OptionalString(arguments, "path", string.Empty))),
                "windows.prepare_power_action" => RequirePermission(
                    PermissionKeys.SystemControl,
                    () => PreparePowerAction(
                        RequiredString(arguments, "action"),
                        OptionalInt(arguments, "delay_seconds", 60))),
                "windows.confirm_power_action" => RequirePermission(
                    PermissionKeys.SystemControl,
                    () => ConfirmPowerAction(RequiredString(arguments, "confirmation_id"))),
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
            && !await _requestPermission(permission, reason, cancellationToken))
        {
            return ToolExecutionResult.Fail("用户没有授权本次键盘或鼠标操作，操作已取消。");
        }
        if (!_permissionEnabled(permission))
        {
            return ToolExecutionResult.Fail("键盘或鼠标授权当前不可用，操作已取消。");
        }
        return action();
    }

    private async Task<ToolExecutionResult?> EnsureAutomationPermissionAsync(
        string permission,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_permissionEnabled(permission))
        {
            return null;
        }
        if (!await _requestPermission(permission, reason, cancellationToken)
            || !_permissionEnabled(permission))
        {
            return ToolExecutionResult.Fail($"用户没有授权“{reason}”，本次操作已取消。");
        }
        return null;
    }

    private Task<ToolExecutionResult> ShowDesktopAsync(CancellationToken cancellationToken)
    {
        return RequireInputPermissionAsync(
            PermissionKeys.Keyboard,
            "使用 Windows 快捷键显示桌面",
            () => PressHotkey("win+d"),
            cancellationToken);
    }

    private async Task<ToolExecutionResult> PresentationControlAsync(
        string action,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        var hotkey = normalizedAction switch
        {
            "previous" => "left",
            "next" => "right",
            "end" => "esc",
            "start_current" => "shift+f5",
            "start_beginning" => "f5",
            _ => string.Empty
        };
        if (hotkey.Length == 0)
        {
            return ToolExecutionResult.Fail("演示动作只支持 previous、next、end、start_current 或 start_beginning。");
        }

        var targetHandle = ScreenRecognitionService.GetForegroundTargetWindow();
        if (targetHandle == IntPtr.Zero || !ScreenRecognitionService.IsWindowAvailable(targetHandle))
        {
            return ToolExecutionResult.Fail("没有找到可控制的前台演示窗口。");
        }
        var processName = ScreenRecognitionService.GetProcessName(targetHandle);
        if (!IsPresentationProcess(processName))
        {
            return ToolExecutionResult.Fail(
                $"当前前台程序“{(string.IsNullOrWhiteSpace(processName) ? "未知" : processName)}”不是已识别的 PowerPoint/WPS 演示窗口，已停止发送快捷键。");
        }

        var denied = await EnsureAutomationPermissionAsync(
            PermissionKeys.Keyboard,
            $"控制前台演示：{normalizedAction}",
            cancellationToken);
        if (denied is { } failure)
        {
            return failure;
        }

        var processNames = new[] { processName };
        if (!EnsureExactTargetIsForeground(targetHandle, processNames))
        {
            return ToolExecutionResult.Fail("授权窗口关闭后无法安全恢复原演示窗口，请手动切回后重试。");
        }
        var result = PressHotkey(hotkey);
        return result.Success
            ? ToolExecutionResult.Ok($"已在 {processName} 执行演示动作：{normalizedAction}。")
            : result;
    }

    private static bool IsPresentationProcess(string processName)
    {
        return processName.Equals("powerpnt", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("wpp", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("wps", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("presentationhost", StringComparison.OrdinalIgnoreCase);
    }

    private ToolExecutionResult SystemControl(string action, string path)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "theme_light" => WindowsSystemTools.SetTheme(dark: false),
            "theme_dark" => WindowsSystemTools.SetTheme(dark: true),
            "set_wallpaper" when !string.IsNullOrWhiteSpace(path) => WindowsSystemTools.SetWallpaper(path),
            "set_wallpaper" => ToolExecutionResult.Fail("更换壁纸必须提供本机图片的绝对路径。"),
            "cancel_power_action" => WindowsSystemTools.CancelPendingPowerAction(),
            _ => ToolExecutionResult.Fail(
                "系统设置动作只支持 theme_light、theme_dark、set_wallpaper 或 cancel_power_action。")
        };
    }

    private ToolExecutionResult PreparePowerAction(string action, int delaySeconds)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction is not "lock" and not "shutdown" and not "restart")
        {
            return ToolExecutionResult.Fail("电源动作只支持 lock、shutdown 或 restart。");
        }
        if (delaySeconds is < 0 or > 3600)
        {
            return ToolExecutionResult.Fail("延迟时间必须是 0 到 3600 秒。");
        }
        if (normalizedAction == "lock")
        {
            delaySeconds = 0;
        }

        var confirmationId = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        _pendingPowerAction = new PendingPowerAction(
            confirmationId,
            normalizedAction,
            delaySeconds,
            DateTimeOffset.Now);
        var description = normalizedAction switch
        {
            "lock" => "锁定电脑",
            "shutdown" => $"在 {delaySeconds} 秒后关机",
            _ => $"在 {delaySeconds} 秒后重启"
        };
        _log($"已准备系统电源操作：{description}；尚未执行，确认编号 {confirmationId}。");
        return ToolExecutionResult.Ok(
            $"已准备“{description}”，尚未执行。确认编号：{confirmationId}。请向用户复述该操作；只有用户在后续消息中单独明确确认后，才能调用 windows.confirm_power_action。编号两分钟有效。");
    }

    private ToolExecutionResult ConfirmPowerAction(string confirmationId)
    {
        var pending = _pendingPowerAction;
        if (pending is null)
        {
            return ToolExecutionResult.Fail("当前没有待确认的电源操作。请先调用 windows.prepare_power_action。");
        }
        if (!pending.ConfirmationId.Equals(confirmationId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Fail("确认编号不匹配，电源操作没有执行。");
        }
        if (DateTimeOffset.Now - pending.CreatedAt > TimeSpan.FromMinutes(2))
        {
            _pendingPowerAction = null;
            return ToolExecutionResult.Fail("确认编号已超过两分钟并失效，请重新准备电源操作。");
        }

        // Consume before the operating-system call so retries cannot repeat a
        // lock, shutdown, or restart after an ambiguous transport failure.
        _pendingPowerAction = null;
        var result = WindowsSystemTools.ExecutePowerAction(pending.Action, pending.DelaySeconds);
        _log(result.Success
            ? $"已执行用户二次确认的电源操作：{pending.Action}。"
            : $"电源操作执行失败：{result.Message}");
        return result;
    }

    private async Task<ToolExecutionResult> VerifiedScreenSearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 200 || query.IndexOfAny(['\r', '\n']) >= 0)
        {
            return ToolExecutionResult.Fail("搜索关键词必须是 1 到 200 个字符的单行文字。");
        }

        var targetHandle = ScreenRecognitionService.GetForegroundTargetWindow();
        if (targetHandle == IntPtr.Zero || !ScreenRecognitionService.IsWindowAvailable(targetHandle))
        {
            return ToolExecutionResult.Fail("没有找到可操作的前台窗口。请先打开包含搜索框的应用或网页。");
        }
        if (ScreenRecognitionService.IsOwnedByCurrentProcess(targetHandle))
        {
            return ToolExecutionResult.Fail("当前前台是路遥智控窗口。请先切回需要搜索的应用或网页。");
        }

        foreach (var request in new[]
                 {
                     (PermissionKeys.Keyboard, "向搜索框输入并提交搜索词"),
                     (PermissionKeys.ScreenRecognition, "识别并核对搜索框中的文字"),
                     (PermissionKeys.Mouse, "聚焦搜索框或点击识别到的搜索按钮")
                 })
        {
            var denied = await EnsureAutomationPermissionAsync(
                request.Item1,
                request.Item2,
                cancellationToken);
            if (denied is { } failure)
            {
                return failure;
            }
        }

        var processName = ScreenRecognitionService.GetProcessName(targetHandle);
        IReadOnlyList<string> processNames = string.IsNullOrWhiteSpace(processName)
            ? Array.Empty<string>()
            : new[] { processName };
        if (!EnsureExactTargetIsForeground(targetHandle, processNames))
        {
            return ToolExecutionResult.Fail("授权窗口关闭后无法安全恢复原搜索窗口。请手动切回后重试。");
        }

        return await PerformVerifiedSearchAsync(
            string.IsNullOrWhiteSpace(processName) ? "当前窗口" : processName,
            targetHandle,
            processNames,
            query,
            cancellationToken);
    }

    private async Task<ToolExecutionResult> PerformVerifiedSearchAsync(
        string displayName,
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string query,
        CancellationToken cancellationToken)
    {
        var filled = await FillAndVerifySearchBoxAsync(
            displayName,
            handle,
            processNames,
            query,
            cancellationToken);
        if (!filled.Result.Success || filled.Input is null)
        {
            return filled.Result;
        }

        var input = filled.Input;
        var submit = ScreenAutomationHeuristics.FindSearchSubmitButton(
            input.Snapshot,
            input.TypedQuery,
            input.OriginalField.Bounds);
        if (submit is not null)
        {
            var clickResult = await ClickRecognizedItemAsync(
                input.Snapshot,
                submit,
                1,
                cancellationToken);
            if (!clickResult.Success)
            {
                return ToolExecutionResult.Fail(
                    $"已核对搜索词“{query}”，但点击识别到的搜索按钮时停止：{clickResult.Message}");
            }

            _log($"已在 {displayName} 核对搜索词后点击屏幕识别到的搜索按钮。");
            return ToolExecutionResult.Ok(
                $"已在 {displayName} 输入并核对“{query}”，屏幕检测到独立搜索按钮，已用鼠标单击提交。");
        }

        if (!EnsureExactTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{displayName} 在提交搜索前失去前台，已取消操作。");
        }
        var enterResult = PressHotkey("enter");
        if (!enterResult.Success)
        {
            return enterResult;
        }

        _log($"已在 {displayName} 核对搜索词；屏幕未发现唯一的独立搜索按钮，使用回车提交。");
        return ToolExecutionResult.Ok(
            $"已在 {displayName} 输入并核对“{query}”；屏幕未发现唯一的独立搜索按钮，已在搜索框中按回车提交。");
    }

    private async Task<(ToolExecutionResult Result, VerifiedSearchInput? Input)> FillAndVerifySearchBoxAsync(
        string displayName,
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string query,
        CancellationToken cancellationToken)
    {
        if (!EnsureExactTargetIsForeground(handle, processNames))
        {
            return (ToolExecutionResult.Fail($"{displayName} 没有保持在前台，已停止搜索。"), null);
        }

        await Task.Delay(180, cancellationToken);
        var initial = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
        var searchField = ScreenAutomationHeuristics.FindSearchField(initial);
        if (searchField is null)
        {
            // Ctrl+F only requests that the target application reveal/focus its
            // search UI. No search is submitted before the text is verified.
            var revealResult = PressHotkey("ctrl+f");
            if (!revealResult.Success)
            {
                return (revealResult, null);
            }
            await Task.Delay(420, cancellationToken);
            if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
            {
                return (ToolExecutionResult.Fail($"{displayName} 在显示搜索框时失去前台，已停止输入。"), null);
            }
            initial = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
            searchField = ScreenAutomationHeuristics.FindSearchField(initial);
        }
        if (searchField is null)
        {
            return (ToolExecutionResult.Fail(
                $"屏幕上没有识别到 {displayName} 的搜索框。为避免把文字输入错误位置，没有继续操作。"), null);
        }

        var focusResult = await ClickRecognizedItemAsync(initial, searchField, 1, cancellationToken);
        if (!focusResult.Success)
        {
            return (ToolExecutionResult.Fail($"无法安全聚焦 {displayName} 的搜索框：{focusResult.Message}"), null);
        }
        await Task.Delay(120, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return (ToolExecutionResult.Fail($"{displayName} 的搜索框聚焦后窗口发生变化，已停止输入。"), null);
        }

        var selectAllResult = PressHotkey("ctrl+a");
        if (!selectAllResult.Success)
        {
            return (selectAllResult, null);
        }
        await Task.Delay(60, cancellationToken);
        var typeResult = TypeText(query);
        if (!typeResult.Success)
        {
            return (typeResult, null);
        }

        await Task.Delay(360, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return (ToolExecutionResult.Fail(
                $"{displayName} 在核对搜索词前失去前台；文字可能已输入，但没有提交搜索。"), null);
        }
        var verification = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
        var typedQuery = ScreenAutomationHeuristics.FindTypedSearchText(
            verification,
            query,
            searchField.Bounds);
        if (typedQuery is null)
        {
            return (ToolExecutionResult.Fail(
                $"已经尝试输入“{query}”，但屏幕识别没有在原搜索框位置核对到相同文字，因此没有点击搜索或按回车。"), null);
        }

        return (
            ToolExecutionResult.Ok($"已在 {displayName} 的搜索框中输入并核对“{query}”，尚未提交。"),
            new VerifiedSearchInput(verification, searchField, typedQuery));
    }

    private async Task<ToolExecutionResult> ClickRecognizedItemAsync(
        ScreenSnapshot snapshot,
        ScreenTextItem expected,
        int clicks,
        CancellationToken cancellationToken,
        Func<ScreenTextItem, Point>? pointResolver = null)
    {
        if (!ScreenRecognitionService.IsWindowAvailable(snapshot.WindowHandle)
            || !EnsureExactTargetIsForeground(snapshot.WindowHandle, BuildProcessNames(snapshot.ProcessName)))
        {
            return ToolExecutionResult.Fail("无法安全恢复生成识别结果的原窗口。");
        }
        if (!ScreenRecognitionService.TryGetWindowBounds(snapshot.WindowHandle, out var currentBounds)
            || !ScreenRecognitionService.WindowBoundsMatch(snapshot.WindowBounds, currentBounds))
        {
            return ToolExecutionResult.Fail("目标窗口的位置或大小已经改变，请重新识别。");
        }

        var refreshed = await ScreenRecognitionService.RefreshItemAsync(snapshot, expected, cancellationToken);
        if (refreshed is null || !ScreenRecognitionService.IsExactForegroundWindow(snapshot.WindowHandle))
        {
            return ToolExecutionResult.Fail("识别到的文字已经移动、消失或目标窗口失去前台，已取消点击。");
        }
        var point = pointResolver?.Invoke(refreshed)
                    ?? new Point(
                        refreshed.Bounds.Left + refreshed.Bounds.Width / 2,
                        refreshed.Bounds.Top + refreshed.Bounds.Height / 2);
        if (!snapshot.WindowBounds.Contains(point))
        {
            return ToolExecutionResult.Fail("识别目标计算出的点击位置超出窗口范围。");
        }

        var moveResult = MoveMouse(point.X, point.Y);
        if (!moveResult.Success)
        {
            return moveResult;
        }
        if (!ScreenRecognitionService.IsExactForegroundWindow(snapshot.WindowHandle))
        {
            return ToolExecutionResult.Fail("鼠标移动后目标窗口失去前台，已取消点击。");
        }
        return SendMouseButton("left", clicks);
    }

    private static IReadOnlyList<string> BuildProcessNames(string processName) =>
        string.IsNullOrWhiteSpace(processName) ? Array.Empty<string>() : new[] { processName };

    private async Task<ToolExecutionResult> InspectScreenAsync(
        int maxItems,
        CancellationToken cancellationToken)
    {
        var targetHandle = ScreenRecognitionService.GetForegroundTargetWindow();
        if (targetHandle == IntPtr.Zero || !ScreenRecognitionService.IsWindowAvailable(targetHandle))
        {
            return ToolExecutionResult.Fail("没有找到可识别的前台窗口。请先把网易云音乐或浏览器切到前台。");
        }
        if (ScreenRecognitionService.IsOwnedByCurrentProcess(targetHandle))
        {
            return ToolExecutionResult.Fail("当前前台是路遥智控窗口。请先把需要操作的网易云音乐或浏览器切到前台，再重新识别。");
        }

        if (!_permissionEnabled(PermissionKeys.ScreenRecognition)
            && !await _requestPermission(
                PermissionKeys.ScreenRecognition,
                "识别当前前台窗口中的可见文字",
                cancellationToken))
        {
            return ToolExecutionResult.Fail("用户没有授权本次屏幕文字识别，操作已取消。");
        }
        if (!_permissionEnabled(PermissionKeys.ScreenRecognition))
        {
            return ToolExecutionResult.Fail("屏幕文字识别授权当前不可用，操作已取消。");
        }

        var processName = ScreenRecognitionService.GetProcessName(targetHandle);
        IReadOnlyList<string> processNames = string.IsNullOrWhiteSpace(processName)
            ? Array.Empty<string>()
            : new[] { processName };
        if (!EnsureExactTargetIsForeground(targetHandle, processNames))
        {
            return ToolExecutionResult.Fail("授权窗口关闭后无法安全恢复原目标窗口。请手动切回目标窗口，再重新识别。");
        }

        await Task.Delay(180, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(targetHandle))
        {
            return ToolExecutionResult.Fail("识别前目标窗口发生变化，已停止读取。请保持目标窗口在前台后重试。");
        }

        var snapshot = await ScreenRecognitionService.InspectWindowAsync(
            targetHandle,
            Math.Clamp(maxItems, 10, 80),
            cancellationToken);
        if (snapshot.Items.Count == 0)
        {
            return ToolExecutionResult.Fail(
                "Windows 没有在当前窗口识别到可见文字。请等待搜索结果显示、把页面缩放恢复到正常大小后重试。");
        }

        _screenSnapshot = snapshot;
        _log($"已在本机识别前台窗口文字：{snapshot.ProcessName}，共 {snapshot.Items.Count} 项；截图未保存。");
        var lines = snapshot.Items.Select(item =>
        {
            var singleLine = item.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (singleLine.Length > 180)
            {
                singleLine = singleLine[..180] + "…";
            }
            return $"[{item.Index}] {singleLine}";
        });
        var message = string.Join(
            Environment.NewLine,
            $"屏幕快照 ID：{snapshot.Id}",
            $"前台程序：{(string.IsNullOrWhiteSpace(snapshot.ProcessName) ? "未知" : snapshot.ProcessName)}",
            $"识别语言：{snapshot.RecognitionLanguage}",
            "可见文字（从上到下、同一行从左到右）：",
            string.Join(Environment.NewLine, lines),
            "下一步：根据用户要求选中标题等唯一文字，再调用 windows.click_screen_item。抖音视频通常单击，网易云歌曲通常双击；不要选择“播放”“关注”等重复通用文字。快照 90 秒后失效。");
        return ToolExecutionResult.Ok(message);
    }

    private async Task<ToolExecutionResult> OpenScreenTextAsync(
        string text,
        int occurrence,
        int requestedClicks,
        CancellationToken cancellationToken)
    {
        var targetText = text.Trim();
        if (targetText.Length is < 1 or > 100)
        {
            return ToolExecutionResult.Fail("要打开的屏幕文字不能为空且不能超过 100 个字符。");
        }
        if (occurrence is < 0 or > 20)
        {
            return ToolExecutionResult.Fail("同名结果序号必须是 1 到 20；没有同名结果时请省略 occurrence。");
        }
        if (requestedClicks is not 0 and not 1 and not 2)
        {
            return ToolExecutionResult.Fail("点击次数只能是 1 或 2；需要自动判断时请省略 clicks。");
        }

        var inspection = await InspectScreenAsync(80, cancellationToken);
        if (!inspection.Success || _screenSnapshot is null)
        {
            return inspection;
        }

        var snapshot = _screenSnapshot;
        var normalizedTarget = NormalizeVisibleText(targetText);
        var exactMatches = snapshot.Items
            .Where(item => NormalizeVisibleText(item.Text).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ToArray();
        var matches = exactMatches.Length > 0
            ? exactMatches
            : snapshot.Items
                .Where(item =>
                {
                    var candidate = NormalizeVisibleText(item.Text);
                    return candidate.Length >= 2
                           && (candidate.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase)
                               || normalizedTarget.Contains(candidate, StringComparison.OrdinalIgnoreCase));
                })
                .OrderBy(item => item.Bounds.Top)
                .ThenBy(item => item.Bounds.Left)
                .ToArray();

        if (matches.Length == 0)
        {
            return ToolExecutionResult.Fail(
                $"当前画面没有识别到“{targetText}”。请确认文字完整可见。识别结果如下：{Environment.NewLine}{inspection.Message}");
        }
        if (matches.Length > 1 && occurrence == 0)
        {
            var choices = string.Join(
                Environment.NewLine,
                matches.Select((item, index) => $"同名 {index + 1}：[屏幕编号 {item.Index}] {item.Text}"));
            return ToolExecutionResult.Fail(
                $"当前画面有 {matches.Length} 个与“{targetText}”匹配的文字，为避免打开错误项目，没有猜测。请根据下列位置重新调用并提供 occurrence：{Environment.NewLine}{choices}");
        }

        var matchIndex = occurrence == 0 ? 0 : occurrence - 1;
        if (matchIndex < 0 || matchIndex >= matches.Length)
        {
            return ToolExecutionResult.Fail($"只找到 {matches.Length} 个“{targetText}”匹配项，没有第 {occurrence} 个。");
        }

        var selected = matches[matchIndex];
        var clicks = requestedClicks == 0
            ? DefaultOpenClickCount(snapshot.ProcessName)
            : requestedClicks;
        var clickResult = await ClickScreenItemAsync(snapshot.Id, selected.Index, clicks, cancellationToken);
        if (!clickResult.Success)
        {
            return clickResult;
        }

        return ToolExecutionResult.Ok(
            $"已识别并{(clicks == 2 ? "双击" : "单击")}屏幕文字“{selected.Text}”，向 Windows 发出打开请求。");
    }

    private static int DefaultOpenClickCount(string processName)
    {
        return processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 2;
    }

    private static string NormalizeVisibleText(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character))).Trim();

    private async Task<ToolExecutionResult> ClickScreenItemAsync(
        string snapshotId,
        int index,
        int clicks,
        CancellationToken cancellationToken)
    {
        var snapshot = _screenSnapshot;
        if (snapshot is null || !snapshot.Id.Equals(snapshotId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Fail("快照 ID 不存在或已被新的识别结果替换。请重新调用 windows.inspect_screen，不能猜测编号。");
        }
        if (DateTimeOffset.Now - snapshot.CreatedAt > TimeSpan.FromSeconds(90))
        {
            _screenSnapshot = null;
            return ToolExecutionResult.Fail("屏幕快照已超过 90 秒。为避免页面变化后点错，请重新识别屏幕。");
        }
        var selected = snapshot.Items.FirstOrDefault(item => item.Index == index);
        if (selected is null)
        {
            return ToolExecutionResult.Fail($"快照 {snapshot.Id} 中没有编号 {index}。请使用识别结果方括号内的编号。");
        }
        if (!_permissionEnabled(PermissionKeys.ScreenRecognition))
        {
            return ToolExecutionResult.Fail("屏幕文字识别授权已撤回，不能核对点击目标。请重新授权并识别屏幕。");
        }
        if (!_permissionEnabled(PermissionKeys.Mouse)
            && !await _requestPermission(
                PermissionKeys.Mouse,
                $"点击屏幕识别结果中的第 {index} 项",
                cancellationToken))
        {
            return ToolExecutionResult.Fail("用户没有授权本次鼠标点击，操作已取消。");
        }
        if (!_permissionEnabled(PermissionKeys.Mouse))
        {
            return ToolExecutionResult.Fail("鼠标授权当前不可用，操作已取消。");
        }
        if (DateTimeOffset.Now - snapshot.CreatedAt > TimeSpan.FromSeconds(90))
        {
            _screenSnapshot = null;
            return ToolExecutionResult.Fail("授权完成时屏幕快照已经过期。请重新识别屏幕后再点击。");
        }
        if (!ScreenRecognitionService.IsWindowAvailable(snapshot.WindowHandle))
        {
            _screenSnapshot = null;
            return ToolExecutionResult.Fail("原目标窗口已经关闭。请重新打开或搜索后再识别屏幕。");
        }

        var processNames = string.IsNullOrWhiteSpace(snapshot.ProcessName)
            ? Array.Empty<string>()
            : new[] { snapshot.ProcessName };
        if (!EnsureExactTargetIsForeground(snapshot.WindowHandle, processNames))
        {
            return ToolExecutionResult.Fail("无法安全恢复生成该快照的原窗口，已拒绝点击。请手动切回目标窗口并重新识别。");
        }
        if (!ScreenRecognitionService.TryGetWindowBounds(snapshot.WindowHandle, out var currentBounds)
            || !ScreenRecognitionService.WindowBoundsMatch(snapshot.WindowBounds, currentBounds))
        {
            _screenSnapshot = null;
            return ToolExecutionResult.Fail("目标窗口的位置或大小已经改变。为避免点错，请重新识别屏幕。");
        }

        await Task.Delay(160, cancellationToken);
        var refreshed = await ScreenRecognitionService.RefreshItemAsync(snapshot, selected, cancellationToken);
        if (refreshed is null)
        {
            _screenSnapshot = null;
            return ToolExecutionResult.Fail("页面内容已经变化，原文字不在原位置附近。为避免点错，已取消点击；请重新识别屏幕。");
        }
        if (!ScreenRecognitionService.IsExactForegroundWindow(snapshot.WindowHandle))
        {
            return ToolExecutionResult.Fail("核对文字后目标窗口失去前台，已取消点击。");
        }

        var x = refreshed.Bounds.Left + refreshed.Bounds.Width / 2;
        var y = refreshed.Bounds.Top + refreshed.Bounds.Height / 2;
        var moveResult = MoveMouse(x, y);
        if (!moveResult.Success)
        {
            return moveResult;
        }
        if (!ScreenRecognitionService.IsExactForegroundWindow(snapshot.WindowHandle))
        {
            return ToolExecutionResult.Fail("鼠标移动后目标窗口失去前台，已取消单击，避免操作错误窗口。");
        }

        var clickResult = SendMouseButton("left", clicks);
        if (!clickResult.Success)
        {
            return clickResult;
        }

        _screenSnapshot = null;
        _log($"已点击屏幕识别结果：{snapshot.ProcessName}，编号 {index}，次数 {clicks}；识别文字未写入日志。");
        var displayText = selected.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (displayText.Length > 100)
        {
            displayText = displayText[..100] + "…";
        }
        return ToolExecutionResult.Ok(
            $"已{(clicks == 2 ? "双击" : "单击")}快照 {snapshot.Id} 的第 {index} 项“{displayText}”。页面变化后如需继续操作，请重新识别屏幕。");
    }

    private bool EnsureExactTargetIsForeground(IntPtr handle, IReadOnlyList<string> processNames)
    {
        if (ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return true;
        }

        TryBringWindowToFront(handle, processNames);
        return ScreenRecognitionService.IsExactForegroundWindow(handle);
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

    private async Task<ToolExecutionResult> NeteaseMusicTaskAsync(
        string action,
        string query,
        int resultNumber,
        CancellationToken cancellationToken)
    {
        var app = ResolveApp("netease_music");
        if (app is null)
        {
            return ToolExecutionResult.Fail(
                "网易云音乐桌面应用尚未在“应用管理”中启用。请先自动检测路径并勾选网易云音乐；不要改用浏览器搜索代替。");
        }

        var normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction == "open")
        {
            return (await ActivateAppWindowAsync(app, cancellationToken)).Result;
        }
        if (normalizedAction is "play_pause" or "previous" or "next")
        {
            return await AppActionAsync(
                app.Alias,
                normalizedAction,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                cancellationToken);
        }
        if (normalizedAction is not "search" and not "search_and_play")
        {
            return ToolExecutionResult.Fail("网易云任务只支持 open、search、search_and_play、play_pause、previous 或 next。");
        }
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200)
        {
            return ToolExecutionResult.Fail("网易云搜索关键词不能为空且不能超过 200 个字符。");
        }
        if (normalizedAction == "search_and_play" && resultNumber is < 1 or > 20)
        {
            return ToolExecutionResult.Fail("要播放的搜索结果序号必须是 1 到 20。");
        }

        var searchResult = await AppActionAsync(
            app.Alias,
            "search",
            query.Trim(),
            string.Empty,
            string.Empty,
            string.Empty,
            cancellationToken);
        if (!searchResult.Success || normalizedAction == "search")
        {
            return searchResult;
        }

        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        var processNames = InstalledAppResolver.GetProcessNames(app, resolvedTarget);
        var handle = FindMainWindow(processNames);
        if (handle == IntPtr.Zero)
        {
            return ToolExecutionResult.Fail("已经提交网易云搜索，但没有找到可继续操作的网易云窗口。请等待应用显示后重试。");
        }

        ToolExecutionResult lastInspection = default;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1600 : 900, cancellationToken);
            if (!EnsureExactTargetIsForeground(handle, processNames))
            {
                return ToolExecutionResult.Fail("网易云搜索后窗口没有保持在前台，已停止后续点击，避免操作错误窗口。");
            }

            lastInspection = await InspectScreenAsync(80, cancellationToken);
            if (!lastInspection.Success || _screenSnapshot is null)
            {
                if (attempt == 2)
                {
                    return ToolExecutionResult.Fail($"已经在网易云中搜索“{query.Trim()}”，但屏幕识别未完成：{lastInspection.Message}");
                }
                continue;
            }

            var snapshot = _screenSnapshot;
            var resultItem = NeteaseMusicAutomation.FindResultItem(snapshot, resultNumber, query.Trim());
            if (resultItem is null)
            {
                continue;
            }

            var clickResult = await ClickScreenItemAsync(
                snapshot.Id,
                resultItem.Index,
                2,
                cancellationToken);
            if (!clickResult.Success)
            {
                return ToolExecutionResult.Fail(
                    $"已经在网易云中搜索“{query.Trim()}”，但播放第 {resultNumber} 个结果时停止：{clickResult.Message}");
            }

            _log($"网易云连续任务完成：搜索并播放第 {resultNumber} 个结果；搜索词未写入额外诊断文件。");
            return ToolExecutionResult.Ok(
                $"已打开网易云音乐，在客户端中搜索“{query.Trim()}”，并双击播放第 {resultNumber} 个搜索结果。");
        }

        return ToolExecutionResult.Fail(
            $"已打开网易云音乐并搜索“{query.Trim()}”，但无法可靠判断第 {resultNumber} 个歌曲结果，因此没有猜测点击。最近一次识别结果：{lastInspection.Message}");
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
                "search" => $"在 {app.DisplayName} 中输入并核对搜索词",
                "send_message" => $"在 {app.DisplayName} 中准备消息（本步骤不会发送）",
                "write_text" or "new_and_write" => "在记事本中创建并写入内容",
                _ => "在记事本中新建文档"
            };
            var denied = await EnsureAutomationPermissionAsync(
                PermissionKeys.Keyboard,
                reason,
                cancellationToken);
            if (denied is { } failure)
            {
                return failure;
            }
        }

        if (normalizedAction is "search" or "send_message")
        {
            foreach (var request in new[]
                     {
                         (PermissionKeys.ScreenRecognition, $"识别并核对 {app.DisplayName} 的搜索框和文字"),
                         (PermissionKeys.Mouse, $"聚焦 {app.DisplayName} 的搜索框并点击核对后的目标")
                     })
            {
                var denied = await EnsureAutomationPermissionAsync(
                    request.Item1,
                    request.Item2,
                    cancellationToken);
                if (denied is { } failure)
                {
                    return failure;
                }
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
            return await PrepareChatMessageAsync(app, handle, processNames, recipient, message, cancellationToken);
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

        var mediaResult = MediaControl(normalizedAction, 1, -1);
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
        return await PerformVerifiedSearchAsync(
            app.DisplayName,
            handle,
            processNames,
            query,
            cancellationToken);
    }

    private async Task<ToolExecutionResult> PrepareChatMessageAsync(
        AppEntry app,
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string recipient,
        string message,
        CancellationToken cancellationToken)
    {
        // A new recipient or message always invalidates the previous one-time
        // confirmation. This prevents an old confirmation from sending to a
        // conversation selected by a newer request.
        _pendingChatSend = null;

        if (!EnsureExactTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 没有保持在前台，已取消消息准备，避免输入到错误窗口。");
        }

        var filled = await FillAndVerifySearchBoxAsync(
            app.DisplayName,
            handle,
            processNames,
            recipient,
            cancellationToken);
        if (!filled.Result.Success || filled.Input is null)
        {
            return ToolExecutionResult.Fail($"联系人搜索未完成，消息没有输入：{filled.Result.Message}");
        }

        // QQ and WeChat both show live contact results. The verified search text
        // is never submitted with Enter because that would blindly open the
        // first result. Instead, wait for OCR and click the one exact contact.
        var resultDelay = app.Alias.Equals("qq", StringComparison.OrdinalIgnoreCase) ? 1050 : 850;
        await Task.Delay(resultDelay, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return ToolExecutionResult.Fail($"等待 {app.DisplayName} 联系人结果时窗口失去前台，消息没有输入。");
        }
        var resultSnapshot = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
        var contact = ScreenAutomationHeuristics.FindRecipientResult(
            resultSnapshot,
            recipient,
            filled.Input.OriginalField.Bounds);
        if (contact is null)
        {
            return ToolExecutionResult.Fail(
                $"已经在 {app.DisplayName} 搜索框中输入并核对“{recipient}”，但没有识别到唯一同名联系人。为避免发错人，没有按回车、没有猜测选择，也没有输入消息。");
        }

        var openConversationResult = await ClickRecognizedItemAsync(
            resultSnapshot,
            contact,
            1,
            cancellationToken);
        if (!openConversationResult.Success)
        {
            return ToolExecutionResult.Fail($"联系人“{recipient}”核对成功，但打开会话时停止：{openConversationResult.Message}");
        }

        await Task.Delay(650, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return ToolExecutionResult.Fail($"打开 {app.DisplayName} 会话后窗口失去前台，消息没有输入。");
        }
        var conversationSnapshot = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
        var header = ScreenAutomationHeuristics.FindConversationHeader(conversationSnapshot, recipient);
        if (header is null)
        {
            return ToolExecutionResult.Fail(
                $"已点击联系人“{recipient}”，但屏幕没有在会话标题位置核对到该名称，因此没有输入消息。");
        }
        var composerTarget = ScreenAutomationHeuristics.FindComposerTarget(conversationSnapshot);
        if (composerTarget is null)
        {
            return ToolExecutionResult.Fail(
                $"已核对 {app.DisplayName} 会话“{recipient}”，但无法可靠定位消息输入框，因此没有输入消息。");
        }

        var focusComposerResult = await ClickRecognizedItemAsync(
            conversationSnapshot,
            composerTarget.Anchor,
            1,
            cancellationToken,
            refreshed => ScreenAutomationHeuristics.ResolveComposerPoint(
                conversationSnapshot,
                refreshed,
                composerTarget.AnchorIsSendButton));
        if (!focusComposerResult.Success)
        {
            return ToolExecutionResult.Fail($"无法安全聚焦消息输入框：{focusComposerResult.Message}");
        }
        await Task.Delay(100, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 的消息输入框聚焦后窗口失去前台，消息没有输入。");
        }

        // Replace any old draft in the selected composer, preventing an earlier
        // unfinished message from being appended and sent as extra content.
        var selectAllResult = PressHotkey("ctrl+a");
        if (!selectAllResult.Success)
        {
            return selectAllResult;
        }
        var clearResult = PressHotkey("backspace");
        if (!clearResult.Success)
        {
            return clearResult;
        }
        var messageResult = TypeText(message);
        if (!messageResult.Success)
        {
            return messageResult;
        }

        await Task.Delay(380, cancellationToken);
        if (!ScreenRecognitionService.IsExactForegroundWindow(handle))
        {
            return ToolExecutionResult.Fail(
                $"{app.DisplayName} 在核对消息草稿前失去前台；没有执行发送，请重新准备。");
        }
        var preparedSnapshot = await ScreenRecognitionService.InspectWindowAsync(handle, 80, cancellationToken);
        var verifiedHeader = ScreenAutomationHeuristics.FindConversationHeader(
            preparedSnapshot,
            recipient,
            header.Bounds);
        var verifiedMessage = ScreenAutomationHeuristics.FindTypedMessage(preparedSnapshot, message);
        if (verifiedHeader is null || verifiedMessage is null)
        {
            return ToolExecutionResult.Fail(
                $"消息文字已经尝试填入，但屏幕无法同时核对联系人标题和草稿内容，因此没有生成发送确认；请检查界面后重新准备。");
        }

        var confirmationId = Guid.NewGuid().ToString("N")[..8];
        _pendingChatSend = new PendingChatSend(
            confirmationId,
            app.Alias,
            app.DisplayName,
            recipient,
            message,
            handle,
            processNames.ToArray(),
            preparedSnapshot.WindowBounds,
            verifiedHeader.Bounds,
            verifiedMessage.Bounds,
            DateTimeOffset.Now);
        _log($"已在 {app.DisplayName} 准备给 {recipient} 的消息（{message.Length} 个字符）；尚未发送，内容未写入日志。");
        return ToolExecutionResult.Ok(
            $"已在 {app.DisplayName} 重新搜索并核对联系人“{recipient}”，清除旧草稿后填入 {message.Length} 个字符。消息尚未发送。确认编号：{confirmationId}。请让用户先在屏幕上核对，并在后续单独明确说“确认发送”；两分钟内仅可确认一次。");
    }

    private async Task<ToolExecutionResult> ConfirmChatSendAsync(
        string confirmationId,
        CancellationToken cancellationToken)
    {
        var pending = _pendingChatSend;
        if (pending is null)
        {
            return ToolExecutionResult.Fail("当前没有待确认消息。请先调用 windows.prepare_chat_message 重新搜索联系人并准备消息。");
        }
        if (!pending.ConfirmationId.Equals(confirmationId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Fail("确认编号不匹配，未发送消息。请使用最近一次准备消息返回的编号。");
        }
        if (DateTimeOffset.Now - pending.CreatedAt > TimeSpan.FromMinutes(2))
        {
            _pendingChatSend = null;
            return ToolExecutionResult.Fail("消息确认已经超过两分钟并失效，未发送。请重新搜索联系人并准备消息。");
        }

        foreach (var request in new[]
                 {
                     (PermissionKeys.Keyboard, $"确认发送 {pending.AppDisplayName} 消息"),
                     (PermissionKeys.ScreenRecognition, "发送前再次核对联系人和草稿"),
                     (PermissionKeys.Mouse, "聚焦草稿或点击发送按钮")
                 })
        {
            var denied = await EnsureAutomationPermissionAsync(
                request.Item1,
                request.Item2,
                cancellationToken);
            if (denied is { } failure)
            {
                return failure;
            }
        }

        if (!ScreenRecognitionService.IsWindowAvailable(pending.WindowHandle)
            || !EnsureExactTargetIsForeground(pending.WindowHandle, pending.ProcessNames))
        {
            _pendingChatSend = null;
            return ToolExecutionResult.Fail($"原 {pending.AppDisplayName} 会话窗口已经关闭或无法恢复；确认已作废，消息未发送。");
        }
        if (!ScreenRecognitionService.TryGetWindowBounds(pending.WindowHandle, out var currentBounds)
            || !ScreenRecognitionService.WindowBoundsMatch(pending.WindowBounds, currentBounds))
        {
            _pendingChatSend = null;
            return ToolExecutionResult.Fail("准备消息后聊天窗口的位置或大小发生变化；确认已作废，消息未发送。");
        }

        await Task.Delay(180, cancellationToken);
        var current = await ScreenRecognitionService.InspectWindowAsync(
            pending.WindowHandle,
            80,
            cancellationToken);
        var header = ScreenAutomationHeuristics.FindConversationHeader(
            current,
            pending.Recipient,
            pending.HeaderBounds);
        var draft = ScreenAutomationHeuristics.FindTypedMessage(
            current,
            pending.Message,
            pending.MessageBounds);
        if (header is null || draft is null)
        {
            _pendingChatSend = null;
            return ToolExecutionResult.Fail(
                "发送前复核发现联系人或草稿与准备时不一致，确认已作废且没有发送。请重新准备消息。");
        }

        ToolExecutionResult sendResult;
        var sendButton = ScreenAutomationHeuristics.FindSendButton(current);
        if (sendButton is not null)
        {
            var refreshedButton = await ScreenRecognitionService.RefreshItemAsync(
                current,
                sendButton,
                cancellationToken);
            if (refreshedButton is null || !ScreenRecognitionService.IsExactForegroundWindow(pending.WindowHandle))
            {
                _pendingChatSend = null;
                return ToolExecutionResult.Fail("发送按钮在最终复核时发生变化，确认已作废且没有发送。");
            }
            var buttonPoint = new Point(
                refreshedButton.Bounds.Left + refreshedButton.Bounds.Width / 2,
                refreshedButton.Bounds.Top + refreshedButton.Bounds.Height / 2);
            var moveResult = MoveMouse(buttonPoint.X, buttonPoint.Y);
            if (!moveResult.Success || !ScreenRecognitionService.IsExactForegroundWindow(pending.WindowHandle))
            {
                _pendingChatSend = null;
                return ToolExecutionResult.Fail("鼠标未能安全到达发送按钮，确认已作废且没有发送。");
            }

            // Consume before the one physical send action so retries can never
            // duplicate the message, even if Windows reports a partial failure.
            _pendingChatSend = null;
            sendResult = SendMouseButton("left", 1);
        }
        else
        {
            var focusResult = await ClickRecognizedItemAsync(current, draft, 1, cancellationToken);
            if (!focusResult.Success || !ScreenRecognitionService.IsExactForegroundWindow(pending.WindowHandle))
            {
                _pendingChatSend = null;
                return ToolExecutionResult.Fail("无法在最终复核后重新聚焦消息草稿，确认已作废且没有发送。");
            }
            _pendingChatSend = null;
            sendResult = PressHotkey("enter");
        }

        if (!sendResult.Success)
        {
            return ToolExecutionResult.Fail(
                $"Windows 没有确认完成发送动作：{sendResult.Message} 为防止重复发送，确认编号已经作废；请先查看聊天窗口，确需重试时重新准备。");
        }

        _log($"已确认向 {pending.AppDisplayName} 联系人 {pending.Recipient} 发送一次（{pending.Message.Length} 个字符，内容未写入日志）。");
        return ToolExecutionResult.Ok(
            $"已再次核对 {pending.AppDisplayName} 联系人“{pending.Recipient}”和草稿内容，并只执行一次发送。确认编号已使用，重复确认不会再次发送。");
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

    private static ToolExecutionResult WebSearch(string query, string engine, bool forceBrowser)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolExecutionResult.Fail("搜索内容不能为空。");
        }
        if (NeteaseMusicAutomation.ShouldRejectWebSearch(query, forceBrowser))
        {
            return ToolExecutionResult.Fail(
                "检测到这是网易云音乐应用请求，已阻止打开浏览器。请改用 windows.netease_music_task；只有用户明确要求网页搜索时才设置 force_browser=true。");
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

    private ToolExecutionResult MediaControl(string action, int steps, int level)
    {
        const byte volumeUp = 0xAF;
        const byte volumeDown = 0xAE;
        const byte volumeMute = 0xAD;
        const byte mediaPlayPause = 0xB3;
        const byte mediaPrevious = 0xB1;
        const byte mediaNext = 0xB0;

        var normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction == "set_volume")
        {
            return WindowsSystemTools.SetMasterVolume(level);
        }
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

    private static bool OptionalBool(JsonElement arguments, string property, bool defaultValue)
    {
        return arguments.ValueKind == JsonValueKind.Object
               && arguments.TryGetProperty(property, out var value)
               && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
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
