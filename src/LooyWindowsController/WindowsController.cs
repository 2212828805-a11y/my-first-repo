using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Looy.WindowsController;

internal sealed class WindowsController
{
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint InputKeyboard = 1;

    private readonly Func<string, bool> _permissionEnabled;
    private readonly Func<IReadOnlyList<AppEntry>> _getApps;
    private readonly SettingsStore _settingsStore;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    public WindowsController(
        Func<string, bool> permissionEnabled,
        Func<IReadOnlyList<AppEntry>> getApps,
        SettingsStore settingsStore,
        Action<string> log)
    {
        _permissionEnabled = permissionEnabled;
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
                "windows.type_text" => RequirePermission(
                    PermissionKeys.Keyboard,
                    () => TypeText(RequiredString(arguments, "text"))),
                "windows.hotkey" => RequirePermission(
                    PermissionKeys.Keyboard,
                    () => PressHotkey(RequiredString(arguments, "keys"))),
                "windows.move_mouse" => RequirePermission(
                    PermissionKeys.Mouse,
                    () => MoveMouse(RequiredInt(arguments, "x"), RequiredInt(arguments, "y"))),
                "windows.click" => RequirePermission(PermissionKeys.Mouse, () => Click(arguments)),
                "windows.scroll" => RequirePermission(
                    PermissionKeys.Mouse,
                    () => Scroll(RequiredInt(arguments, "amount"))),
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

    private static ToolExecutionResult GetSystemStatus()
    {
        var message = string.Join(
            Environment.NewLine,
            $"电脑名称：{Environment.MachineName}",
            $"当前用户：{Environment.UserName}",
            $"系统：{Environment.OSVersion}",
            $"64 位系统：{Environment.Is64BitOperatingSystem}",
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
        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        };
        Process.Start(startInfo);
        _log($"已打开应用：{app.DisplayName}");
        return ToolExecutionResult.Ok($"已打开 {app.DisplayName}。");
    }

    private ToolExecutionResult CloseApp(string alias)
    {
        var app = ResolveApp(alias);
        if (app is null)
        {
            return ToolExecutionResult.Fail($"应用 {alias} 不在白名单中或尚未启用。");
        }

        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        if (InstalledAppResolver.IsProtocol(app.Target.Trim()))
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
            if (!_permissionEnabled(PermissionKeys.Keyboard))
            {
                return ToolExecutionResult.Fail("应用内搜索需要先在“授权管理”中开启键盘权限。");
            }
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200)
            {
                return ToolExecutionResult.Fail("搜索关键词不能为空且不能超过 200 个字符。");
            }
        }
        else if (normalizedAction == "send_message")
        {
            if (!app.Alias.Equals("wechat", StringComparison.OrdinalIgnoreCase))
            {
                return ToolExecutionResult.Fail("发送消息动作目前只支持微信。");
            }
            if (!_permissionEnabled(PermissionKeys.Keyboard))
            {
                return ToolExecutionResult.Fail("微信发送消息需要先在“权限管理”中开启键盘输入权限。");
            }
            recipient = recipient.Trim();
            message = message.Trim();
            if (recipient.Length is < 1 or > 80)
            {
                return ToolExecutionResult.Fail("微信联系人不能为空且不能超过 80 个字符。");
            }
            if (message.Length is < 1 or > 1000)
            {
                return ToolExecutionResult.Fail("微信消息不能为空且不能超过 1000 个字符。");
            }
        }
        else if (normalizedAction is "play_pause" or "previous" or "next")
        {
            if (!_permissionEnabled(PermissionKeys.Media))
            {
                return ToolExecutionResult.Fail("媒体动作需要先在“授权管理”中开启媒体权限。");
            }
        }
        else if (normalizedAction != "activate")
        {
            return ToolExecutionResult.Fail("不支持的应用动作。");
        }

        var activation = await ActivateAppWindowAsync(app, cancellationToken);
        if (!activation.Success)
        {
            return activation;
        }

