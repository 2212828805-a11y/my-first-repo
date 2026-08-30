using Microsoft.Win32;

namespace Looy.WindowsController;

internal static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LooyWindowsController";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true)
                        ?? throw new InvalidOperationException("无法打开 Windows 开机启动设置。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定程序路径。");
        }

        key.SetValue(ValueName, $"\"{executablePath}\" --autostart", RegistryValueKind.String);
    }
}
