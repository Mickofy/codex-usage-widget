using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexUsageWidget;

internal sealed class FloatingWidgetForm : Form
{
    private const int WidgetWidth = 206;
    private const int WidgetHeight = 76;
    private const int ScreenMargin = 18;
    private const float CornerRadius = 15f;
    private const byte CardBackgroundAlpha = 230;
    private static readonly RectangleF LogoBounds = new(12f, 17f, 42f, 42f);

    private static readonly Color CardBackground = Color.FromArgb(18, 22, 28);
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
    private GeminiUsageSnapshot? _geminiSnapshot;
    private UsageProvider _provider = UsageProvider.Codex;
    private string? _errorMessage;
    private WidgetState _state = WidgetState.Loading;
    private bool _allowClose;
    private bool _dragging;
    private bool _restoringSettings;
    private Point _dragStartCursor;
    private Point _dragStartLocation;
    private bool _logoPressed;

    public UsageProvider Provider => _provider;

    public event EventHandler? RefreshRequested;
    public event EventHandler? ProviderSwitchRequested;
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
        AutoScaleMode = AutoScaleMode.None;

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

        Shown += (_, _) =>
        {
            RestorePosition();
            RenderLayeredWindow();
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
            const int WsExToolWindow = 0x00000080;
            const int WsExLayered = 0x00080000;

            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExLayered;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

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
            // Continue without DWM customization.
        }
        catch (EntryPointNotFoundException)
        {
            // Continue without DWM customization.
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // A layered window is rendered entirely through UpdateLayeredWindow.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // A layered window is rendered entirely through UpdateLayeredWindow.
    }

    private Bitmap BuildWidgetBitmap()
    {
        var bitmap = new Bitmap(
            WidgetWidth,
            WidgetHeight,
            PixelFormat.Format32bppPArgb);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (GraphicsPath cardPath = RoundedRect(
                   new RectangleF(0.5f, 0.5f, WidgetWidth - 1f, WidgetHeight - 1f),
                   CornerRadius))
        using (var cardBrush = new SolidBrush(Color.FromArgb(
                   CardBackgroundAlpha,
                   CardBackground.R,
                   CardBackground.G,
                   CardBackground.B)))
        {
            graphics.FillPath(cardBrush, cardPath);
        }

        DrawProviderLogo(graphics);

        if (_provider == UsageProvider.Codex)
        {
            DrawMetricRow(graphics, "5h Usage", FormatPercent(_snapshot?.FiveHour), 13f);
            DrawMetricRow(graphics, "Weekly", FormatPercent(_snapshot?.Weekly), 39f);
        }
        else
        {
            DrawMetricRow(graphics, "Weekly", FormatPercent(_geminiSnapshot?.RemainingPercent), 13f);
            DrawMetricRow(
                graphics,
                "Reset",
                FormatCountdown(_geminiSnapshot?.ResetsAt),
                39f,
                compactValue: true);
        }

        return bitmap;
    }

