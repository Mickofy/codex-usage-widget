using System.Drawing.Drawing2D;

namespace CodexUsageWidget;

internal sealed class FloatingWidgetForm : Form
{
    private const int WidgetWidth = 258;
    private const int WidgetHeight = 76;
    private const int ScreenMargin = 18;

    private readonly Label _statusLabel = new();
    private readonly Label _fiveHourValue = new();
    private readonly Label _weeklyValue = new();
    private readonly Label _fiveHourReset = new();
    private readonly Label _weeklyReset = new();
    private readonly ToolTip _toolTip = new();
    private readonly ToolStripMenuItem _alwaysOnTopItem;

    private UsageSnapshot? _snapshot;
    private bool _allowClose;
    private bool _dragging;
    private bool _restoringSettings;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

    public event EventHandler? RefreshRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? OpenLogRequested;

    public FloatingWidgetForm()
    {
        Text = "Codex Usage Widget";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Width = WidgetWidth;
        Height = WidgetHeight;
        MinimumSize = new Size(WidgetWidth, WidgetHeight);
        MaximumSize = new Size(WidgetWidth, WidgetHeight);
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Opacity = 0.97;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));

        _alwaysOnTopItem = new ToolStripMenuItem("Always on top")
        {
            Checked = true,
            CheckOnClick = true
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) => SetAlwaysOnTop(_alwaysOnTopItem.Checked);
        menu.Items.Add(_alwaysOnTopItem);

        menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 7),
            BackColor = BackColor,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 15));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "CODEX",
            AutoSize = true,
            ForeColor = Color.FromArgb(212, 212, 216),
            Font = new Font("Segoe UI Semibold", 8f),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left
        };

        _statusLabel.Text = "●";
        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(161, 161, 170);
        _statusLabel.Font = new Font("Segoe UI", 7.5f);
        _statusLabel.Margin = Padding.Empty;
        _statusLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_statusLabel, 1, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var divider = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(63, 63, 70),
            Margin = new Padding(0, 5, 0, 5)
        };

        metrics.Controls.Add(BuildMetric("5H", _fiveHourValue), 0, 0);
        metrics.Controls.Add(divider, 1, 0);
        metrics.Controls.Add(BuildMetric("WEEK", _weeklyValue), 2, 0);

        var resets = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        resets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        resets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        ConfigureResetLabel(_fiveHourReset);
        ConfigureResetLabel(_weeklyReset);
        resets.Controls.Add(_fiveHourReset, 0, 0);
        resets.Controls.Add(_weeklyReset, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(metrics, 0, 1);
        root.Controls.Add(resets, 0, 2);
        Controls.Add(root);

        AttachDragRecursively(this);

        Shown += (_, _) => RestorePosition();
        FormClosing += (_, e) =>
        {
            if (_allowClose)
                return;

            e.Cancel = true;
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            const int WsExToolWindow = 0x00000080;

            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            cp.ExStyle |= WsExToolWindow;
            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedRegion();
    }

    private Control BuildMetric(string labelText, Label valueLabel)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var metricLabel = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = Color.FromArgb(161, 161, 170),
            Font = new Font("Segoe UI Semibold", 8f),
            Margin = new Padding(0, 7, 6, 0),
            Anchor = AnchorStyles.Left
        };

        valueLabel.Text = "--";
        valueLabel.AutoSize = true;
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = new Font("Segoe UI Semibold", 15f);
        valueLabel.Margin = Padding.Empty;
        valueLabel.Anchor = AnchorStyles.Left;

        panel.Controls.Add(metricLabel, 0, 0);
        panel.Controls.Add(valueLabel, 1, 0);
        return panel;
    }

    private static void ConfigureResetLabel(Label label)
    {
        label.Text = "reset --";
        label.AutoSize = true;
        label.ForeColor = Color.FromArgb(161, 161, 170);
        label.Font = new Font("Segoe UI", 7.25f);
        label.Margin = Padding.Empty;
        label.Anchor = AnchorStyles.Left;
    }

    public void SetLoading()
    {
        _statusLabel.ForeColor = Color.FromArgb(250, 204, 21);
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _fiveHourValue.Text = FormatPercent(snapshot.FiveHour);
        _weeklyValue.Text = FormatPercent(snapshot.Weekly);
        _statusLabel.ForeColor = Color.FromArgb(74, 222, 128);

        UpdateCountdowns();
        UpdateToolTip(snapshot);
    }

    public void SetError(string message)
    {
        _statusLabel.ForeColor = Color.FromArgb(248, 113, 113);
        _toolTip.SetToolTip(this, message);
    }

    public void UpdateCountdowns()
    {
        if (_snapshot is null)
            return;

        _fiveHourReset.Text = FormatReset(_snapshot.FiveHour);
        _weeklyReset.Text = FormatReset(_snapshot.Weekly);
    }

    public void AllowClose() => _allowClose = true;

    public void RestorePosition()
    {
        WidgetSettings settings = WidgetSettings.Load();

        _restoringSettings = true;
        _alwaysOnTopItem.Checked = settings.AlwaysOnTop;
        TopMost = settings.AlwaysOnTop;
        _restoringSettings = false;

        if (settings.X is int x && settings.Y is int y)
        {
            Rectangle candidate = new(x, y, Width, Height);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(candidate)))
            {
                Location = ClampToVisibleArea(candidate.Location);
                return;
            }
        }

        ResetPosition(save: false);
    }

    public void ResetPosition() => ResetPosition(save: true);

    private void ResetPosition(bool save)
    {
        Screen screen = Screen.FromPoint(Cursor.Position);
        Rectangle area = screen.WorkingArea;

        Location = new Point(
            area.Right - Width - ScreenMargin,
            area.Bottom - Height - ScreenMargin);

        if (save)
            SaveCurrentSettings();
    }

    private void SetAlwaysOnTop(bool enabled)
    {
        TopMost = enabled;
        if (!_restoringSettings)
            SaveCurrentSettings();
    }

    private void SaveCurrentSettings()
    {
        WidgetSettings.Save(new WidgetSettings
        {
            X = Left,
            Y = Top,
            AlwaysOnTop = TopMost
        });
    }

    private static string FormatPercent(UsageWindow? window)
    {
        return window is null ? "N/A" : $"{window.RemainingPercent:0.#}%";
    }

    private static string FormatReset(UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return "reset --";

        TimeSpan remaining = window.ResetsAt.Value - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "reset due";

        if (remaining.TotalDays >= 1)
            return $"reset {(int)remaining.TotalDays}d {remaining.Hours}h";

        if (remaining.TotalHours >= 1)
            return $"reset {(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"reset {Math.Max(0, remaining.Minutes)}m";
    }

    private void UpdateToolTip(UsageSnapshot snapshot)
    {
        string five = snapshot.FiveHour is null
            ? "5-hour: unavailable"
            : $"5-hour: {snapshot.FiveHour.RemainingPercent:0.#}% remaining · reset {FormatDate(snapshot.FiveHour.ResetsAt)}";

        string week = snapshot.Weekly is null
            ? "Weekly: unavailable"
            : $"Weekly: {snapshot.Weekly.RemainingPercent:0.#}% remaining · reset {FormatDate(snapshot.Weekly.ResetsAt)}";

        string text = $"{five}\n{week}\nUpdated {snapshot.RetrievedAt:h:mm:ss tt}";
        ApplyToolTipRecursively(this, text);
    }

    private void ApplyToolTipRecursively(Control control, string text)
    {
        _toolTip.SetToolTip(control, text);
        foreach (Control child in control.Controls)
            ApplyToolTipRecursively(child, text);
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("MMM d, h:mm tt") ?? "unknown";
    }

    private void ApplyRoundedRegion()
    {
        const int radius = 12;
        int diameter = radius * 2;

        using var path = new GraphicsPath();
        Rectangle rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        if (rect.Width <= diameter || rect.Height <= diameter)
            return;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        Region?.Dispose();
        Region = new Region(path);
    }

    private void AttachDragRecursively(Control control)
    {
        control.MouseDown += DragMouseDown;
        control.MouseMove += DragMouseMove;
        control.MouseUp += DragMouseUp;

        foreach (Control child in control.Controls)
            AttachDragRecursively(child);
    }

    private void DragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _dragging = true;
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
        Cursor = Cursors.SizeAll;
    }

    private void DragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        Point cursor = Cursor.Position;
        Location = new Point(
            _dragStartLocation.X + cursor.X - _dragStartCursor.X,
            _dragStartLocation.Y + cursor.Y - _dragStartCursor.Y);
    }

    private void DragMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
            return;

        _dragging = false;
        Cursor = Cursors.Default;
        Location = ClampToVisibleArea(Location);
        SaveCurrentSettings();
    }

    private Point ClampToVisibleArea(Point location)
    {
        Rectangle widgetRect = new(location.X, location.Y, Width, Height);
        Screen screen = Screen.FromRectangle(widgetRect);
        Rectangle area = screen.WorkingArea;

        int x = Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height));
        return new Point(x, y);
    }
}
