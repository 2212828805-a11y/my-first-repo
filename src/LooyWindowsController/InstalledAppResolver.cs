using System.Diagnostics;
using Microsoft.Win32;

namespace Looy.WindowsController;

internal static class InstalledAppResolver
{
    private sealed record AppSpec(
        string[] ExecutableNames,
        string[] ProcessNames,
        string[] DisplayNameTokens,
        string[] KnownPaths,
        string[] SupportedActions);

    private static readonly Dictionary<string, AppSpec> Specs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wechat"] = new(
            ["Weixin.exe", "WeChat.exe"],
            ["Weixin", "WeChat"],
            ["微信", "Weixin", "WeChat"],
            [
                @"%ProgramFiles%\Tencent\Weixin\Weixin.exe",
                @"%ProgramFiles(x86)%\Tencent\Weixin\Weixin.exe",
                @"%ProgramFiles%\Tencent\WeChat\WeChat.exe",
                @"%ProgramFiles(x86)%\Tencent\WeChat\WeChat.exe",
                @"%LOCALAPPDATA%\Tencent\Weixin\Weixin.exe",
                @"%LOCALAPPDATA%\Tencent\WeChat\WeChat.exe",
                @"%LOCALAPPDATA%\Programs\Tencent\Weixin\Weixin.exe",
                @"%LOCALAPPDATA%\Programs\Tencent\WeChat\WeChat.exe"
            ],
            ["activate", "search"]),
        ["netease_music"] = new(
            ["cloudmusic.exe"],
            ["cloudmusic"],
            ["网易云音乐", "NetEase CloudMusic", "CloudMusic"],
            [
                @"%ProgramFiles%\NetEase\CloudMusic\cloudmusic.exe",
                @"%ProgramFiles(x86)%\NetEase\CloudMusic\cloudmusic.exe",
                @"%LOCALAPPDATA%\NetEase\CloudMusic\cloudmusic.exe",
                @"%LOCALAPPDATA%\Programs\NetEase\CloudMusic\cloudmusic.exe"
            ],
            ["activate", "search", "play_pause", "previous", "next"]),
        ["chrome"] = new(
            ["chrome.exe"],
            ["chrome"],
            ["Google Chrome"],
            [
                @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
                @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
                @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
            ],
            ["activate", "search"]),
        ["vscode"] = new(
            ["Code.exe"],
            ["Code"],
            ["Microsoft Visual Studio Code", "Visual Studio Code"],
            [
                @"%ProgramFiles%\Microsoft VS Code\Code.exe",
                @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"
            ],
            ["activate"]),
        ["douyin"] = new(
            ["Douyin.exe"],
            ["Douyin"],
            ["抖音", "Douyin"],
            [
                @"%ProgramFiles%\Douyin\Douyin.exe",
                @"%LOCALAPPDATA%\Douyin\Douyin.exe",
                @"%LOCALAPPDATA%\Programs\Douyin\Douyin.exe"
            ],
            ["activate", "play_pause"])
    };

    public static string ResolveForLaunch(AppEntry app) =>
        TryResolvePath(app) ?? Environment.ExpandEnvironmentVariables(app.Target.Trim());

    public static string? TryResolvePath(AppEntry app)
    {
        var expandedTarget = Environment.ExpandEnvironmentVariables(app.Target.Trim());
        if (IsProtocol(expandedTarget))
        {
            return expandedTarget;
        }
        if (Path.IsPathRooted(expandedTarget) && File.Exists(expandedTarget))
        {
            return Path.GetFullPath(expandedTarget);
        }

        var spec = Specs.GetValueOrDefault(app.Alias);
        var executableNames = BuildExecutableNames(expandedTarget, spec);

        foreach (var processName in GetProcessNames(app, expandedTarget))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch
                    {
                        // Some elevated or protected processes hide module details.
                    }
                }
            }
        }

        foreach (var executableName in executableNames)
        {
            var appPath = FindAppPath(executableName);
            if (appPath is not null)
            {
                return appPath;
            }
        }

        if (spec is not null)
        {
            foreach (var candidate in spec.KnownPaths)
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate);
                if (File.Exists(expanded))
                {
                    return Path.GetFullPath(expanded);
                }
            }

            var uninstallPath = FindFromUninstallRegistry(spec);
            if (uninstallPath is not null)
            {
                return uninstallPath;
            }
        }

        foreach (var executableName in executableNames)
        {
            var systemPath = FindOnSearchPath(executableName);
            if (systemPath is not null)
            {
                return systemPath;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetProcessNames(AppEntry app, string? resolvedTarget = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Specs.TryGetValue(app.Alias, out var spec))
        {
            foreach (var name in spec.ProcessNames)
            {
                names.Add(name);
            }
        }

        foreach (var candidate in new[] { resolvedTarget, app.Target })
        {
            if (string.IsNullOrWhiteSpace(candidate) || IsProtocol(candidate))
            {
                continue;
            }
            var processName = Path.GetFileNameWithoutExtension(candidate.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(processName))
            {
                names.Add(processName);
            }
        }
        return names.ToArray();
    }

    public static string GetSupportedActions(AppEntry app)
    {
        return Specs.TryGetValue(app.Alias, out var spec)
            ? string.Join(", ", spec.SupportedActions)
            : "activate";
    }

    public static bool SupportsAction(AppEntry app, string action)
    {
        return Specs.TryGetValue(app.Alias, out var spec)
            ? spec.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase)
            : action.Equals("activate", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildDiagnosticReport(IReadOnlyList<AppEntry> apps)
    {
        var lines = new List<string>
        {
            "LOOY Windows Controller application diagnostics",
            $"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Computer: {Environment.MachineName}",
            $"Windows: {Environment.OSVersion}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            "MCP endpoint and token are intentionally omitted.",
            string.Empty
        };

        foreach (var app in apps.OrderBy(item => item.Alias, StringComparer.OrdinalIgnoreCase))
        {
            var resolved = TryResolvePath(app);
            var processes = GetProcessNames(app, resolved);
            var runningWindows = new List<string>();
            foreach (var processName in processes)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            runningWindows.Add(
                                $"{process.ProcessName} pid={process.Id} window=0x{process.MainWindowHandle.ToInt64():X}");
                        }
                        catch
                        {
                            runningWindows.Add($"{processName} (details unavailable)");
                        }
                    }
                }
            }

            lines.Add($"[{app.Alias}] {app.DisplayName}");
            lines.Add($"Enabled: {app.Enabled}");
            lines.Add($"Configured target: {app.Target}");
            lines.Add($"Resolved target: {resolved ?? "NOT FOUND"}");
            lines.Add($"Supported actions: {GetSupportedActions(app)}");
            lines.Add($"Running windows: {(runningWindows.Count == 0 ? "none" : string.Join(" | ", runningWindows))}");
            lines.Add(string.Empty);
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static bool IsProtocol(string target) =>
        target.Contains(':') && !Path.IsPathRooted(target);

    private static IReadOnlyList<string> BuildExecutableNames(string target, AppSpec? spec)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spec is not null)
        {
            foreach (var executableName in spec.ExecutableNames)
            {
                names.Add(executableName);
            }
        }

        if (!IsProtocol(target))
        {
            var fileName = Path.GetFileName(target.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(fileName);
            }
        }
        return names.ToArray();
    }

    private static string? FindAppPath(string executableName)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                    var path = key?.GetValue(null) as string;
                    path = CleanExecutablePath(path);
                    if (path is not null)
                    {
                        return path;
                    }
                }
                catch
                {
                    // Try the next registry view.
                }
            }
        }
        return null;
    }

    private static string? FindFromUninstallRegistry(AppSpec spec)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallKey is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using var appKey = uninstallKey.OpenSubKey(subKeyName);
                        var displayName = appKey?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)
                            || !spec.DisplayNameTokens.Any(token => displayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var displayIcon = CleanExecutablePath(appKey?.GetValue("DisplayIcon") as string);
                        if (displayIcon is not null
                            && spec.ExecutableNames.Contains(Path.GetFileName(displayIcon), StringComparer.OrdinalIgnoreCase))
                        {
                            return displayIcon;
                        }

                        var installLocation = appKey?.GetValue("InstallLocation") as string;
                        if (string.IsNullOrWhiteSpace(installLocation))
                        {
                            continue;
                        }
                        foreach (var executableName in spec.ExecutableNames)
                        {
                            var candidate = Path.Combine(installLocation.Trim().Trim('"'), executableName);
                            if (File.Exists(candidate))
                            {
                                return Path.GetFullPath(candidate);
                            }
                        }
                    }
                }
                catch
                {
                    // Registry access can vary by Windows edition and policy.
                }
            }
        }
        return null;
    }

    private static string? FindOnSearchPath(string executableName)
    {
        var directories = new List<string>
        {
            Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private static string? CleanExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var path = Environment.ExpandEnvironmentVariables(value.Trim());
        if (path.StartsWith('"'))
        {
            var closingQuote = path.IndexOf('"', 1);
            path = closingQuote > 1 ? path[1..closingQuote] : path.Trim('"');
        }
        else
        {
            var commaIndex = path.LastIndexOf(',');
            if (commaIndex > 1 && int.TryParse(path[(commaIndex + 1)..], out _))
            {
                path = path[..commaIndex];
            }
        }
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }
}
