namespace CodexUsageWidget;

internal sealed class WidgetApplicationContext : ApplicationContext
{
    private readonly FloatingWidgetForm _widget;
    private readonly CodexUsageService _codexService = new();
    private readonly GeminiUsageService _geminiService = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;

    private bool _refreshing;
    private bool _refreshAfterCurrent;
    private bool _exiting;

    public WidgetApplicationContext()
    {
        _widget = new FloatingWidgetForm();
        _widget.RefreshRequested += async (_, _) => await RefreshAsync(forceGemini: true);
        _widget.ProviderSwitchRequested += async (_, _) => await SwitchProviderAsync();
        _widget.ExitRequested += (_, _) => ExitApp();
        _widget.OpenLogRequested += (_, _) => OpenLog();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 2 * 60 * 1000
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _countdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 30 * 1000
        };
        _countdownTimer.Tick += (_, _) => _widget.UpdateCountdowns();
        _countdownTimer.Start();

        _widget.Show();
        _ = RefreshAsync(forceGemini: true);
    }

    private async Task SwitchProviderAsync()
    {
        UsageProvider next = _widget.Provider == UsageProvider.Codex
            ? UsageProvider.Gemini
            : UsageProvider.Codex;

        _widget.SetProvider(next);

        if (_refreshing)
        {
            _refreshAfterCurrent = true;
            return;
        }

        await RefreshAsync(forceGemini: true);
    }

    private async Task RefreshAsync(bool forceGemini = false)
    {
        if (_refreshing || _exiting)
            return;

        _refreshing = true;
        UsageProvider provider = _widget.Provider;
        _widget.SetLoading();

        try
        {
            if (provider == UsageProvider.Codex)
            {
                UsageSnapshot snapshot = await _codexService.GetUsageAsync();

                if (_widget.Provider == provider)
                    _widget.SetSnapshot(snapshot);
            }
            else
            {
                GeminiUsageSnapshot snapshot =
                    await _geminiService.GetUsageAsync(forceGemini);

                if (_widget.Provider == provider)
                    _widget.SetGeminiSnapshot(snapshot);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"{provider} usage refresh failed", ex);

            if (_widget.Provider == provider)
                _widget.SetError(ex.Message);
        }
        finally
        {
            _refreshing = false;

            if (_refreshAfterCurrent && !_exiting)
            {
                _refreshAfterCurrent = false;
                _ = RefreshAsync(forceGemini: true);
            }
        }
    }

    private static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);

            if (!File.Exists(AppLog.LogPath))
            {
                File.WriteAllText(
                    AppLog.LogPath,
                    "No log entries yet." + Environment.NewLine);
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppLog.LogPath,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not open log",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        if (_exiting)
            return;

        _exiting = true;

        _refreshTimer.Stop();
        _countdownTimer.Stop();

        _codexService.Dispose();

        _refreshTimer.Dispose();
        _countdownTimer.Dispose();

        _widget.AllowClose();
        _widget.Close();
        _widget.Dispose();

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
            ExitApp();

        base.Dispose(disposing);
    }
}
