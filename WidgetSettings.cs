using System.Text.Json;

namespace CodexUsageWidget;

internal sealed record WidgetSettings(int? TaskbarOffsetX)
{
    private static readonly object Sync = new();

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
                return new WidgetSettings(null);

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<WidgetSettings>(json)
                ?? new WidgetSettings(null);
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not load widget settings", ex);
            return new WidgetSettings(null);
        }
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
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
