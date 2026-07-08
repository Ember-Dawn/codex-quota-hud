using System.Drawing.Drawing2D;

namespace CodexQuotaHud;

public sealed class MainHudForm : Form
{
    private static readonly Color WindowBackColor = ColorTranslator.FromHtml("#181C22");
    private static readonly Color TrackColor = ColorTranslator.FromHtml("#2A2F36");
    private static readonly Color TextColor = ColorTranslator.FromHtml("#F2F4F8");
    private static readonly Color SevenDayColor = ColorTranslator.FromHtml("#4EA1FF");
    private static readonly Color FiveHourColor = ColorTranslator.FromHtml("#FFB454");
    private static readonly Color MutedTextColor = Color.FromArgb(185, 192, 203);

    private readonly CodexQuotaReader _reader = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();

    private readonly Panel _detailView = new();
    private readonly Panel _collapsedDockView = new();
    private readonly Label _titleLabel = new();
    private readonly QuotaBarControl _detailSevenDayBar = new();
    private readonly QuotaBarControl _detailFiveHourBar = new();
    private readonly Label _sevenDayResetLabel = new();
    private readonly Label _fiveHourResetLabel = new();
    private readonly Label _footerLabel = new();
    private readonly QuotaBarControl _collapsedSevenDayBar = new();
    private readonly QuotaBarControl _collapsedFiveHourBar = new();

    private QuotaSnapshot _snapshot = new();
    private bool _isDocked;
    private bool _isExpanded = true;
    private bool _isDragging;
    private bool _isRefreshing;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

    public MainHudForm()
    {
        Text = "Codex Quota HUD";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(380, 164);
        MinimumSize = new Size(320, 30);
        BackColor = WindowBackColor;
        ForeColor = TextColor;
        DoubleBuffered = true;

        BuildMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Codex Quota HUD",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.DoubleClick += (_, _) => ToggleVisible();

        BuildViews();
        WireMouseEvents(this);

        _refreshTimer.Interval = 60_000;
        _refreshTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        _refreshTimer.Start();

        Load += async (_, _) => await RefreshQuotaAsync();
        FormClosing += MainHudForm_FormClosing;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var path = RoundedRect(ClientRectangle, 10);
        Region = new Region(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildMenu()
    {
        _menu.Items.Add("显示/隐藏", null, (_, _) => ToggleVisible());
        _menu.Items.Add("刷新", null, async (_, _) => await RefreshQuotaAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitApplication());
    }

    private void BuildViews()
    {
        BuildDetailView();
        BuildCollapsedDockView();

        Controls.Add(_detailView);
        Controls.Add(_collapsedDockView);
        UpdateUi("启动中...");
    }

    private void BuildDetailView()
    {
        _detailView.Dock = DockStyle.Fill;
        _detailView.BackColor = WindowBackColor;
        _detailView.Padding = new Padding(14, 10, 14, 10);

        _titleLabel.AutoSize = false;
        _titleLabel.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _titleLabel.ForeColor = TextColor;
        _titleLabel.Text = "Codex Quota";
        _titleLabel.Location = new Point(14, 10);
        _titleLabel.Size = new Size(352, 22);

        ConfigureBar(_detailSevenDayBar, "7d", SevenDayColor, true);
        _detailSevenDayBar.Location = new Point(14, 42);
        _detailSevenDayBar.Size = new Size(352, 24);

        ConfigureResetLabel(_sevenDayResetLabel, new Point(14, 68));

        ConfigureBar(_detailFiveHourBar, "5h", FiveHourColor, true);
        _detailFiveHourBar.Location = new Point(14, 92);
        _detailFiveHourBar.Size = new Size(352, 24);

        ConfigureResetLabel(_fiveHourResetLabel, new Point(14, 118));

        _footerLabel.AutoSize = false;
        _footerLabel.ForeColor = MutedTextColor;
        _footerLabel.Location = new Point(14, 140);
        _footerLabel.Size = new Size(352, 20);

        _detailView.Controls.AddRange(new Control[]
        {
            _titleLabel,
            _detailSevenDayBar,
            _sevenDayResetLabel,
            _detailFiveHourBar,
            _fiveHourResetLabel,
            _footerLabel
        });
    }

    private void BuildCollapsedDockView()
    {
        _collapsedDockView.Dock = DockStyle.Fill;
        _collapsedDockView.BackColor = WindowBackColor;
        _collapsedDockView.Padding = new Padding(8, 4, 8, 4);
        _collapsedDockView.Visible = false;

        ConfigureBar(_collapsedSevenDayBar, "7d", SevenDayColor, false);
        _collapsedSevenDayBar.Location = new Point(8, 4);
        _collapsedSevenDayBar.Size = new Size(174, 24);

        ConfigureBar(_collapsedFiveHourBar, "5h", FiveHourColor, false);
        _collapsedFiveHourBar.Location = new Point(198, 4);
        _collapsedFiveHourBar.Size = new Size(174, 24);

        _collapsedDockView.Controls.AddRange(new Control[] { _collapsedSevenDayBar, _collapsedFiveHourBar });
    }

    private void ConfigureBar(QuotaBarControl bar, string title, Color fillColor, bool showPercentText)
    {
        bar.Title = title;
        bar.FillColor = fillColor;
        bar.TrackColor = TrackColor;
        bar.TextColor = TextColor;
        bar.BackColor = WindowBackColor;
        bar.ShowPercentText = showPercentText;
    }

    private static void ConfigureResetLabel(Label label, Point location)
    {
        label.AutoSize = false;
        label.ForeColor = MutedTextColor;
        label.Location = location;
        label.Size = new Size(352, 18);
    }

    private async Task RefreshQuotaAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateUi("更新中...");

        try
        {
            _snapshot = await _reader.ReadAsync();
        }
        catch (Exception ex)
        {
            _snapshot = new QuotaParser().FromError(ex.Message);
        }
        finally
        {
            _isRefreshing = false;
            UpdateUi();
        }
    }

    private void UpdateUi(string? statusOverride = null)
    {
        _detailSevenDayBar.Percent = _snapshot.SevenDay.RemainingPercent;
        _detailFiveHourBar.Percent = _snapshot.FiveHour.RemainingPercent;
        _collapsedSevenDayBar.Percent = _snapshot.SevenDay.RemainingPercent;
        _collapsedFiveHourBar.Percent = _snapshot.FiveHour.RemainingPercent;

        _sevenDayResetLabel.Text = $"reset: {_snapshot.SevenDay.ResetText}";
        _fiveHourResetLabel.Text = $"reset: {_snapshot.FiveHour.ResetText}";

        var status = statusOverride;
        if (status is null && !string.IsNullOrWhiteSpace(_snapshot.ErrorMessage))
        {
            status = $"读取失败: {_snapshot.ErrorMessage}";
            _footerLabel.ForeColor = FiveHourColor;
        }
        else
        {
            _footerLabel.ForeColor = statusOverride is null ? MutedTextColor : FiveHourColor;
        }

        _footerLabel.Text = string.IsNullOrWhiteSpace(status)
            ? $"updated: {_snapshot.UpdatedAt:HH:mm}"
            : $"updated: {_snapshot.UpdatedAt:HH:mm}  {status}";
    }

    private void ShowCurrentView()
    {
        var showDetail = !_isDocked || _isExpanded;
        Size = showDetail ? new Size(380, 164) : new Size(380, 32);
        _detailView.Visible = showDetail;
        _collapsedDockView.Visible = !showDetail;
        Invalidate();
    }

    private void ToggleVisible()
    {
        Visible = !Visible;
        if (Visible)
        {
            Show();
            Activate();
        }
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    private void MainHudForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleVisible();
        }
    }

