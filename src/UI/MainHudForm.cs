using System.Drawing.Drawing2D;

namespace CodexQuotaHud;

public sealed class MainHudForm : Form
{
    private static readonly Color WindowBackColor = ColorTranslator.FromHtml("#181C22");
    private static readonly Color WindowBorderColor = ColorTranslator.FromHtml("#2E3640");

    private readonly QuotaPoller _poller = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Panel _detailView = new();
    private readonly Panel _collapsedDockView = new();
    private readonly FlowLayoutPanel _detailFlow = new BufferedFlowLayoutPanel();
    private readonly FlowLayoutPanel _collapsedFlow = new BufferedFlowLayoutPanel();
    private readonly Dictionary<string, ProviderCardControl> _providerCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CollapsedProviderRowControl> _providerRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _appCts = new();

    private AppSettings _settings;
    private Color _sevenDayColor;
    private Color _fiveHourColor;
    private Color _trackColor;
    private Color _trackBorderColor;
    private List<ProviderQuotaSnapshot> _providerSnapshots = new();
    private HudToastForm? _toast;
    private string? _lastAgyToastKey;
    private DateTime _lastAgyToastAt = DateTime.MinValue;
    private bool _isDocked;
    private bool _isExpanded = true;
    private bool _isDragging;
    private bool _isRefreshing;
    private bool _isExiting;
    private bool _pollerDisposed;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