        if (normalizedAction == "activate")
        {
            return activation;
        }

        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        var processNames = InstalledAppResolver.GetProcessNames(app, resolvedTarget);
        var handle = FindMainWindow(processNames);
        if (handle == IntPtr.Zero)
        {
            return ToolExecutionResult.Fail($"无法再次确认 {app.DisplayName} 的主窗口，请重试。");
        }

        await Task.Delay(350, cancellationToken);
        if (normalizedAction == "search")
        {
            return await SearchInAppAsync(app, handle, processNames, query.Trim(), cancellationToken);
        }

        if (normalizedAction == "send_message")
        {
            return await SendWeChatMessageAsync(handle, processNames, recipient, message, cancellationToken);
        }

        var mediaResult = MediaControl(normalizedAction, 1);
        if (mediaResult.Success)
        {
            _log($"已对 {app.DisplayName} 执行：{normalizedAction}");
        }
        return mediaResult;
    }

    private async Task<ToolExecutionResult> ActivateAppWindowAsync(
        AppEntry app,
        CancellationToken cancellationToken)
    {
        var resolvedTarget = InstalledAppResolver.TryResolvePath(app);
        var processNames = InstalledAppResolver.GetProcessNames(app, resolvedTarget);
        var handle = FindMainWindow(processNames);
        if (handle == IntPtr.Zero)
        {
            var openResult = OpenApp(app.Alias);
            if (!openResult.Success)
            {
                return openResult;
            }
            for (var attempt = 0; attempt < 30 && handle == IntPtr.Zero; attempt++)
            {
                await Task.Delay(200, cancellationToken);
                handle = FindMainWindow(processNames);
            }
        }

        if (handle == IntPtr.Zero)
        {
            return ToolExecutionResult.Fail($"已启动 {app.DisplayName}，但没有检测到可激活的主窗口。应用可能仍在启动或缩小到托盘。");
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
            return ToolExecutionResult.Fail(
                $"Windows 阻止了 {app.DisplayName} 获取前台焦点。请手动点击一次窗口后重试，控制器不会向未确认的窗口输入内容。");
        }
        _log($"已激活应用窗口：{app.DisplayName}");
        return ToolExecutionResult.Ok($"已激活 {app.DisplayName} 窗口。");
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
        if (!IsTargetProcessForeground(processNames))
        {
            return ToolExecutionResult.Fail($"{app.DisplayName} 的搜索框尚未获得焦点，已停止输入，请重试。");
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
            if (!IsTargetProcessForeground(processNames))
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

    private async Task<ToolExecutionResult> SendWeChatMessageAsync(
        IntPtr handle,
        IReadOnlyList<string> processNames,
        string recipient,
        string message,
        CancellationToken cancellationToken)
    {
        if (!EnsureTargetIsForeground(handle, processNames))
        {
            return ToolExecutionResult.Fail("微信没有保持在前台，已取消发送，避免消息进入错误窗口。");
        }

        PressHotkey("esc");
        await Task.Delay(120, cancellationToken);
        var searchResult = PressHotkey("ctrl+f");
        if (!searchResult.Success)
        {
            return searchResult;
        }
        await Task.Delay(500, cancellationToken);
        if (!IsTargetProcessForeground(processNames))
        {
            return ToolExecutionResult.Fail("微信搜索框没有获得焦点，已取消发送。");
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

        await Task.Delay(900, cancellationToken);
        if (!IsTargetProcessForeground(processNames))
        {
            return ToolExecutionResult.Fail("等待微信联系人结果时窗口失去焦点，消息未发送。");
        }
        var openConversationResult = PressHotkey("enter");
        if (!openConversationResult.Success)
        {
            return openConversationResult;
        }

        await Task.Delay(600, cancellationToken);
        if (!IsTargetProcessForeground(processNames))
        {
            return ToolExecutionResult.Fail("打开微信会话后窗口失去焦点，消息未发送。");
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
        if (!IsTargetProcessForeground(processNames))
        {
            return ToolExecutionResult.Fail("微信在发送前失去焦点，消息内容已输入但没有按下发送键。");
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

        _log($"已向微信联系人 {recipient} 执行发送（{message.Length} 个字符，内容未写入日志）。");
        return ToolExecutionResult.Ok($"已在微信联系人“{recipient}”的会话中按下发送键。");
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
                        if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
                        {
                            candidates.Add((process.MainWindowHandle, long.MaxValue));
                        }
                    }
                    catch
                    {
                        // Continue checking other instances.
                    }
                }
            }
        }

        if (candidates.Count == 0 && processIds.Count > 0)
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
                if (width < 320 || height < 240)
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

    private static bool EnsureTargetIsForeground(IntPtr handle, IReadOnlyList<string> processNames)
    {
        return IsTargetProcessForeground(processNames) || TryBringWindowToFront(handle, processNames);
    }

    private static bool IsTargetProcessForeground(IReadOnlyList<string> processNames)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
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

    private static bool TryBringWindowToFront(IntPtr handle, IReadOnlyList<string> processNames)
    {
        if (handle == IntPtr.Zero)
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
        return IsTargetProcessForeground(processNames);
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
            File.WriteAllText(path, InstalledAppResolver.BuildDiagnosticReport(_getApps()));
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
            _ => $"https://www.baidu.com/s?wd={encodedQuery}"
        };
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return ToolExecutionResult.Ok($"已搜索：{query.Trim()}");
    }

    private static ToolExecutionResult TypeText(string text)
    {
        if (text.Length == 0)
        {
            return ToolExecutionResult.Fail("输入文字不能为空。");
        }

        if (text.Length > 4000)
        {
            return ToolExecutionResult.Fail("单次输入不能超过 4000 个字符。");
        }

        foreach (var character in text)
        {
            var inputs = new[]
            {
                CreateUnicodeInput(character, false),
                CreateUnicodeInput(character, true)
            };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            if (sent != (uint)inputs.Length)
            {
                return ToolExecutionResult.Fail("键盘输入被系统或高权限窗口阻止。");
            }
        }

        return ToolExecutionResult.Ok($"已输入 {text.Length} 个字符。");
    }

    private static ToolExecutionResult PressHotkey(string keys)
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

        foreach (var virtualKey in virtualKeys)
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        }
        for (var index = virtualKeys.Count - 1; index >= 0; index--)
        {
            keybd_event(virtualKeys[index], 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        return ToolExecutionResult.Ok($"已按下快捷键：{string.Join('+', parts)}");
    }

    private static ToolExecutionResult MoveMouse(int x, int y)
    {
        var screen = SystemInformation.VirtualScreen;
        if (x < screen.Left || x >= screen.Right || y < screen.Top || y >= screen.Bottom)
        {
            return ToolExecutionResult.Fail(
                $"坐标超出屏幕范围。当前范围：x={screen.Left}..{screen.Right - 1}, y={screen.Top}..{screen.Bottom - 1}");
        }

        return SetCursorPos(x, y)
            ? ToolExecutionResult.Ok($"鼠标已移动到 ({x}, {y})。")
            : ToolExecutionResult.Fail("鼠标移动被系统阻止。");
    }

    private static ToolExecutionResult Click(JsonElement arguments)
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

        var button = OptionalString(arguments, "button", "left").ToLowerInvariant();
        var clicks = Math.Clamp(OptionalInt(arguments, "clicks", 1), 1, 2);
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
            mouse_event(flags.Item1, 0, 0, 0, UIntPtr.Zero);
            mouse_event(flags.Item2, 0, 0, 0, UIntPtr.Zero);
            if (clicks == 2 && index == 0)
            {
                Thread.Sleep(80);
            }
        }

        return ToolExecutionResult.Ok($"已{(clicks == 2 ? "双击" : "单击")}{button}键。");
    }

    private static ToolExecutionResult Scroll(int amount)
    {
        if (amount is < -20 or > 20 || amount == 0)
        {
            return ToolExecutionResult.Fail("滚动量必须是 -20 到 20 之间的非零整数。");
        }

        mouse_event(MouseEventWheel, 0, 0, unchecked((uint)(amount * 120)), UIntPtr.Zero);
        return ToolExecutionResult.Ok($"已滚动 {amount} 格。");
    }

    private static ToolExecutionResult MediaControl(string action, int steps)
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
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
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
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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
