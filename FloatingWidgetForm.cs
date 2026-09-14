using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CodexUsageWidget;

internal sealed class FloatingWidgetForm : Form
{
    private const int WidgetWidth = 230;
    private const int WidgetHeight = 76;
    private const int ScreenMargin = 18;

    private readonly ToolTip _toolTip = new();
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ContextMenuStrip _menu;
    private readonly Font _labelFont = new("Segoe UI Semibold", 9.5f, FontStyle.Regular);
    private readonly Font _valueFont = new("Segoe UI Semibold", 13f, FontStyle.Bold);

    private UsageSnapshot? _snapshot;
    private string? _errorMessage;
    private WidgetState _state = WidgetState.Loading;
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
        ClientSize = new Size(WidgetWidth, WidgetHeight);
        MinimumSize = new Size(WidgetWidth, WidgetHeight);
        MaximumSize = new Size(WidgetWidth, WidgetHeight);
        BackColor = Color.FromArgb(24, 25, 28);
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        Opacity = 0.98;
        DoubleBuffered = true;

        _toolTip.InitialDelay = 300;
        _toolTip.ReshowDelay = 100;
        _toolTip.AutoPopDelay = 12_000;
        _toolTip.ShowAlways = true;
        _toolTip.SetToolTip(this, "Loading Codex usage…");

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

        MouseDown += DragMouseDown;
        MouseMove += DragMouseMove;
        MouseUp += DragMouseUp;
        MouseEnter += (_, _) => RefreshHoverText();

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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        DrawCardBorder(e.Graphics);
        DrawDivider(e.Graphics);
        DrawCodexMark(e.Graphics);
        DrawMetricRow(e.Graphics, "5h Usage", FormatPercent(_snapshot?.FiveHour), 15f);
        DrawMetricRow(e.Graphics, "Weekly", FormatPercent(_snapshot?.Weekly), 43f);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    private void DrawCardBorder(Graphics graphics)
    {
        using var borderPen = new Pen(Color.FromArgb(70, 74, 82), 1.1f);
        using GraphicsPath path = RoundedRect(
            new RectangleF(0.7f, 0.7f, ClientSize.Width - 1.4f, ClientSize.Height - 1.4f),
            14f);

        graphics.DrawPath(borderPen, path);
    }

    private void DrawDivider(Graphics graphics)
    {
        using var pen = new Pen(Color.FromArgb(61, 64, 70), 1f);
        graphics.DrawLine(pen, 67f, 14f, 67f, ClientSize.Height - 14f);
    }

    private void DrawCodexMark(Graphics graphics)
    {
        Color color = _state switch
        {
            WidgetState.Live => Color.FromArgb(239, 239, 242),
            WidgetState.Loading => Color.FromArgb(170, 171, 178),
            WidgetState.Error => Color.FromArgb(248, 113, 113),
            _ => Color.White
        };

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(34f, 38f);

        using var pen = new Pen(color, 2.05f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        // Six overlapping loops create a compact knot-like mark without
        // requiring an external image asset.
        for (int i = 0; i < 6; i++)
        {
            graphics.RotateTransform(60f);
            graphics.DrawArc(pen, -5.5f, -16f, 11f, 21f, 205f, 250f);
        }

        graphics.Restore(state);
    }

    private void DrawMetricRow(Graphics graphics, string label, string value, float y)
    {
        using var labelBrush = new SolidBrush(Color.FromArgb(160, 162, 169));
        using var valueBrush = new SolidBrush(Color.FromArgb(244, 244, 246));

        graphics.DrawString(label, _labelFont, labelBrush, 82f, y);

        SizeF valueSize = graphics.MeasureString(value, _valueFont);
        float valueX = ClientSize.Width - 14f - valueSize.Width;
        graphics.DrawString(value, _valueFont, valueBrush, valueX, y - 3f);
    }

    public void SetLoading()
    {
        _state = WidgetState.Loading;
        _errorMessage = null;
        Invalidate();
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _errorMessage = null;
        _state = WidgetState.Live;
        RefreshHoverText();
        Invalidate();
    }

    public void SetError(string message)
    {
        _errorMessage = message;
        _state = WidgetState.Error;
        RefreshHoverText();
        Invalidate();
    }

    public void UpdateCountdowns()
    {
        RefreshHoverText();
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

    private void RefreshHoverText()
    {
        string text;

        if (_state == WidgetState.Error)
        {
            text = $"Codex usage unavailable\n{_errorMessage ?? "Unknown error"}";
        }
        else if (_snapshot is null)
        {
            text = "Loading Codex usage…";
        }
        else
        {
            text = string.Join(
                Environment.NewLine,
                BuildResetLine("5h reset", _snapshot.FiveHour),
                BuildResetLine("Weekly reset", _snapshot.Weekly),
                $"Updated: {_snapshot.RetrievedAt:h:mm:ss tt}");
        }

        _toolTip.SetToolTip(this, text);
    }

    private static string BuildResetLine(string label, UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return $"{label}: unavailable";

        string countdown = FormatCountdown(window.ResetsAt.Value);
        return $"{label}: {window.ResetsAt.Value:MMM d, h:mm tt} ({countdown})";
    }

    private static string FormatCountdown(DateTimeOffset resetAt)
    {
        TimeSpan remaining = resetAt - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "due now";

        if (remaining.TotalDays >= 1)
            return $"in {(int)remaining.TotalDays}d {remaining.Hours}h {remaining.Minutes}m";

        if (remaining.TotalHours >= 1)
            return $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"in {Math.Max(0, remaining.Minutes)}m";
    }

    private static string FormatPercent(UsageWindow? window)
    {
        return window is null ? "--" : $"{window.RemainingPercent:0.#}%";
    }

    private void ApplyRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        using GraphicsPath path = RoundedRect(
            new RectangleF(0, 0, ClientSize.Width, ClientSize.Height),
            14f);

        Region?.Dispose();
        Region = new Region(path);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
            _valueFont.Dispose();
            _toolTip.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private enum WidgetState
    {
        Loading,
        Live,
        Error
    }
}
