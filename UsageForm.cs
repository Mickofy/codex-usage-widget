namespace CodexUsageWidget;

internal sealed class UsageForm : Form
{
    private readonly Label _fiveValue = new();
    private readonly Label _weeklyValue = new();

    private readonly ProgressBar _fiveBar = new();
    private readonly ProgressBar _weeklyBar = new();

    private readonly Label _fiveReset = new();
    private readonly Label _weeklyReset = new();

    private readonly Label _status = new();
    private readonly Label _updated = new();

    private readonly Button _refresh = new();

    private UsageSnapshot? _snapshot;
    private bool _allowClose;

    public event EventHandler? RefreshRequested;

    public UsageForm()
    {
        Text = "Codex Usage";
        ClientSize = new Size(390, 298);
        FormBorderStyle = FormBorderStyle.FixedSingle;

        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Codex Usage",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17f),
            Margin = new Padding(0, 0, 0, 12)
        };

        root.Controls.Add(title);

        root.Controls.Add(
            BuildGroup(
                "5-hour limit",
                _fiveValue,
                _fiveBar,
                _fiveReset));

        root.Controls.Add(
            BuildGroup(
                "Weekly limit",
                _weeklyValue,
                _weeklyBar,
                _weeklyReset));

        _status.AutoSize = true;
        _status.Text = "Loading…";
        _status.Margin = new Padding(0, 4, 0, 0);

        _updated.AutoSize = true;
        _updated.ForeColor = SystemColors.GrayText;
        _updated.Margin = new Padding(0, 2, 0, 0);

        root.Controls.Add(_status);
        root.Controls.Add(_updated);
        root.Controls.Add(new Panel { Dock = DockStyle.Fill });

        _refresh.Text = "Refresh";
        _refresh.AutoSize = true;
        _refresh.MinimumSize = new Size(92, 34);
        _refresh.Anchor = AnchorStyles.Right;
        _refresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        root.Controls.Add(_refresh);

        Controls.Add(root);

        Deactivate += (_, _) =>
        {
            if (!_allowClose)
                Hide();
        };

        FormClosing += (_, e) =>
        {
            if (_allowClose)
                return;

            e.Cancel = true;
            Hide();
        };
    }

    private static Control BuildGroup(
        string title,
        Label value,
        ProgressBar bar,
        Label reset)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 14)
        };

        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f)
        };

        value.Text = "—";
        value.AutoSize = true;
        value.Font = new Font(
            "Segoe UI Semibold",
            10.5f);

        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.Value = 0;
        bar.Dock = DockStyle.Fill;
        bar.Height = 16;
        bar.Margin = new Padding(0, 6, 0, 5);

        reset.Text = "Reset: —";
        reset.AutoSize = true;
        reset.ForeColor = SystemColors.GrayText;

        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(value, 1, 0);

        panel.Controls.Add(bar, 0, 1);
        panel.SetColumnSpan(bar, 2);

        panel.Controls.Add(reset, 0, 2);
        panel.SetColumnSpan(reset, 2);

        return panel;
    }

    public void SetLoading()
    {
        _refresh.Enabled = false;
        _status.Text = "Reading Codex usage…";
        _status.ForeColor = SystemColors.ControlText;
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;

        Apply(
            snapshot.FiveHour,
            _fiveValue,
            _fiveBar);

        Apply(
            snapshot.Weekly,
            _weeklyValue,
            _weeklyBar);

        _status.Text = "Connected to Codex";
        _status.ForeColor = SystemColors.ControlText;

        _updated.Text =
            $"Updated {snapshot.RetrievedAt:h:mm:ss tt}";

        _refresh.Enabled = true;

        UpdateCountdowns();
    }

    public void SetError(Exception ex)
    {
        _status.Text = "Refresh failed";
        _status.ForeColor = Color.Firebrick;

        _updated.Text = ex.Message;

        _refresh.Enabled = true;
    }

    public void UpdateCountdowns()
    {
        if (_snapshot is null)
            return;

        _fiveReset.Text =
            ResetText(_snapshot.FiveHour);

        _weeklyReset.Text =
            ResetText(_snapshot.Weekly);
    }

    private static void Apply(
        UsageWindow? window,
        Label label,
        ProgressBar bar)
    {
        if (window is null)
        {
            label.Text = "Unavailable";
            bar.Value = 0;
            return;
        }

        int remaining = Math.Clamp(
            (int)Math.Round(window.RemainingPercent),
            0,
            100);

        label.Text =
            $"{window.RemainingPercent:0.#}% left";

        bar.Value = remaining;
    }

    private static string ResetText(
        UsageWindow? window)
    {
        if (window?.ResetsAt is null)
            return "Reset: unavailable";

        TimeSpan left =
            window.ResetsAt.Value - DateTimeOffset.Now;

        if (left <= TimeSpan.Zero)
            return
                $"Reset due · {window.ResetsAt.Value:MMM d, h:mm tt}";

        string countdown =
            left.TotalDays >= 1
                ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes}m"
                : left.TotalHours >= 1
                    ? $"{(int)left.TotalHours}h {left.Minutes}m"
                    : $"{Math.Max(0, left.Minutes)}m";

        return
            $"Reset in {countdown} · {window.ResetsAt.Value:MMM d, h:mm tt}";
    }

    public void AllowClose() =>
        _allowClose = true;
}
