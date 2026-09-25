using System.Text.Json;
using System.Text.Json.Serialization;

namespace Looy.WindowsController;

internal static class PermissionKeys
{
    public const string SystemStatus = "system_status";
    public const string Applications = "applications";
    public const string Web = "web";
    public const string Keyboard = "keyboard";
    public const string Mouse = "mouse";
    public const string Media = "media";
    public const string SystemControl = "system_control";
    public const string Clipboard = "clipboard";
    public const string ScreenRecognition = "screen_recognition";
    public const string Screenshot = "screenshot";

    public static Dictionary<string, bool> CreateDefaults() => new()
    {
        [SystemStatus] = true,
        [Applications] = true,
        [Web] = true,
        [Keyboard] = false,
        [Mouse] = false,
        [Media] = true,
        [SystemControl] = false,
        [Clipboard] = false,
        [ScreenRecognition] = false,
        [Screenshot] = false
    };
}

internal sealed class ControllerSettings
{
    public string ProtectedEndpoint { get; set; } = string.Empty;

    [JsonIgnore]
    public string Endpoint { get; set; } = string.Empty;

    public bool RememberEndpoint { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool AutoConnect { get; set; }
    public Dictionary<string, bool> Permissions { get; set; } = PermissionKeys.CreateDefaults();
    public List<AppEntry> Apps { get; set; } = AppEntry.CreateDefaults();
}

internal sealed class AppEntry
{
    public string Alias { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public AppEntry Clone() => new()
    {
        Alias = Alias,
        DisplayName = DisplayName,
        Target = Target,
        Enabled = Enabled
    };

    public static List<AppEntry> CreateDefaults() =>
    [
        new() { Alias = "notepad", DisplayName = "记事本", Target = "notepad.exe" },
        new() { Alias = "calculator", DisplayName = "计算器", Target = "calc.exe" },
        new() { Alias = "explorer", DisplayName = "文件资源管理器", Target = "explorer.exe" },
        new() { Alias = "settings", DisplayName = "Windows 设置", Target = "ms-settings:" },
        new() { Alias = "edge", DisplayName = "Microsoft Edge", Target = "microsoft-edge:" },
        new() { Alias = "chrome", DisplayName = "Google Chrome", Target = "chrome.exe", Enabled = false },
        new() { Alias = "wechat", DisplayName = "微信", Target = "WeChat.exe", Enabled = false },
        new() { Alias = "qq", DisplayName = "QQ", Target = "QQ.exe", Enabled = false },
        new() { Alias = "douyin", DisplayName = "抖音", Target = "Douyin.exe", Enabled = false },
        new() { Alias = "netease_music", DisplayName = "网易云音乐", Target = "cloudmusic.exe", Enabled = false },
        new() { Alias = "vscode", DisplayName = "Visual Studio Code", Target = "Code.exe", Enabled = false }
    ];
}

internal sealed class ToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public required object InputSchema { get; init; }
}

internal readonly record struct ToolExecutionResult(bool Success, string Message)
{
    public static ToolExecutionResult Ok(string message) => new(true, message);
    public static ToolExecutionResult Fail(string message) => new(false, message);
}

internal enum EndpointConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Stopped
}

internal delegate Task<ToolExecutionResult> ToolExecutor(
    string toolName,
    JsonElement arguments,
    CancellationToken cancellationToken);
