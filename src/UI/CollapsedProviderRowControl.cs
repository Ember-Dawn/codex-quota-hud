namespace CodexQuotaHud;

public sealed class CollapsedProviderRowControl : UserControl
{
    private static readonly Color WindowBackColor = ColorTranslator.FromHtml("#181C22");
    private static readonly Color TextColor = ColorTranslator.FromHtml("#F2F4F8");
    private static readonly Color MutedTextColor = ColorTranslator.FromHtml("#C8D0DA");

    private readonly Label _providerLabel = new();
    private readonly QuotaBarControl _sevenDayBar = new();
    private readonly QuotaBarControl _fiveHourBar = new();
    private readonly Label _statusLabel = new();

    public CollapsedProviderRowControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = WindowBackColor;
        Size = new Size(320, 28);
        Margin = new Padding(0);

        _providerLabel.AutoSize = false;
        _providerLabel.Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
        _providerLabel.ForeColor = TextColor;
        _providerLabel.BackColor = Color.Transparent;
        _providerLabel.TextAlign = ContentAlignment.MiddleLeft;
        _providerLabel.Location = new Point(8, 3);
        _providerLabel.Size = new Size(56, 22);

        _sevenDayBar.Location = new Point(66, 3);
        _sevenDayBar.Size = new Size(112, 22);
        _fiveHourBar.Location = new Point(196, 3);
        _fiveHourBar.Size = new Size(112, 22);

        _statusLabel.AutoSize = false;
        _statusLabel.Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
        _statusLabel.ForeColor = MutedTextColor;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Location = new Point(66, 3);
        _statusLabel.Size = new Size(242, 22);
        _statusLabel.Visible = false;

        Controls.AddRange(new Control[] { _providerLabel, _sevenDayBar, _fiveHourBar, _statusLabel });
    }

    public void SetColors(Color sevenDayColor, Color fiveHourColor, Color trackColor, Color trackBorderColor)
    {
        ConfigureBar(_sevenDayBar, "7d", sevenDayColor, trackColor, trackBorderColor);
        ConfigureBar(_fiveHourBar, "5h", fiveHourColor, trackColor, trackBorderColor);
    }

    public void UpdateSnapshot(ProviderQuotaSnapshot snapshot)
    {
        SetTextIfChanged(_providerLabel, snapshot.DisplayName);
        var sevenDay = snapshot.FindBucket("7d");
        var fiveHour = snapshot.FindBucket("5h");
        _sevenDayBar.Percent = sevenDay?.RemainingPercent;
        _fiveHourBar.Percent = fiveHour?.RemainingPercent;

        var showStatus = snapshot.Status is QuotaProviderStatus.Offline or QuotaProviderStatus.Failed;
        SetTextIfChanged(_statusLabel, snapshot.ProviderId == "agy" ? "AGY offline" : snapshot.Status.ToString());
        SetVisibleIfChanged(_statusLabel, showStatus);
        SetVisibleIfChanged(_sevenDayBar, !showStatus);
        SetVisibleIfChanged(_fiveHourBar, !showStatus);
    }

    private static void ConfigureBar(QuotaBarControl bar, string title, Color fillColor, Color trackColor, Color trackBorderColor)
    {
        bar.Title = title;
        bar.ShowTitle = true;
        bar.ShowPercentText = false;
        bar.FillColor = fillColor;
        bar.TrackColor = trackColor;
        bar.TrackBorderColor = trackBorderColor;
        bar.TrackBorderWidth = 2f;
        bar.TextColor = TextColor;
        bar.BackColor = WindowBackColor;
    }

    private static void SetTextIfChanged(Label label, string text)
    {
        if (label.Text != text)
        {
            label.Text = text;
        }
    }

    private static void SetVisibleIfChanged(Control control, bool visible)
    {
        if (control.Visible != visible)
        {
            control.Visible = visible;
        }
    }
}
