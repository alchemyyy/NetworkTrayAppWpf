using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkTrayAppWpf;

public sealed class AppSettings
{
    public IconSettings Icon { get; set; } = new();
    public TraySettings Tray { get; set; } = new();

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetworkTrayIcon");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall through to create default
        }

        AppSettings settings = CreateWithSystemThemeDefaults();
        settings.Save();
        return settings;
    }

    private static AppSettings CreateWithSystemThemeDefaults()
    {
        bool isLightTheme = IsSystemLightTheme();
        return new AppSettings
        {
            Icon = IconSettings.CreateForTheme(isLightTheme)
        };
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            return value is 1;
        }
        catch
        {
            return false;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public static void OpenSettingsFile()
    {
        if (!File.Exists(SettingsFilePath))
        {
            new AppSettings().Save();
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SettingsFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if we can't open the file
        }
    }

    public static string SettingsFilePath { get; } = Path.Combine(SettingsFolder, "settings.json");
}

public sealed class IconSettings
{
    // Dark theme defaults
    private const string DarkConnected = "#FFFFFF";
    private const string DarkNoInternet = "#FFB900";
    private const string DarkDisconnected = "#808080";

    // Light theme defaults
    private const string LightConnected = "#000000";
    private const string LightNoInternet = "#996600";
    private const string LightDisconnected = "#666666";

    public string ConnectedColor { get; set; } = DarkConnected;
    public string NoInternetColor { get; set; } = DarkNoInternet;
    public string DisconnectedColor { get; set; } = DarkDisconnected;
    public bool ApplyColorsToLightTheme { get; set; }

    public static IconSettings CreateForTheme(bool isLightTheme)
    {
        return isLightTheme
            ? new IconSettings
            {
                ConnectedColor = LightConnected,
                NoInternetColor = LightNoInternet,
                DisconnectedColor = LightDisconnected,
                ApplyColorsToLightTheme = true
            }
            : new IconSettings
            {
                ConnectedColor = DarkConnected,
                NoInternetColor = DarkNoInternet,
                DisconnectedColor = DarkDisconnected,
                ApplyColorsToLightTheme = false
            };
    }
}

public sealed class TraySettings
{
    public FlyoutStyle FlyoutStyle { get; set; } = FlyoutStyle.Windows10;
    public AdapterSettingsStyle AdapterSettingsStyle { get; set; } = AdapterSettingsStyle.Explorer;
}
