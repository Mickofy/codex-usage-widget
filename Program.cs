namespace CodexUsageWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) =>
        {
            AppLog.Write("UI exception", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppLog.Write("Unhandled exception", ex);
        };

        try
        {
            Application.Run(new WidgetApplicationContext());
        }
        catch (Exception ex)
        {
            AppLog.Write("Fatal startup error", ex);
            MessageBox.Show(
                $"{ex.Message}\n\nLog:\n{AppLog.LogPath}",
                "Codex Usage Widget",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
