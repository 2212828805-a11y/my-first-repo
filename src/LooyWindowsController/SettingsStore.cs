using System.Text.Json;

namespace Looy.WindowsController;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOOY",
        "WindowsController");

    public string ScreenshotDirectory => Path.Combine(DataDirectory, "Screenshots");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public ControllerSettings Load(Action<string>? log = null)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ScreenshotDirectory);

        if (!File.Exists(SettingsPath))
        {
            return new ControllerSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ControllerSettings>(json, JsonOptions)
                           ?? new ControllerSettings();
            Normalize(settings);
            if (settings.RememberEndpoint && !string.IsNullOrWhiteSpace(settings.ProtectedEndpoint))
            {
                settings.Endpoint = DpapiProtector.Unprotect(settings.ProtectedEndpoint);
            }

            return settings;
        }
        catch (Exception exception)
        {
            log?.Invoke($"读取设置失败，已恢复默认设置：{exception.Message}");
            return new ControllerSettings();
        }
    }

    public void Save(ControllerSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        settings.ProtectedEndpoint = settings.RememberEndpoint && !string.IsNullOrWhiteSpace(settings.Endpoint)
            ? DpapiProtector.Protect(settings.Endpoint.Trim())
            : string.Empty;

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static void Normalize(ControllerSettings settings)
    {
        settings.Permissions ??= PermissionKeys.CreateDefaults();
        foreach (var pair in PermissionKeys.CreateDefaults())
        {
            settings.Permissions.TryAdd(pair.Key, pair.Value);
        }

        settings.Apps ??= AppEntry.CreateDefaults();
        settings.Apps = settings.Apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Alias) && !string.IsNullOrWhiteSpace(app.Target))
            .GroupBy(app => app.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (settings.Apps.Count == 0)
        {
            settings.Apps = AppEntry.CreateDefaults();
        }
    }
}
