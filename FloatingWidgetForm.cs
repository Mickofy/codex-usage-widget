using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexUsageWidget;

internal sealed class FloatingWidgetForm : Form
{
    private const int WidgetWidth = 230;
    private const int WidgetHeight = 76;
    private const int ScreenMargin = 18;
    private const double WidgetOpacity = 0.90;
    private const float CornerRadius = 15f;

    private static readonly Color CardBackground = Color.FromArgb(18, 22, 28);
    private static readonly Color CardBorder = Color.FromArgb(61, 66, 74);
    private static readonly Color DividerColor = Color.FromArgb(67, 72, 79);
    private static readonly Color LabelColor = Color.FromArgb(170, 173, 180);
    private static readonly Color ValueColor = Color.FromArgb(246, 246, 247);

    private readonly ToolTip _toolTip = new();
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ContextMenuStrip _menu;
    private readonly Font _labelFont = new(
        "Inter",
        16f,
        FontStyle.Regular,
        GraphicsUnit.Pixel);
    private readonly Font _valueFont = new(
        "Inter SemiBold",
        19f,
        FontStyle.Regular,
        GraphicsUnit.Pixel);

    private OpenAiBlossomRenderer? _blossomRenderer;
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
        BackColor = CardBackground;
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        Opacity = WidgetOpacity;
        DoubleBuffered = true;

        try
        {
            _blossomRenderer = OpenAiBlossomRenderer.Load();
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not load official OpenAI Blossom asset", ex);
        }

        _toolTip.InitialDelay = 300;
        _toolTip.ReshowDelay = 100;
        _toolTip.AutoPopDelay = 12_000;
        _toolTip.ShowAlways = true;
        _toolTip.SetToolTip(this, "Loading Codex usage…");

        _menu = new ContextMenuStrip();
        _menu.Items.Add(
            "Refresh",
            null,
            (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));

        _alwaysOnTopItem = new ToolStripMenuItem("Always on top")
        {
            Checked = true,
            CheckOnClick = true
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) =>
            SetAlwaysOnTop(_alwaysOnTopItem.Checked);
        _menu.Items.Add(_alwaysOnTopItem);

        _menu.Items.Add("Reset position", null, (_, _) => ResetPosition());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(
            "Open log",
            null,
            (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(
            "Exit",
            null,
            (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
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
            const int WsExToolWindow = 0x00000080;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Keep the widget shadowless; the card itself supplies the rounded
        // silhouette and border.
        const int DwmwaNcRenderingPolicy = 2;
        const int DwmncrpDisabled = 1;
        int policy = DwmncrpDisabled;

        try
        {
            DwmSetWindowAttribute(
                Handle,
                DwmwaNcRenderingPolicy,
                ref policy,
                sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Older/non-DWM environments can continue without the attribute.
        }
        catch (EntryPointNotFoundException)
        {
            // Older/non-DWM environments can continue without the attribute.
        }

        ApplyRoundedRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(CardBackground);

        DrawCardBorder(graphics);
        DrawOfficialBlossom(graphics);
        DrawDivider(graphics);
        DrawMetricRow(graphics, "5h Usage", FormatPercent(_snapshot?.FiveHour), 13f);
        DrawMetricRow(graphics, "Weekly", FormatPercent(_snapshot?.Weekly), 39f);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    private void DrawCardBorder(Graphics graphics)
    {
        using var borderPen = new Pen(CardBorder, 1f);
        using GraphicsPath path = RoundedRect(
            new RectangleF(0.75f, 0.75f, ClientSize.Width - 1.5f, ClientSize.Height - 1.5f),
            CornerRadius);

        graphics.DrawPath(borderPen, path);
    }

    private void DrawOfficialBlossom(Graphics graphics)
    {
        _blossomRenderer?.Draw(
            graphics,
            new RectangleF(18f, 20f, 36f, 36f));
    }

    private static void DrawDivider(Graphics graphics)
    {
        using var pen = new Pen(DividerColor, 1f);
        graphics.DrawLine(pen, 67.5f, 15f, 67.5f, 61f);
    }

    private void DrawMetricRow(
        Graphics graphics,
        string label,
        string value,
        float y)
    {
        using var labelBrush = new SolidBrush(LabelColor);
        using var valueBrush = new SolidBrush(ValueColor);

        RectangleF labelRect = new(84f, y, 80f, 24f);
        RectangleF valueRect = new(164f, y - 1f, 52f, 25f);

        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        };

        using var valueFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        };

        graphics.DrawString(
            label,
            _labelFont,
            labelBrush,
            labelRect,
            labelFormat);

        graphics.DrawString(
            value,
            _valueFont,
            valueBrush,
            valueRect,
            valueFormat);
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

    public void UpdateCountdowns() => RefreshHoverText();

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
            if (Screen.AllScreens.Any(
                    screen => screen.WorkingArea.IntersectsWith(candidate)))
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
                BuildResetLine("5h Usage", _snapshot.FiveHour),
                BuildResetLine("Weekly", _snapshot.Weekly),
                $"Updated: {_snapshot.RetrievedAt:h:mm:ss tt}");
        }

        _toolTip.SetToolTip(this, text);
    }

    private static string BuildResetLine(
        string label,
        UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return $"{label}: reset unavailable";

        string countdown = FormatCountdown(window.ResetsAt.Value);
        return $"{label}: resets {window.ResetsAt.Value:MMM d, h:mm tt} · {countdown}";
    }

    private static string FormatCountdown(DateTimeOffset resetAt)
    {
        TimeSpan remaining = resetAt - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "due now";

        if (remaining.TotalDays >= 1)
        {
            return $"in {(int)remaining.TotalDays}d " +
                   $"{remaining.Hours}h {remaining.Minutes}m";
        }

        if (remaining.TotalHours >= 1)
            return $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"in {Math.Max(0, remaining.Minutes)}m";
    }

    private static string FormatPercent(UsageWindow? window)
    {
        return window is null
            ? "--"
            : $"{window.RemainingPercent:0.#}%";
    }

    private void ApplyRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        using GraphicsPath path = RoundedRect(
            new RectangleF(0, 0, ClientSize.Width, ClientSize.Height),
            CornerRadius);

        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRect(
        RectangleF bounds,
        float radius)
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

        int x = Math.Clamp(
            location.X,
            area.Left,
            Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(
            location.Y,
            area.Top,
            Math.Max(area.Top, area.Bottom - Height));

        return new Point(x, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blossomRenderer?.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
            _toolTip.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    private enum WidgetState
    {
        Loading,
        Live,
        Error
    }
}