    private void RenderLayeredWindow()
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        using Bitmap bitmap = BuildWidgetBitmap();

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return;

        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            return;
        }

        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memoryDc, hBitmap);

            var destination = new NativePoint(Left, Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(WidgetWidth, WidgetHeight);
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1
            };

            if (!UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    2))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not render layered widget", ex);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                SelectObject(memoryDc, oldBitmap);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);

            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DrawProviderLogo(Graphics graphics)
    {
        if (_provider == UsageProvider.Codex)
        {
            _blossomRenderer?.Draw(
                graphics,
                new RectangleF(16f, 21f, 34f, 34f));
            return;
        }

        DrawGeminiMark(graphics);
    }

    private static void DrawGeminiMark(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.FromArgb(248, 248, 250));
        using var path = new GraphicsPath();

        PointF[] points =
        [
            new(33f, 19f),
            new(37.5f, 31.5f),
            new(50f, 38f),
            new(37.5f, 42.5f),
            new(33f, 56f),
            new(28.5f, 42.5f),
            new(16f, 38f),
            new(28.5f, 31.5f)
        ];

        path.AddPolygon(points);
        graphics.FillPath(brush, path);
    }

    private void DrawMetricRow(
        Graphics graphics,
        string label,
        string value,
        float y,
        bool compactValue = false)
    {
        using var labelBrush = new SolidBrush(LabelColor);
        using var valueBrush = new SolidBrush(ValueColor);

        // Keep the compact layout, but add a small visual gap between the
        // metric label and the percentage column.
        RectangleF labelRect = new(62f, y, 73f, 24f);
        RectangleF valueRect = compactValue
            ? new RectangleF(132f, y - 1f, 60f, 25f)
            : new RectangleF(142f, y - 1f, 50f, 25f);

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
            compactValue ? _labelFont : _valueFont,
            valueBrush,
            valueRect,
            valueFormat);
    }

    public void SetProvider(UsageProvider provider)
    {
        if (_provider == provider)
            return;

        _provider = provider;
        _state = WidgetState.Loading;
        _errorMessage = null;
        SaveCurrentSettings();
        RefreshHoverText();
        RenderLayeredWindow();
    }

    public void SetLoading()
    {
        _state = WidgetState.Loading;
        _errorMessage = null;
        RefreshHoverText();
        RenderLayeredWindow();
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _errorMessage = null;
        _state = WidgetState.Live;
        RefreshHoverText();
        RenderLayeredWindow();
    }

    public void SetGeminiSnapshot(GeminiUsageSnapshot snapshot)
    {
        _geminiSnapshot = snapshot;
        _errorMessage = null;
        _state = WidgetState.Live;
        RefreshHoverText();
        RenderLayeredWindow();
    }

    public void SetError(string message)
    {
        _errorMessage = message;
        _state = WidgetState.Error;
        RefreshHoverText();
        RenderLayeredWindow();
    }

    public void UpdateCountdowns()
    {
        RefreshHoverText();

        if (_provider == UsageProvider.Gemini)
            RenderLayeredWindow();
    }

    public void AllowClose() => _allowClose = true;

    public void RestorePosition()
    {
        WidgetSettings settings = WidgetSettings.Load();

        _restoringSettings = true;
        _alwaysOnTopItem.Checked = settings.AlwaysOnTop;
        TopMost = settings.AlwaysOnTop;

        if (Enum.TryParse(
                settings.Provider,
                ignoreCase: true,
                out UsageProvider savedProvider))
        {
            _provider = savedProvider;
        }

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
            AlwaysOnTop = TopMost,
            Provider = _provider.ToString()
        });
    }

    private void RefreshHoverText()
    {
        string text;

        if (_state == WidgetState.Error)
        {
            string providerName =
                _provider == UsageProvider.Codex ? "Codex" : "Gemini";
            text = $"{providerName} usage unavailable\n{_errorMessage ?? "Unknown error"}";
        }
        else if (_provider == UsageProvider.Codex)
        {
            text = _snapshot is null
                ? "Loading Codex usage…"
                : string.Join(
                    Environment.NewLine,
                    BuildResetLine("5h", _snapshot.FiveHour),
                    BuildResetLine("Weekly", _snapshot.Weekly));
        }
        else
        {
            text = _geminiSnapshot is null
                ? "Loading Gemini usage…"
                : string.Join(
                    Environment.NewLine,
                    $"Weekly Remaining: {_geminiSnapshot.RemainingPercent:0.#}%",
                    $"Weekly Reset: {FormatCountdown(_geminiSnapshot.ResetsAt)}");
        }

        _toolTip.SetToolTip(this, text);
    }

    private static string BuildResetLine(
        string label,
        UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return $"{label} Reset: unavailable";

        string countdown = FormatCountdown(window.ResetsAt.Value);
        return $"{label} Reset: {countdown}";
    }

    private static string FormatCountdown(DateTimeOffset resetAt)
    {
        TimeSpan remaining = resetAt - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
            return "due now";

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}d " +
                   $"{remaining.Hours}h {remaining.Minutes}m";
        }

        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"{Math.Max(0, remaining.Minutes)}m";
    }

    private static string FormatPercent(UsageWindow? window)
    {
        return window is null
            ? "--"
            : $"{window.RemainingPercent:0.#}%";
    }

    private static string FormatPercent(double? remainingPercent)
    {
        return remainingPercent is null
            ? "--"
            : $"{remainingPercent.Value:0.#}%";
    }

    private static string FormatCountdown(DateTimeOffset? resetAt)
    {
        return resetAt is null ? "--" : FormatCountdown(resetAt.Value);
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
        _logoPressed = LogoBounds.Contains(e.Location);
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
        Cursor = _logoPressed ? Cursors.Hand : Cursors.SizeAll;
    }

    private void DragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            Cursor = LogoBounds.Contains(e.Location)
                ? Cursors.Hand
                : Cursors.Default;
            return;
        }

        Point cursor = Cursor.Position;
        Location = new Point(
            _dragStartLocation.X + cursor.X - _dragStartCursor.X,
            _dragStartLocation.Y + cursor.Y - _dragStartCursor.Y);
    }

    private void DragMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
            return;

        Point cursor = Cursor.Position;
        int movement =
            Math.Abs(cursor.X - _dragStartCursor.X) +
            Math.Abs(cursor.Y - _dragStartCursor.Y);

        bool logoClick =
            _logoPressed &&
            movement <= 4 &&
            LogoBounds.Contains(PointToClient(cursor));

        _dragging = false;
        _logoPressed = false;

        if (logoClick)
        {
            Cursor = Cursors.Hand;
            ProviderSwitchRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Cursor = Cursors.Default;
        Location = ClampToVisibleArea(Location);
        SaveCurrentSettings();
        RenderLayeredWindow();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        uint crKey,
        ref BlendFunction pblend,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private enum WidgetState
    {
        Loading,
        Live,
        Error
    }
}
