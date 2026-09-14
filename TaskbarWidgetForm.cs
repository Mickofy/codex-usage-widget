using System.Runtime.InteropServices;

namespace CodexUsageWidget;

internal sealed class TaskbarWidgetForm : Form
{
    private const int DefaultOffsetFromTaskbarLeft = 86;
    private const int HorizontalPadding = 12;

    private readonly Label _statusLabel = new();
    private readonly Label _fiveHourValue = new();
    private readonly Label _weeklyValue = new();
    private readonly Label _fiveHourReset = new();
    private readonly Label _weeklyReset = new();
    private readonly ToolTip _toolTip = new();

    private UsageSnapshot? _snapshot;
    private bool _allowClose;

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

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
        Width = 292;
        Height = 58;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Place beside Weather", null, (_, _) => SnapRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => OpenLogRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(HorizontalPadding, 5, HorizontalPadding, 4),
            BackColor = BackColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var mainRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };

        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "CODEX",
            AutoSize = true,
            ForeColor = Color.FromArgb(228, 228, 231),
            Font = new Font("Segoe UI Semibold", 8.5f),
            Margin = new Padding(0, 3, 10, 0),
            Anchor = AnchorStyles.Left
        };

        var fiveLabel = MakeMetricLabel("5H");
        ConfigureValueLabel(_fiveHourValue);

        var weekLabel = MakeMetricLabel("W");
        ConfigureValueLabel(_weeklyValue);

        _statusLabel.Text = "●";
        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(161, 161, 170);
        _statusLabel.Font = new Font("Segoe UI", 8f);
        _statusLabel.Margin = new Padding(7, 3, 0, 0);
        _statusLabel.Anchor = AnchorStyles.Right;

        mainRow.Controls.Add(title, 0, 0);
        mainRow.Controls.Add(fiveLabel, 1, 0);
        mainRow.Controls.Add(_fiveHourValue, 2, 0);
        mainRow.Controls.Add(weekLabel, 3, 0);
        mainRow.Controls.Add(_weeklyValue, 4, 0);
        mainRow.Controls.Add(_statusLabel, 5, 0);

        var resetRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = BackColor
        };
        resetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        resetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        ConfigureResetLabel(_fiveHourReset);
        ConfigureResetLabel(_weeklyReset);

        resetRow.Controls.Add(_fiveHourReset, 0, 0);
        resetRow.Controls.Add(_weeklyReset, 1, 0);

        root.Controls.Add(mainRow, 0, 0);
        root.Controls.Add(resetRow, 0, 1);
        Controls.Add(root);

        AttachDragHandler(this);
        AttachDragHandler(root);
        AttachDragHandler(mainRow);
        AttachDragHandler(resetRow);
        AttachDragHandler(title);
        AttachDragHandler(fiveLabel);
        AttachDragHandler(weekLabel);
        AttachDragHandler(_statusLabel);
        AttachDragHandler(_fiveHourValue);
        AttachDragHandler(_weeklyValue);
        AttachDragHandler(_fiveHourReset);
        AttachDragHandler(_weeklyReset);

        FormClosing += (_, e) =>
        {
            if (_allowClose)
                return;

            e.Cancel = true;
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;

            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    private static Label MakeMetricLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(161, 161, 170),
            Font = new Font("Segoe UI Semibold", 8f),
            Margin = new Padding(0, 4, 4, 0),
            Anchor = AnchorStyles.Left
        };
    }

    private static void ConfigureValueLabel(Label label)
    {
        label.Text = "--";
        label.AutoSize = true;
        label.ForeColor = Color.White;
        label.Font = new Font("Segoe UI Semibold", 12.5f);
        label.Margin = new Padding(0, 0, 12, 0);
        label.Anchor = AnchorStyles.Left;
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

    public void SnapToTaskbar()
    {
        if (!TryGetTaskbarRectangle(out Rectangle taskbar))
        {
            Rectangle fallback = Screen.PrimaryScreen?.Bounds ?? Screen.FromPoint(Cursor.Position).Bounds;
            taskbar = new Rectangle(fallback.Left, fallback.Bottom - 48, fallback.Width, 48);
        }

        int availableHeight = Math.Max(32, taskbar.Height - 6);
        Height = Math.Min(58, availableHeight);

        int offset = WidgetSettings.Load().TaskbarOffsetX ?? DefaultOffsetFromTaskbarLeft;
        int x = taskbar.Left + offset;
        int maxX = Math.Max(taskbar.Left, taskbar.Right - Width - 4);
        x = Math.Clamp(x, taskbar.Left + 4, maxX);

        int y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);

        SetWindowPos(
            Handle,
            HwndTopMost,
            x,
            y,
            Width,
            Height,
            SwpNoActivate | SwpShowWindow);
    }

    public void KeepInsideTaskbar()
    {
        if (!TryGetTaskbarRectangle(out Rectangle taskbar))
            return;

        int maxX = Math.Max(taskbar.Left, taskbar.Right - Width - 4);
        int x = Math.Clamp(Left, taskbar.Left + 4, maxX);
        int y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);

        SetWindowPos(
            Handle,
            HwndTopMost,
            x,
            y,
            Width,
            Height,
            SwpNoActivate | SwpShowWindow);
    }

    public void AllowClose() => _allowClose = true;

    private static string FormatPercent(UsageWindow? window)
    {
        return window is null ? "N/A" : $"{window.RemainingPercent:0.#}%";
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

    private void AttachDragHandler(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            _dragStartCursor = Cursor.Position;
            _dragStartLocation = Location;
        };

        control.MouseMove += (_, _) =>
        {
            if (!_dragging)
                return;

            if (!TryGetTaskbarRectangle(out Rectangle taskbar))
                return;

            int deltaX = Cursor.Position.X - _dragStartCursor.X;
            int candidateX = _dragStartLocation.X + deltaX;
            int maxX = Math.Max(taskbar.Left, taskbar.Right - Width - 4);
            int x = Math.Clamp(candidateX, taskbar.Left + 4, maxX);
            int y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);

            SetWindowPos(
                Handle,
                HwndTopMost,
                x,
                y,
                Width,
                Height,
                SwpNoActivate | SwpShowWindow);
        };

        control.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !_dragging)
                return;

            _dragging = false;

            if (TryGetTaskbarRectangle(out Rectangle taskbar))
            {
                int offset = Math.Max(0, Left - taskbar.Left);
                WidgetSettings.Save(new WidgetSettings
                {
                    TaskbarOffsetX = offset
                });
            }
        };
    }

    private static bool TryGetTaskbarRectangle(out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return false;

        if (!GetWindowRect(taskbar, out Rect rect))
            return false;

        rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return rectangle.Width > 0 && rectangle.Height > 0;
    }

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);

    private static readonly IntPtr HwndTopMost = new(-1);

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
