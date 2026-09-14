namespace CodexUsageWidget;

internal sealed class WidgetApplicationContext : ApplicationContext
{
    private readonly TaskbarWidgetForm _widget;
    private readonly CodexUsageService _service = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;

    private bool _refreshing;
    private bool _exiting;

    public WidgetApplicationContext()
    {
        _widget = new TaskbarWidgetForm();
        _widget.RefreshRequested += async (_, _) => await RefreshAsync();
        _widget.ExitRequested += (_, _) => ExitApp();
        _widget.OpenLogRequested += (_, _) => OpenLog();
        _widget.SnapRequested += (_, _) => _widget.SnapToTaskbar();

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
        _countdownTimer.Tick += (_, _) =>
        {
            _widget.UpdateCountdowns();
            _widget.KeepInsideTaskbar();
        };
        _countdownTimer.Start();

        _widget.Show();
        _widget.SnapToTaskbar();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _exiting)
            return;

        _refreshing = true;
        _widget.SetLoading();

        try
        {
            UsageSnapshot snapshot = await _service.GetUsageAsync();
            _widget.SetSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            AppLog.Write("Widget refresh failed", ex);
            _widget.SetError(ex.Message);
        }
        finally
        {
            _refreshing = false;
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

        _service.Dispose();

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