    public MainHudForm()
    {
        _settings = SettingsStore.Load();
        DebugLogger.Initialize(_settings);
        _sevenDayColor = ColorTranslator.FromHtml(_settings.SevenDayColor);
        _fiveHourColor = ColorTranslator.FromHtml(_settings.FiveHourColor);
        _trackColor = ColorTranslator.FromHtml(_settings.TrackColor);
        _trackBorderColor = ColorTranslator.FromHtml(_settings.TrackBorderColor);
        _providerSnapshots = CreateInitialSnapshots();

        Text = "Codex Quota HUD";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(370, 116);
        MinimumSize = new Size(290, 30);
        Padding = new Padding(1);
        BackColor = WindowBackColor;
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

        _refreshTimer.Interval = Math.Max(1, _settings.AutoRefreshSeconds) * 1000;
        _refreshTimer.Tick += async (_, __) =>
        {
            if (_isExiting)
            {
                return;
            }

            await RefreshQuotaAsync();
        };
        _refreshTimer.Start();

        Load += async (_, _) =>
        {
            await ApplySettingsAsync(_settings, save: false);
            await RefreshQuotaAsync();
        };
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
        using var path = RoundedRect(bounds, CurrentWindowRadius());
        using var pen = new Pen(WindowBorderColor, 1f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            CloseToast();
            _appCts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildMenu()
    {
        _menu.Items.Add("Refresh Now", null, async (_, _) => await RefreshQuotaAsync());
        _menu.Items.Add("Settings...", null, (_, _) => ShowSettingsWindow());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());
    }

    private void BuildViews()
    {
        _detailView.Dock = DockStyle.Fill;
        _detailView.BackColor = WindowBackColor;
        _collapsedDockView.Dock = DockStyle.Fill;
        _collapsedDockView.BackColor = WindowBackColor;
        _collapsedDockView.Visible = false;

        _detailFlow.Dock = DockStyle.Fill;
        _detailFlow.FlowDirection = FlowDirection.TopDown;
        _detailFlow.WrapContents = false;
        _detailFlow.Padding = new Padding(10, 10, 10, 2);
        _detailFlow.BackColor = WindowBackColor;

        _collapsedFlow.Dock = DockStyle.Fill;
        _collapsedFlow.FlowDirection = FlowDirection.TopDown;
        _collapsedFlow.WrapContents = false;
        _collapsedFlow.Padding = new Padding(0, 2, 0, 2);
        _collapsedFlow.BackColor = WindowBackColor;

        _detailView.Controls.Add(_detailFlow);
        _collapsedDockView.Controls.Add(_collapsedFlow);
        Controls.Add(_detailView);
        Controls.Add(_collapsedDockView);
        UpdateUi(forceLayout: true);
    }

    private async Task RefreshQuotaAsync()
    {
        if (_isRefreshing || _isExiting || _appCts.IsCancellationRequested)
        {
            return;
        }

        _isRefreshing = true;
        if (EnsureEnabledProviderPlaceholders())
        {
            UpdateUi(forceLayout: true);
        }

        try
        {
            var snapshots = await _poller.RefreshAsync(_appCts.Token);
            if (_isExiting || IsDisposed || Disposing || _appCts.IsCancellationRequested)
            {
                return;
            }

            if (snapshots.Count > 0)
            {
                _providerSnapshots = snapshots.ToList();
            }

            UpdateUi(forceLayout: false);
            ShowAgyToastIfNeeded();
        }
        catch (OperationCanceledException) when (_isExiting || _appCts.IsCancellationRequested)
        {
            AppendLog("refresh cancelled by exit");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void UpdateUi(bool forceLayout)
    {
        if (_isExiting || IsDisposed || Disposing)
        {
            return;
        }

        var layoutChanged = SynchronizeProviderControls();
        if (layoutChanged || forceLayout)
        {
            ShowCurrentView(force: forceLayout);
        }
    }

    private bool SynchronizeProviderControls()
    {
        var layoutChanged = false;
        var providerIds = _providerSnapshots.Select(snapshot => snapshot.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _detailFlow.SuspendLayout();
        _collapsedFlow.SuspendLayout();
        try
        {
            foreach (var providerId in _providerCards.Keys.Where(id => !providerIds.Contains(id)).ToArray())
            {
                RemoveProviderControl(providerId);
                layoutChanged = true;
            }

            for (var index = 0; index < _providerSnapshots.Count; index++)
            {
                var snapshot = _providerSnapshots[index];
                if (!_providerCards.TryGetValue(snapshot.ProviderId, out var card))
                {
                    card = new ProviderCardControl();
                    card.SetColors(_sevenDayColor, _fiveHourColor, _trackColor, _trackBorderColor);
                    WireMouseEvents(card);
                    _providerCards[snapshot.ProviderId] = card;
                    _detailFlow.Controls.Add(card);
                    layoutChanged = true;
                }

                if (!_providerRows.TryGetValue(snapshot.ProviderId, out var row))
                {
                    row = new CollapsedProviderRowControl();
                    row.SetColors(_sevenDayColor, _fiveHourColor, _trackColor, _trackBorderColor);
                    WireMouseEvents(row);
                    _providerRows[snapshot.ProviderId] = row;
                    _collapsedFlow.Controls.Add(row);
                    layoutChanged = true;
                }

                card.SetColors(_sevenDayColor, _fiveHourColor, _trackColor, _trackBorderColor);
                row.SetColors(_sevenDayColor, _fiveHourColor, _trackColor, _trackBorderColor);
                layoutChanged |= card.UpdateSnapshot(snapshot);
                row.UpdateSnapshot(snapshot);

                if (_detailFlow.Controls.GetChildIndex(card) != index)
                {
                    _detailFlow.Controls.SetChildIndex(card, index);
                    layoutChanged = true;
                }

                if (_collapsedFlow.Controls.GetChildIndex(row) != index)
                {
                    _collapsedFlow.Controls.SetChildIndex(row, index);
                    layoutChanged = true;
                }
            }
        }
        finally
        {
            _collapsedFlow.ResumeLayout();
            _detailFlow.ResumeLayout();
        }

        return layoutChanged;
    }

    private void RemoveProviderControl(string providerId)
    {
        if (_providerCards.Remove(providerId, out var card))
        {
            _detailFlow.Controls.Remove(card);
            card.Dispose();
        }

        if (_providerRows.Remove(providerId, out var row))
        {
            _collapsedFlow.Controls.Remove(row);
            row.Dispose();
        }
    }

    private async Task ApplySettingsAsync(AppSettings settings, bool save)
    {
        if (_isExiting || _appCts.IsCancellationRequested)
        {
            return;
        }

        _settings = settings.Clone();
        DebugLogger.ApplySettings(_settings);
        _sevenDayColor = ColorTranslator.FromHtml(_settings.SevenDayColor);
        _fiveHourColor = ColorTranslator.FromHtml(_settings.FiveHourColor);
        _trackColor = ColorTranslator.FromHtml(_settings.TrackColor);
        _trackBorderColor = ColorTranslator.FromHtml(_settings.TrackBorderColor);
        _refreshTimer.Interval = Math.Max(1, _settings.AutoRefreshSeconds) * 1000;

        await _poller.ApplySettingsAsync(_settings);

        if (!_settings.EnableAntigravity)
        {
            _providerSnapshots = _providerSnapshots.Where(snapshot => snapshot.ProviderId != "agy").ToList();
            _lastAgyToastKey = null;
            CloseToast();
        }

        if (save)
        {
            SettingsStore.Save(_settings);
        }

        UpdateUi(forceLayout: true);
    }

    private async void ShowSettingsWindow()
    {
        if (_isExiting)
        {
            return;
        }

        using var settingsForm = new SettingsForm(_settings);
        settingsForm.Location = CalculateSettingsLocation(settingsForm.Size);
        if (settingsForm.ShowDialog(this) == DialogResult.OK)
        {
            var enableChanged = settingsForm.ResultSettings.EnableAntigravity != _settings.EnableAntigravity;
            await ApplySettingsAsync(settingsForm.ResultSettings, save: true);
            if (enableChanged)
            {
                await RefreshQuotaAsync();
            }
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

    private void ShowCurrentView(bool force = false)
    {
        var showDetail = !_isDocked || _isExpanded;
        var desiredSize = showDetail ? new Size(370, CalculateDetailHeight()) : new Size(320, CalculateCollapsedHeight());
        var changed = false;

        if (Size != desiredSize)
        {
            Size = desiredSize;
            changed = true;
        }

        if (_detailView.Visible != showDetail)
        {
            _detailView.Visible = showDetail;
            changed = true;
        }

        if (_collapsedDockView.Visible == showDetail)
        {
            _collapsedDockView.Visible = !showDetail;
            changed = true;
        }

        if (changed || force)
        {
            ApplyRoundedRegion();
            Invalidate();
        }
    }

    private int CalculateDetailHeight()
    {
        var height = 20;
        foreach (var snapshot in _providerSnapshots)
        {
            if (_providerCards.TryGetValue(snapshot.ProviderId, out var card))
            {
                height += card.Height + card.Margin.Bottom;
            }
            else
            {
                height += 104;
            }
        }

        return Math.Max(104, height);
    }

    private int CalculateCollapsedHeight()
    {
        return Math.Max(32, 6 + _providerSnapshots.Count * 28);
    }

    private void ShowAgyToastIfNeeded()
    {
        if (_isExiting || IsDisposed || Disposing || !_settings.EnableAntigravity)
        {
            return;
        }

        var agy = _providerSnapshots.FirstOrDefault(snapshot => snapshot.ProviderId == "agy");
        if (agy is null || agy.Status is not (QuotaProviderStatus.Offline or QuotaProviderStatus.Failed))
        {
            return;
        }

        var message = agy.ErrorMessage ?? "AGY offline";
        var now = DateTime.Now;
        if (string.Equals(_lastAgyToastKey, message, StringComparison.Ordinal) && now - _lastAgyToastAt < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastAgyToastKey = message;
        _lastAgyToastAt = now;
        CloseToast();
        _toast = new HudToastForm(message);
        _toast.ShowNear(this);
    }

    private bool EnsureEnabledProviderPlaceholders()
    {
        if (!_settings.EnableAntigravity || _providerSnapshots.Any(snapshot => snapshot.ProviderId == "agy"))
        {
            return false;
        }

        _providerSnapshots.Add(new ProviderQuotaSnapshot
        {
            ProviderId = "agy",
            DisplayName = "AGY",
            Subtitle = "Gemini",
            Source = "Managed AGY",
            Status = QuotaProviderStatus.Refreshing,
            Buckets = CreateEmptyBuckets("gemini-weekly", "gemini-5h")
        });
        return true;
    }

    private static List<ProviderQuotaSnapshot> CreateInitialSnapshots()
    {
        return new List<ProviderQuotaSnapshot>
        {
            new()
            {
                ProviderId = "codex",
                DisplayName = "Codex",
                Source = "Codex CLI",
                Status = QuotaProviderStatus.Refreshing,
                UpdatedAt = DateTime.Now,
                Buckets = CreateEmptyBuckets("codex-7d", "codex-5h")
            }
        };
    }

    private static List<QuotaBucketSnapshot> CreateEmptyBuckets(string sevenDayId, string fiveHourId)
    {
        return new List<QuotaBucketSnapshot>
        {
            new() { Id = sevenDayId, Label = "7d", ShortLabel = "7d" },
            new() { Id = fiveHourId, Label = "5h", ShortLabel = "5h" }
        };
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

    private async Task ExitApplicationAsync()
    {
        if (_isExiting)
        {
            return;
        }

        AppendLog("exit requested");
        _isExiting = true;
        _refreshTimer.Stop();
        _appCts.Cancel();
        _menu.Enabled = false;
        CloseToast();

        try
        {
            await DisposePollerAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            AppendLog("poller dispose timeout during exit");
        }
        catch (OperationCanceledException)
        {
            AppendLog("poller dispose cancelled during exit");
        }
        catch (Exception ex)
        {
            AppendLog($"poller dispose failed during exit: {Shorten(ex.Message)}");
        }
        finally
        {
            _notifyIcon.Visible = false;
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(Close));
            }
            else
            {
                Close();
            }
        }
    }

    private async Task DisposePollerAsync()
    {
        if (_pollerDisposed)
        {
            return;
        }

        _pollerDisposed = true;
        AppendLog("poller dispose started");
        await _poller.DisposeAsync();
        AppendLog("poller dispose completed");
    }

    private void CloseToast()
    {
        try
        {
            _toast?.Close();
            _toast?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _toast = null;
        }
    }

    private void MainHudForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
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
        control.MouseDown -= Hud_MouseDown;
        control.MouseMove -= Hud_MouseMove;
        control.MouseUp -= Hud_MouseUp;
        control.MouseEnter -= Hud_MouseEnter;
        control.MouseLeave -= Hud_MouseLeave;

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
            ShowCurrentView(force: true);
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
            ShowCurrentView(force: true);
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
            ShowCurrentView(force: true);
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
            ShowCurrentView(force: true);
        }
        else
        {
            _isDocked = false;
            _isExpanded = true;
            ShowCurrentView(force: true);
        }
    }

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CurrentWindowRadius());
        Region?.Dispose();
        Region = new Region(path);
    }

    private int CurrentWindowRadius()
    {
        return _isDocked && !_isExpanded ? 12 : 14;
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

    private static void AppendLog(string message)
    {
        DebugLogger.Info("APP", message);
    }

    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= 180 ? text : text[..180] + "...";
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }
    }
}