    private void WireMouseEvents(Control control)
    {
        control.MouseDown += Hud_MouseDown;
        control.MouseMove += Hud_MouseMove;
        control.MouseUp += Hud_MouseUp;
        control.MouseEnter += Hud_MouseEnter;
        control.MouseLeave += Hud_MouseLeave;

        foreach (Control child in control.Controls)
        {
            WireMouseEvents(child);
        }
    }

    private void Hud_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _menu.Show(this, PointToClient(Cursor.Position));
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_isDocked)
        {
            _isDocked = false;
            _isExpanded = true;
            ShowCurrentView();
        }

        _isDragging = true;
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
    }

    private void Hud_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var delta = Cursor.Position - new Size(_dragStartCursor);
        Location = _dragStartLocation + new Size(delta);
    }

    private void Hud_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_isDragging)
        {
            return;
        }

        _isDragging = false;
        TryDockToTop();
    }

    private void Hud_MouseEnter(object? sender, EventArgs e)
    {
        if (_isDocked && !_isDragging)
        {
            _isExpanded = true;
            ShowCurrentView();
        }
    }

    private void Hud_MouseLeave(object? sender, EventArgs e)
    {
        if (!_isDocked || _isDragging)
        {
            return;
        }

        var clientPoint = PointToClient(Cursor.Position);
        if (!ClientRectangle.Contains(clientPoint))
        {
            _isExpanded = false;
            ShowCurrentView();
        }
    }

    private void TryDockToTop()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        if (Math.Abs(Top - workingArea.Top) <= 16)
        {
            _isDocked = true;
            _isExpanded = false;
            Location = new Point(Left, workingArea.Top);
            ShowCurrentView();
        }
        else
        {
            _isDocked = false;
            _isExpanded = true;
            ShowCurrentView();
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
