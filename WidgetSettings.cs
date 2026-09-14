using System.Text.Json;

namespace CodexUsageWidget;

internal sealed class WidgetSettings
{
    private static readonly object Sync = new();

    public int? X { get; set; }
    public int? Y { get; set; }
    public bool AlwaysOnTop { get; set; } = true;

    private static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageWidget");

    private static string SettingsPath =>
        Path.Combine(SettingsDirectory, "settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new WidgetSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<WidgetSettings>(json)
                ?? new WidgetSettings();
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not load widget settings", ex);
            return new WidgetSettings();
        }
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            lock (Sync)
            {
                File.WriteAllText(SettingsPath, json);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not save widget settings", ex);
        }
    }
}
