namespace CodexUsageWidget;

internal static class AppLog
{
    private static readonly object Sync = new();

    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageWidget");

    public static string LogPath =>
        Path.Combine(LogDirectory, "app.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            string line =
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}";

            if (ex is not null)
                line += Environment.NewLine + ex;

            lock (Sync)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
