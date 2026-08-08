using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Looy.WindowsController;

internal static class WindowsInputAccess
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity)
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string StatusText => IsElevated
        ? "系统层级：管理员输入模式（仍不能操作 UAC 安全窗口）"
        : "系统层级：普通输入模式（可操作普通应用）";

    public static void RestartElevated()
    {
        var executablePath = Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("没有找到路遥智控程序文件，无法切换管理员输入模式。");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--elevated-restart",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("你取消了 Windows 管理员确认，程序仍保持普通输入模式。", exception);
        }
    }
}
