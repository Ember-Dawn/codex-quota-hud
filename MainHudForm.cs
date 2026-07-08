using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexQuotaHud;

public sealed class MainHudForm : Form
{
    private static readonly Color WindowBackColor = ColorTranslator.FromHtml("#181C22");
    private static readonly Color WindowBorderColor = ColorTranslator.FromHtml("#2E3640");
    private static readonly Color TrackColor = ColorTranslator.FromHtml("#303740");
    private static readonly Color TextColor = ColorTranslator.FromHtml("#F2F4F8");
    private static readonly Color MutedTextColor = ColorTranslator.FromHtml("#B8C0CC");

    private readonly CodexQuotaReader _reader = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();

    private readonly Panel _detailView = new();
    private readonly Panel _collapsedDockView = new();
    private readonly Label _titleLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _sevenDayLabel = new();
    private readonly Label _fiveHourLabel = new();
    private readonly QuotaBarControl _detailSevenDayBar = new();
    private readonly QuotaBarControl _detailFiveHourBar = new();
    private readonly Label _sevenDayResetPrefixLabel = new();
    private readonly Label _sevenDayResetDateLabel = new();
    private readonly Label _sevenDayResetTimeLabel = new();
    private readonly Label _fiveHourResetPrefixLabel = new();
    private readonly Label _fiveHourResetTimeLabel = new();
    private readonly Label _footerLabel = new();
    private readonly QuotaBarControl _collapsedSevenDayBar = new();
    private readonly QuotaBarControl _collapsedFiveHourBar = new();

    private AppSettings _settings;
    private Color _sevenDayColor;
    private Color _fiveHourColor;
    private Color _trackBorderColor;
    private QuotaSnapshot _snapshot = new();
    private bool _isDocked;
    private bool _isExpanded = true;
    private bool _isDragging;
    private bool _isRefreshing;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

    public MainHudForm()
    {
        _settings = SettingsStore.Load();
        _sevenDayColor = ColorTranslator.FromHtml(_settings.SevenDayColor);
        _fiveHourColor = ColorTranslator.FromHtml(_settings.FiveHourColor);
        _trackBorderColor = ColorTranslator.FromHtml(_settings.TrackBorderColor);

        Text = "Codex Quota HUD";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(370, 104);
        MinimumSize = new Size(290, 30);
        Padding = new Padding(1);
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
        ApplySettings(_settings, save: false);

        _refreshTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        _refreshTimer.Start();

        Load += async (_, _) => await RefreshQuotaAsync();
        FormClosing += MainHudForm_FormClosing;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedRegion();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(bounds, 11);
        using var pen = new Pen(WindowBorderColor, 1f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
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
        _menu.Items.Add("Refresh Now", null, async (_, _) => await RefreshQuotaAsync());
        _menu.Items.Add("Settings...", null, (_, _) => ShowSettingsWindow());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApplication());
    }

    private void BuildViews()
    {
        BuildDetailView();
        BuildCollapsedDockView();

        Controls.Add(_detailView);
        Controls.Add(_collapsedDockView);
        UpdateUi("Loading...");
    }

    private void BuildDetailView()
    {
        _detailView.Dock = DockStyle.Fill;
        _detailView.BackColor = WindowBackColor;

        _titleLabel.AutoSize = false;
        _titleLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        _titleLabel.ForeColor = TextColor;
        _titleLabel.Text = "Codex Quota";
        _titleLabel.Location = new Point(14, 9);
        _titleLabel.Size = new Size(180, 20);

        ConfigureMetaLabel(_updatedLabel, new Point(306, 9), new Size(50, 20));
        _updatedLabel.ForeColor = MutedTextColor;
        _updatedLabel.TextAlign = ContentAlignment.MiddleLeft;

        ConfigureMetaLabel(_sevenDayLabel, new Point(14, 39), new Size(32, 23));
        _sevenDayLabel.Text = "7d";
        ConfigureMetaLabel(_fiveHourLabel, new Point(14, 68), new Size(32, 23));
        _fiveHourLabel.Text = "5h";

        ConfigureBar(_detailSevenDayBar, string.Empty, _sevenDayColor, showTitle: false, showPercentText: true);
        _detailSevenDayBar.Location = new Point(50, 38);
        _detailSevenDayBar.Size = new Size(168, 23);

        ConfigureBar(_detailFiveHourBar, string.Empty, _fiveHourColor, showTitle: false, showPercentText: true);
        _detailFiveHourBar.Location = new Point(50, 67);
        _detailFiveHourBar.Size = new Size(168, 23);

        ConfigureMetaLabel(_sevenDayResetPrefixLabel, new Point(230, 38), new Size(14, 23));
        _sevenDayResetPrefixLabel.Text = "R";
        ConfigureMetaLabel(_sevenDayResetDateLabel, new Point(248, 38), new Size(54, 23));
        ConfigureMetaLabel(_sevenDayResetTimeLabel, new Point(306, 38), new Size(50, 23));

        ConfigureMetaLabel(_fiveHourResetPrefixLabel, new Point(230, 67), new Size(14, 23));
        _fiveHourResetPrefixLabel.Text = "R";
        ConfigureMetaLabel(_fiveHourResetTimeLabel, new Point(306, 67), new Size(50, 23));

        _footerLabel.AutoSize = false;
        _footerLabel.ForeColor = _fiveHourColor;
        _footerLabel.Location = new Point(14, 88);
        _footerLabel.Size = new Size(342, 16);
        _footerLabel.Visible = false;

        _detailView.Controls.AddRange(new Control[]
        {
            _titleLabel,
            _updatedLabel,
            _sevenDayLabel,
            _fiveHourLabel,
            _detailSevenDayBar,
            _detailFiveHourBar,
            _sevenDayResetPrefixLabel,
            _sevenDayResetDateLabel,
            _sevenDayResetTimeLabel,
            _fiveHourResetPrefixLabel,
            _fiveHourResetTimeLabel,
            _footerLabel
        });
    }

