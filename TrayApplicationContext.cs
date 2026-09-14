using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexUsageWidget;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly UsageForm _form;
    private readonly CodexUsageService _service = new();

    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;

    private Icon? _ownedDynamicIcon;

    private bool _refreshing;
    private bool _exiting;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(
            "Open",
            null,
            (_, _) => ToggleWindow());

        menu.Items.Add(
            "Refresh",
            null,
            async (_, _) => await RefreshAsync());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(
            "Open log",
            null,
            (_, _) => OpenLog());

        menu.Items.Add(
            "Exit",
            null,
            (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Visible = true,
            Icon = SystemIcons.Information,
            Text = "Codex Usage — loading…",
            ContextMenuStrip = menu
        };

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleWindow();
        };

        _form = new UsageForm();

        _form.RefreshRequested += async (_, _) =>
            await RefreshAsync();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 2 * 60 * 1000
        };

        _refreshTimer.Tick += async (_, _) =>
            await RefreshAsync();

        _refreshTimer.Start();

        _countdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 30 * 1000
        };

        _countdownTimer.Tick += (_, _) =>
            _form.UpdateCountdowns();

        _countdownTimer.Start();

        // V3 starts quietly in the tray.
        _ = RefreshAsync();
    }

    private void ToggleWindow()
    {
        if (_form.Visible)
        {
            _form.Hide();
            return;
        }

        PositionNearTray();
        _form.Show();
        _form.BringToFront();
        _form.Activate();
    }

    private void PositionNearTray()
    {
        Rectangle area =
            Screen.GetWorkingArea(Cursor.Position);

        int x =
            Math.Max(
                area.Left,
                area.Right - _form.Width - 10);

        int y =
            Math.Max(
                area.Top,
                area.Bottom - _form.Height - 10);

        _form.Location = new Point(x, y);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _exiting)
            return;

        _refreshing = true;

        if (_form.Visible)
            _form.SetLoading();

        try
        {
            UsageSnapshot snapshot =
                await _service.GetUsageAsync();

            _form.SetSnapshot(snapshot);
            UpdateTray(snapshot);
        }
        catch (Exception ex)
        {
            AppLog.Write("Refresh failed", ex);

            _form.SetError(ex);

            _tray.Text =
                "Codex Usage — refresh failed";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateTray(
        UsageSnapshot snapshot)
    {
        string five =
            snapshot.FiveHour is null
                ? "N/A"
                : $"{snapshot.FiveHour.RemainingPercent:0.#}%";

        string week =
            snapshot.Weekly is null
                ? "N/A"
                : $"{snapshot.Weekly.RemainingPercent:0.#}%";

        string tooltip =
            $"Codex | 5H {five} | W {week}";

        _tray.Text =
            tooltip.Length <= 63
                ? tooltip
                : tooltip[..63];

        double displayPercent =
            snapshot.Weekly?.RemainingPercent
            ?? snapshot.FiveHour?.RemainingPercent
            ?? 0;

        Icon icon =
            CreatePercentIcon(displayPercent);

        Icon? oldIcon =
            _ownedDynamicIcon;

        _tray.Icon = icon;
        _ownedDynamicIcon = icon;

        oldIcon?.Dispose();
    }

    private static Icon CreatePercentIcon(
        double percent)
    {
        int rounded = Math.Clamp(
            (int)Math.Round(percent),
            0,
            100);

        using var bitmap =
            new Bitmap(32, 32);

        using Graphics graphics =
            Graphics.FromImage(bitmap);

        graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        graphics.Clear(Color.Transparent);

        using var background =
            new SolidBrush(
                Color.FromArgb(35, 35, 35));

        graphics.FillEllipse(
            background,
            1,
            1,
            30,
            30);

        string text = rounded.ToString();

        float fontSize =
            text.Length switch
            {
                1 => 16f,
                2 => 13f,
                _ => 9.5f
            };

        using var font =
            new Font(
                "Segoe UI",
                fontSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel);

        using var brush =
            new SolidBrush(Color.White);

        SizeF size =
            graphics.MeasureString(text, font);

        float x =
            (32f - size.Width) / 2f;

        float y =
            (32f - size.Height) / 2f - 1f;

        graphics.DrawString(
            text,
            font,
            brush,
            x,
            y);

        IntPtr hIcon =
            bitmap.GetHicon();

        try
        {
            using Icon temp =
                Icon.FromHandle(hIcon);

            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(
                AppLog.LogDirectory);

            if (!File.Exists(AppLog.LogPath))
            {
                File.WriteAllText(
                    AppLog.LogPath,
                    "No log entries yet." +
                    Environment.NewLine);
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
                "Could not open log");
        }
    }

    private void ExitApp()
    {
        if (_exiting)
            return;

        _exiting = true;

        _refreshTimer.Stop();
        _countdownTimer.Stop();

        _tray.Visible = false;

        _service.Dispose();

        _form.AllowClose();
        _form.Close();

        _ownedDynamicIcon?.Dispose();

        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();

        _refreshTimer.Dispose();
        _countdownTimer.Dispose();

        ExitThread();
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing && !_exiting)
            ExitApp();

        base.Dispose(disposing);
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(
        IntPtr hIcon);
}
