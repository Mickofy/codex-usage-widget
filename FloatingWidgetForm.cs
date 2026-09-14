using System.Drawing.Drawing2D;

namespace CodexUsageWidget;

internal sealed class FloatingWidgetForm : Form
{
    private const int MetricWidth = 94;
    private const int ScreenMargin = 18;

    private readonly CodeIconControl _codeIcon = new();
    private readonly Label _fiveHourValue = new();
    private readonly Label _weeklyValue = new();
    private readonly Label _fiveHourReset = new();
    private readonly Label _weeklyReset = new();
    private readonly ToolTip _toolTip = new();
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ContextMenuStrip _menu;

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
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Opacity = 0.98;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));

        _alwaysOnTopItem = new ToolStripMenuItem("Always on top")
        {
            Checked = true,
            CheckOnClick = true
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) => SetAlwaysOnTop(_alwaysOnTopItem.Checked);
        _menu.Items.Add(_alwaysOnTopItem);

        _menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Open log", null, (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = _menu;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = BackColor,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MetricWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MetricWidth));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureCodeIcon();
        root.Controls.Add(_codeIcon, 0, 0);
        root.SetRowSpan(_codeIcon, 2);

        Control fiveMetric = BuildMetric("5H", _fiveHourValue);
        Control weeklyMetric = BuildMetric("W", _weeklyValue);
        root.Controls.Add(fiveMetric, 1, 0);
        root.Controls.Add(weeklyMetric, 3, 0);

        var divider = new Panel
        {
            Width = 1,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(58, 58, 62),
            Margin = new Padding(0, 2, 0, 2)
        };
        root.Controls.Add(divider, 2, 0);
        root.SetRowSpan(divider, 2);

        ConfigureResetLabel(_fiveHourReset);
        ConfigureResetLabel(_weeklyReset);
        root.Controls.Add(_fiveHourReset, 1, 1);
        root.Controls.Add(_weeklyReset, 3, 1);

        Controls.Add(root);

        ApplyInteractionRecursively(this);

        Shown += (_, _) =>
        {
            PerformLayout();
            RestorePosition();
        };

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

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    private void ConfigureCodeIcon()
    {
        _codeIcon.Size = new Size(22, 22);
        _codeIcon.Margin = new Padding(1, 5, 7, 0);
        _codeIcon.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _codeIcon.BackColor = BackColor;
        _codeIcon.StatusColor = Color.FromArgb(161, 161, 170);
    }

    private Control BuildMetric(string labelText, Label valueLabel)
    {
        var line = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BackColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var metricLabel = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 160),
            Font = new Font("Segoe UI Semibold", 7.75f),
            Margin = new Padding(0, 7, 5, 0)
        };

        valueLabel.Text = "--";
        valueLabel.AutoSize = true;
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = new Font("Segoe UI Semibold", 13.5f);
        valueLabel.Margin = Padding.Empty;

        line.Controls.Add(metricLabel);
        line.Controls.Add(valueLabel);
        return line;
    }

    private static void ConfigureResetLabel(Label label)
    {
        label.Text = "--";
        label.AutoSize = true;
        label.ForeColor = Color.FromArgb(145, 145, 152);
        label.Font = new Font("Segoe UI", 7.25f);
        label.Margin = new Padding(0, 1, 0, 0);
        label.Anchor = AnchorStyles.Left;
    }

    public void SetLoading()
    {
        _codeIcon.StatusColor = Color.FromArgb(250, 204, 21);
        _codeIcon.Invalidate();
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _fiveHourValue.Text = FormatPercent(snapshot.FiveHour);
        _weeklyValue.Text = FormatPercent(snapshot.Weekly);
        _codeIcon.StatusColor = Color.FromArgb(74, 222, 128);
        _codeIcon.Invalidate();

        UpdateCountdowns();
        UpdateToolTip(snapshot);
    }

    public void SetError(string message)
    {
        _codeIcon.StatusColor = Color.FromArgb(248, 113, 113);
        _codeIcon.Invalidate();
        ApplyToolTipRecursively(this, message);
    }

    public void UpdateCountdowns()
    {
        if (_snapshot is null)
            return;

        _fiveHourReset.Text = FormatCountdown(_snapshot.FiveHour);
        _weeklyReset.Text = FormatCountdown(_snapshot.Weekly);
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

    private static string FormatCountdown(UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return "--";

        TimeSpan remaining = window.ResetsAt.Value - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "due";

        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";

        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"{Math.Max(0, remaining.Minutes)}m";
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
        const int radius = 11;
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

    private void ApplyInteractionRecursively(Control control)
    {
        control.ContextMenuStrip = _menu;
        control.MouseDown += DragMouseDown;
        control.MouseMove += DragMouseMove;
        control.MouseUp += DragMouseUp;

        foreach (Control child in control.Controls)
            ApplyInteractionRecursively(child);
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

    private sealed class CodeIconControl : Control
    {
        public Color StatusColor { get; set; } = Color.FromArgb(161, 161, 170);

        public CodeIconControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var borderPen = new Pen(Color.FromArgb(92, 92, 98), 1.2f);
            using var glyphPen = new Pen(Color.FromArgb(224, 224, 228), 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var statusBrush = new SolidBrush(StatusColor);

            RectangleF box = new(1.5f, 2.5f, Width - 5f, Height - 5f);
            using var path = RoundedRect(box, 4f);
            e.Graphics.DrawPath(borderPen, path);

            float midY = Height / 2f;
            e.Graphics.DrawLine(glyphPen, 6f, midY - 3f, 9f, midY);
            e.Graphics.DrawLine(glyphPen, 9f, midY, 6f, midY + 3f);
            e.Graphics.DrawLine(glyphPen, 11.5f, midY + 3f, 15.5f, midY + 3f);

            e.Graphics.FillEllipse(statusBrush, Width - 6f, 1f, 5f, 5f);
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