    private void BuildCollapsedDockView()
    {
        _collapsedDockView.Dock = DockStyle.Fill;
        _collapsedDockView.BackColor = WindowBackColor;
        _collapsedDockView.Visible = false;

        ConfigureBar(_collapsedSevenDayBar, "7d", _sevenDayColor, showTitle: true, showPercentText: false);
        _collapsedSevenDayBar.Location = new Point(10, 5);
        _collapsedSevenDayBar.Size = new Size(140, 22);

        ConfigureBar(_collapsedFiveHourBar, "5h", _fiveHourColor, showTitle: true, showPercentText: false);
        _collapsedFiveHourBar.Location = new Point(170, 5);
        _collapsedFiveHourBar.Size = new Size(140, 22);

        _collapsedDockView.Controls.AddRange(new Control[] { _collapsedSevenDayBar, _collapsedFiveHourBar });
    }

    private void ConfigureBar(QuotaBarControl bar, string title, Color fillColor, bool showTitle, bool showPercentText)
    {
        bar.Title = title;
        bar.FillColor = fillColor;
        bar.TrackColor = TrackColor;
        bar.TrackBorderColor = _trackBorderColor;
        bar.TextColor = TextColor;
        bar.BackColor = WindowBackColor;
        bar.ShowTitle = showTitle;
        bar.ShowPercentText = showPercentText;
    }

    private static void ConfigureMetaLabel(Label label, Point location, Size size)
    {
        label.AutoSize = false;
        label.Font = new Font(FontFamily.GenericSansSerif, 8.8f, FontStyle.Regular);
        label.ForeColor = MutedTextColor;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Location = location;
        label.Size = size;
    }

    private async Task RefreshQuotaAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateUi("Loading...");

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

        _updatedLabel.Text = _snapshot.UpdatedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        _sevenDayResetDateLabel.Text = FormatSevenDayResetDate(_snapshot.SevenDay.ResetAt);
        _sevenDayResetTimeLabel.Text = FormatResetTime(_snapshot.SevenDay.ResetAt);
        _fiveHourResetTimeLabel.Text = FormatResetTime(_snapshot.FiveHour.ResetAt);

        var status = statusOverride;
        if (status is null && !string.IsNullOrWhiteSpace(_snapshot.ErrorMessage))
        {
            status = $"Failed: {_snapshot.ErrorMessage}";
        }

        _footerLabel.Text = status ?? string.Empty;
        _footerLabel.Visible = !string.IsNullOrWhiteSpace(status);
    }

    private static string FormatSevenDayResetDate(DateTime? resetAt)
    {
        return resetAt.HasValue ? resetAt.Value.ToString("MMM dd", CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatResetTime(DateTime? resetAt)
    {
        return resetAt.HasValue ? resetAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "--";
    }

    private void ApplySettings(AppSettings settings, bool save)
    {
        _settings = settings.Clone();
        _sevenDayColor = ColorTranslator.FromHtml(_settings.SevenDayColor);
        _fiveHourColor = ColorTranslator.FromHtml(_settings.FiveHourColor);
        _trackBorderColor = ColorTranslator.FromHtml(_settings.TrackBorderColor);

        _refreshTimer.Interval = Math.Max(1, _settings.AutoRefreshSeconds) * 1000;
        _detailSevenDayBar.FillColor = _sevenDayColor;
        _collapsedSevenDayBar.FillColor = _sevenDayColor;
        _detailFiveHourBar.FillColor = _fiveHourColor;
        _collapsedFiveHourBar.FillColor = _fiveHourColor;
        _detailSevenDayBar.TrackBorderColor = _trackBorderColor;
        _collapsedSevenDayBar.TrackBorderColor = _trackBorderColor;
        _detailFiveHourBar.TrackBorderColor = _trackBorderColor;
        _collapsedFiveHourBar.TrackBorderColor = _trackBorderColor;
        _footerLabel.ForeColor = _fiveHourColor;

        if (save)
        {
            SettingsStore.Save(_settings);
        }
    }

    private void ShowSettingsWindow()
    {
        using var settingsForm = new SettingsForm(_settings);
        settingsForm.Location = CalculateSettingsLocation(settingsForm.Size);
        if (settingsForm.ShowDialog(this) == DialogResult.OK)
        {
            ApplySettings(settingsForm.ResultSettings, save: true);
        }
    }

    private Point CalculateSettingsLocation(Size settingsSize)
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var preferred = new Point(Right + 8, Bottom + 8);

        if (preferred.X + settingsSize.Width > workingArea.Right)
        {
            preferred.X = Left - settingsSize.Width - 8;
        }

        if (preferred.Y + settingsSize.Height > workingArea.Bottom)
        {
            preferred.Y = Top - settingsSize.Height - 8;
        }

        preferred.X = Math.Clamp(preferred.X, workingArea.Left, workingArea.Right - settingsSize.Width);
        preferred.Y = Math.Clamp(preferred.Y, workingArea.Top, workingArea.Bottom - settingsSize.Height);
        return preferred;
    }

    private void ShowCurrentView()
    {
        var showDetail = !_isDocked || _isExpanded;
        Size = showDetail ? new Size(370, 104) : new Size(320, 32);
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

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 11);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        radius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
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
