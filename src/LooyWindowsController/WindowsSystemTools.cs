using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Looy.WindowsController;

internal static class WindowsSystemTools
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const int ClsContextAll = 23;
    private static readonly Guid MMDeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private static readonly string[] WallpaperExtensions = [".bmp", ".jpg", ".jpeg", ".png"];

    public static ToolExecutionResult GetResourceStatus()
    {
        try
        {
            var networkBefore = GetNetworkTotals();
            var cpuBefore = ReadCpuTimes();
            Thread.Sleep(350);
            var cpuAfter = ReadCpuTimes();
            var networkAfter = GetNetworkTotals();

            var cpuPercent = CalculateCpuPercent(cpuBefore, cpuAfter);
            var memoryLine = GetMemoryLine();
            var diskLines = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .Select(drive =>
                {
                    var used = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
                    var percent = drive.TotalSize == 0 ? 0 : used * 100d / drive.TotalSize;
                    return $"{drive.Name} {FormatBytes(used)}/{FormatBytes(drive.TotalSize)}（{percent:F1}%）";
                })
                .ToArray();

            const double sampleSeconds = 0.35;
            var uploadPerSecond = Math.Max(0, networkAfter.Sent - networkBefore.Sent) / sampleSeconds;
            var downloadPerSecond = Math.Max(0, networkAfter.Received - networkBefore.Received) / sampleSeconds;
            var message = string.Join(
                Environment.NewLine,
                $"CPU 使用率：{cpuPercent:F1}%（{Environment.ProcessorCount} 个逻辑处理器）",
                memoryLine,
                "磁盘：" + (diskLines.Length == 0 ? "未找到可用磁盘" : string.Join("；", diskLines)),
                $"网络瞬时速度：上传 {FormatBytes((long)uploadPerSecond)}/s，下载 {FormatBytes((long)downloadPerSecond)}/s");
            return ToolExecutionResult.Ok(message);
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"读取系统资源失败：{exception.Message}");
        }
    }

    public static ToolExecutionResult SetMasterVolume(int level)
    {
        if (level is < 0 or > 100)
        {
            return ToolExecutionResult.Fail("音量必须是 0 到 100 之间的整数。");
        }

        IMMDevice? device = null;
        object? endpointObject = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorClassId, throwOnError: true)
                                 ?? throw new InvalidOperationException("Windows 音频设备服务不可用。");
            var enumerator = (IMMDeviceEnumerator)(Activator.CreateInstance(enumeratorType)
                             ?? throw new InvalidOperationException("无法创建 Windows 音频设备枚举器。"));
            try
            {
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
                var endpointVolumeId = typeof(IAudioEndpointVolume).GUID;
                ThrowIfFailed(device.Activate(ref endpointVolumeId, ClsContextAll, IntPtr.Zero, out endpointObject));
                var endpointVolume = (IAudioEndpointVolume)endpointObject;
                ThrowIfFailed(endpointVolume.SetMasterVolumeLevelScalar(level / 100f, Guid.Empty));
            }
            finally
            {
                if (Marshal.IsComObject(enumerator))
                {
                    Marshal.FinalReleaseComObject(enumerator);
                }
            }

            return ToolExecutionResult.Ok($"系统音量已设置为 {level}%。");
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"设置系统音量失败：{exception.Message}");
        }
        finally
        {
            if (endpointObject is not null && Marshal.IsComObject(endpointObject))
            {
                Marshal.FinalReleaseComObject(endpointObject);
            }
            if (device is not null && Marshal.IsComObject(device))
            {
                Marshal.FinalReleaseComObject(device);
            }
        }
    }

    public static ToolExecutionResult SetTheme(bool dark)
    {
        try
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (key is null)
            {
                return ToolExecutionResult.Fail("Windows 没有允许打开当前用户的主题设置。");
            }

            var lightValue = dark ? 0 : 1;
            key.SetValue("AppsUseLightTheme", lightValue, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", lightValue, RegistryValueKind.DWord);
            _ = SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                UIntPtr.Zero,
                "ImmersiveColorSet",
                SmtoAbortIfHung,
                1000,
                out _);
            return ToolExecutionResult.Ok($"Windows 已切换为{(dark ? "深色" : "浅色")}主题。");
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"切换主题失败：{exception.Message}");
        }
    }

    public static ToolExecutionResult SetWallpaper(string path)
    {
        var validation = ValidateWallpaperPath(path, out var fullPath);
        if (!validation.Success)
        {
            return validation;
        }

        try
        {
            var changed = SystemParametersInfo(
                SpiSetDesktopWallpaper,
                0,
                fullPath,
                SpifUpdateIniFile | SpifSendChange);
            return changed
                ? ToolExecutionResult.Ok($"桌面壁纸已设置为：{fullPath}")
                : ToolExecutionResult.Fail($"Windows 未能更换壁纸，错误码 {Marshal.GetLastWin32Error()}。");
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"更换壁纸失败：{exception.Message}");
        }
    }

    public static ToolExecutionResult ExecutePowerAction(string action, int delaySeconds)
    {
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized == "lock")
        {
            return LockWorkStation()
                ? ToolExecutionResult.Ok("电脑已锁定。")
                : ToolExecutionResult.Fail($"Windows 未能锁定电脑，错误码 {Marshal.GetLastWin32Error()}。");
        }

        if (!TryBuildPowerArguments(normalized, delaySeconds, out var arguments, out var error))
        {
            return ToolExecutionResult.Fail(error);
        }
        return RunShutdown(arguments, normalized == "restart" ? "重启" : "关机");
    }

    public static ToolExecutionResult CancelPendingPowerAction()
    {
        return RunShutdown(["/a"], "取消关机或重启计划");
    }

    public static ToolExecutionResult ReadClipboardText()
    {
        string? value = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                value = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : null;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "LOOY Clipboard Reader"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            return ToolExecutionResult.Fail("读取剪贴板超时；剪贴板可能正被其他程序占用。");
        }
        if (failure is not null)
        {
            return ToolExecutionResult.Fail($"读取剪贴板失败：{failure.Message}");
        }
        if (string.IsNullOrEmpty(value))
        {
            return ToolExecutionResult.Fail("剪贴板中没有可读取的文字。");
        }

        const int maximumReturnedCharacters = 4000;
        if (value.Length > maximumReturnedCharacters)
        {
            value = value[..maximumReturnedCharacters] + "\n…（剪贴板内容过长，仅返回前 4000 个字符）";
        }
        return ToolExecutionResult.Ok("剪贴板文字：\n" + value);
    }

    internal static bool RunComponentSelfTest()
    {
        var shutdownOk = TryBuildPowerArguments("shutdown", 60, out var shutdown, out _)
                         && shutdown.SequenceEqual(["/s", "/t", "60"]);
        var restartOk = TryBuildPowerArguments("restart", 0, out var restart, out _)
                        && restart.SequenceEqual(["/r", "/t", "0"]);
        var invalidPower = !TryBuildPowerArguments("hibernate", 0, out _, out _)
                           && !TryBuildPowerArguments("shutdown", -1, out _, out _);
        var tools = ToolCatalog.Build(_ => true);
        var expectedTools = new[]
        {
            "windows.resource_status",
            "windows.read_clipboard_text",
            "windows.show_desktop",
            "windows.find_text",
            "windows.presentation_control",
            "windows.system_control",
            "windows.prepare_power_action",
            "windows.confirm_power_action"
        };
        var catalogOk = expectedTools.All(name => tools.Any(tool => tool.Name == name));
        var noShell = tools.All(tool => !tool.Name.Contains("cmd", StringComparison.OrdinalIgnoreCase)
                                       && !tool.Name.Contains("shell", StringComparison.OrdinalIgnoreCase));
        return shutdownOk && restartOk && invalidPower && catalogOk && noShell;
    }

    private static ToolExecutionResult ValidateWallpaperPath(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return ToolExecutionResult.Fail("壁纸必须使用本机图片的绝对路径。");
        }

        fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
        {
            return ToolExecutionResult.Fail("指定的壁纸文件不存在。");
        }
        if (!WallpaperExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Fail("壁纸只支持 BMP、JPG、JPEG 或 PNG 图片。");
        }
        if (new FileInfo(fullPath).Length > 50L * 1024 * 1024)
        {
            return ToolExecutionResult.Fail("壁纸文件不能超过 50 MB。");
        }
        return ToolExecutionResult.Ok("壁纸路径有效。");
    }

    private static bool TryBuildPowerArguments(
        string action,
        int delaySeconds,
        out string[] arguments,
        out string error)
    {
        arguments = [];
        error = string.Empty;
        if (delaySeconds is < 0 or > 3600)
        {
            error = "延迟时间必须是 0 到 3600 秒。";
            return false;
        }

        var operation = action switch
        {
            "shutdown" => "/s",
            "restart" => "/r",
            _ => string.Empty
        };
        if (operation.Length == 0)
        {
            error = "电源操作只支持 lock、shutdown 或 restart。";
            return false;
        }

        arguments = [operation, "/t", delaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        return true;
    }

    private static ToolExecutionResult RunShutdown(IReadOnlyList<string> arguments, string operationName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ToolExecutionResult.Fail($"Windows 未能启动“{operationName}”操作。");
            }
            if (!process.WaitForExit(3000))
            {
                return ToolExecutionResult.Fail($"Windows 没有及时确认“{operationName}”操作。");
            }
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd().Trim();
                return ToolExecutionResult.Fail(
                    string.IsNullOrWhiteSpace(error)
                        ? $"“{operationName}”失败，退出码 {process.ExitCode}。"
                        : $"“{operationName}”失败：{error}");
            }
            return ToolExecutionResult.Ok($"Windows 已执行：{operationName}。");
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Fail($"{operationName}失败：{exception.Message}");
        }
    }

    private static string GetMemoryLine()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return "内存：Windows 未返回数据";
        }
        var used = status.TotalPhysical - status.AvailablePhysical;
        return $"内存：{FormatBytes((long)used)}/{FormatBytes((long)status.TotalPhysical)}（{status.MemoryLoad}%）";
    }

    private static CpuTimes ReadCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new InvalidOperationException($"Windows 未返回 CPU 时间，错误码 {Marshal.GetLastWin32Error()}。");
        }
        return new CpuTimes(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
    }

    private static double CalculateCpuPercent(CpuTimes before, CpuTimes after)
    {
        var idle = after.Idle - before.Idle;
        var kernel = after.Kernel - before.Kernel;
        var user = after.User - before.User;
        var total = kernel + user;
        return total == 0 ? 0 : Math.Clamp((total - idle) * 100d / total, 0, 100);
    }

    private static NetworkTotals GetNetworkTotals()
    {
        long sent = 0;
        long received = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }
            try
            {
                var statistics = adapter.GetIPv4Statistics();
                sent += statistics.BytesSent;
                received += statistics.BytesReceived;
            }
            catch
            {
                // Some virtual adapters do not expose counters. Ignore only that adapter.
            }
        }
        return new NetworkTotals(sent, received);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }
        return $"{display:F1} {units[unit]}";
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
    private readonly record struct NetworkTotals(long Sent, long Received);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        [PreserveSig] int GetMute(out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(Guid eventContext);
        [PreserveSig] int VolumeStepDown(Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        string value,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
