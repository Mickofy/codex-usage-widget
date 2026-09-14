using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexUsageWidget;

internal sealed class TaskbarWidgetForm : Form
{
    private readonly Label _statusLabel = new();
    private readonly Label _fiveHourValue = new();
    private readonly Label _weeklyValue = new();
    private readonly Label _fiveHourReset = new();
    private readonly Label _weeklyReset = new();
    private readonly ToolTip _toolTip = new();

    private UsageSnapshot? _snapshot;
    private bool _allowClose;

    public event EventHandler? RefreshRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? OpenLogRequested;
    public event EventHandler? SnapRequested;

    public TaskbarWidgetForm()
    {
        Text = "Codex Usage Widget";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Width = 312;
        Height = 92;
        BackColor = Color.FromArgb(24, 24, 27);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Opacity = 0.97;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Snap to taskbar", null, (_, _) => SnapRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 9, 12, 9),
            BackColor = BackColor,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));

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
            Text = "CODEX USAGE",
            AutoSize = true,
            ForeColor = Color.FromArgb(212, 212, 216),
            Font = new Font("Segoe UI Semibold", 8.5f),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left
        };

        _statusLabel.Text = "LOADING";
        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(161, 161, 170);
        _statusLabel.Font = new Font("Segoe UI Semibold", 7.5f);
        _statusLabel.Margin = Padding.Empty;
        _statusLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_statusLabel, 1, 0);

        var values = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        values.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var divider = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(63, 63, 70),
            Margin = new Padding(0, 5, 0, 5)
        };

        values.Controls.Add(BuildMetric("5H", _fiveHourValue), 0, 0);
        values.Controls.Add(divider, 1, 0);
        values.Controls.Add(BuildMetric("WEEK", _weeklyValue), 2, 0);

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
        root.Controls.Add(values, 0, 1);
        root.Controls.Add(resets, 0, 2);

        Controls.Add(root);

        AttachDragHandler(this);
        AttachDragHandler(root);
        AttachDragHandler(header);
        AttachDragHandler(values);
        AttachDragHandler(title);
        AttachDragHandler(_statusLabel);
        AttachDragHandler(_fiveHourValue);
        AttachDragHandler(_weeklyValue);
        AttachDragHandler(_fiveHourReset);
        AttachDragHandler(_weeklyReset);

        DoubleClick += (_, _) => SnapRequested?.Invoke(this, EventArgs.Empty);

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
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
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
            Font = new Font("Segoe UI Semibold", 9f),
            Margin = new Padding(0, 7, 7, 0),
            Anchor = AnchorStyles.Left
        };

        valueLabel.Text = "--";
        valueLabel.AutoSize = true;
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = new Font("Segoe UI Semibold", 17f);
        valueLabel.Margin = new Padding(0, 0, 0, 0);
        valueLabel.Anchor = AnchorStyles.Left;

        panel.Controls.Add(metricLabel, 0, 0);
        panel.Controls.Add(valueLabel, 1, 0);

        AttachDragHandler(panel);
        AttachDragHandler(metricLabel);

        return panel;
    }

    private static void ConfigureResetLabel(Label label)
    {
        label.Text = "reset --";
        label.AutoSize = true;
        label.ForeColor = Color.FromArgb(161, 161, 170);
        label.Font = new Font("Segoe UI", 7.5f);
        label.Margin = Padding.Empty;
        label.Anchor = AnchorStyles.Left;
    }

    public void SetLoading()
    {
        _statusLabel.Text = "REFRESHING";
        _statusLabel.ForeColor = Color.FromArgb(161, 161, 170);
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;

        _fiveHourValue.Text = FormatPercent(snapshot.FiveHour);
        _weeklyValue.Text = FormatPercent(snapshot.Weekly);

        _statusLabel.Text = "LIVE";
        _statusLabel.ForeColor = Color.FromArgb(134, 239, 172);

        UpdateCountdowns();
        UpdateToolTip(snapshot);
    }

    public void SetError(string message)
    {
        _statusLabel.Text = "ERROR";
        _statusLabel.ForeColor = Color.FromArgb(252, 165, 165);
        _toolTip.SetToolTip(this, message);
    }

    public void UpdateCountdowns()
    {
        if (_snapshot is null)
            return;

        _fiveHourReset.Text = FormatReset(_snapshot.FiveHour);
        _weeklyReset.Text = FormatReset(_snapshot.Weekly);
    }

    public void SnapToTaskbar()
    {
        Screen screen = Screen.FromPoint(Cursor.Position);
        Rectangle area = screen.WorkingArea;

        int x = Math.Max(area.Left, area.Right - Width - 12);
        int y = Math.Max(area.Top, area.Bottom - Height - 10);

        Location = new Point(x, y);
    }

    public void AllowClose() => _allowClose = true;

    private static string FormatPercent(UsageWindow? window)
    {
        if (window is null)
            return "N/A";

        return $"{window.RemainingPercent:0.#}%";
    }

    private static string FormatReset(UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return "reset unavailable";

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

        _toolTip.SetToolTip(this, text);
        _toolTip.SetToolTip(_fiveHourValue, text);
        _toolTip.SetToolTip(_weeklyValue, text);
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("MMM d, h:mm tt") ?? "unknown";
    }

    private void ApplyRoundedRegion()
    {
        const int radius = 16;
        int diameter = radius * 2;

        using var path = new GraphicsPath();
        Rectangle rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        Region?.Dispose();
        Region = new Region(path);
    }

    private void AttachDragHandler(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        };
    }

    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        int wParam,
        int lParam);
}
