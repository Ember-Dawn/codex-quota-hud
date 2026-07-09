using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexQuotaHud;

public sealed class ProviderCardControl : UserControl
{
    private static readonly Color CardBackColor = ColorTranslator.FromHtml("#1D232B");
    private static readonly Color CardBorderColor = ColorTranslator.FromHtml("#34404D");
    private static readonly Color TextColor = ColorTranslator.FromHtml("#F2F4F8");
    private static readonly Color MutedTextColor = ColorTranslator.FromHtml("#C8D0DA");

    private readonly Label _titleLabel = new();
    private readonly Label _metaLabel = new();
    private readonly Label _sevenDayLabel = new();
    private readonly Label _fiveHourLabel = new();
    private readonly QuotaBarControl _sevenDayBar = new();
    private readonly QuotaBarControl _fiveHourBar = new();
    private readonly Label _sevenDayResetLabel = new();
    private readonly Label _fiveHourResetLabel = new();
    private readonly Label _footerLabel = new();

    public ProviderCardControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = CardBackColor;
        ForeColor = TextColor;
        Size = new Size(350, 96);
        Margin = new Padding(0, 0, 0, 8);

        ConfigureLabel(_titleLabel, new Point(12, 8), new Size(188, 22), 10f, FontStyle.Bold, TextColor, ContentAlignment.MiddleLeft);
        ConfigureLabel(_metaLabel, new Point(208, 8), new Size(128, 22), 8.5f, FontStyle.Bold, MutedTextColor, ContentAlignment.MiddleRight);
        ConfigureLabel(_sevenDayLabel, new Point(12, 36), new Size(30, 22), 8.8f, FontStyle.Bold, TextColor, ContentAlignment.MiddleLeft);
        ConfigureLabel(_fiveHourLabel, new Point(12, 65), new Size(30, 22), 8.8f, FontStyle.Bold, TextColor, ContentAlignment.MiddleLeft);
        ConfigureLabel(_sevenDayResetLabel, new Point(220, 36), new Size(116, 22), 8.5f, FontStyle.Bold, TextColor, ContentAlignment.MiddleRight);
        ConfigureLabel(_fiveHourResetLabel, new Point(220, 65), new Size(116, 22), 8.5f, FontStyle.Bold, TextColor, ContentAlignment.MiddleRight);
        ConfigureLabel(_footerLabel, new Point(12, 92), new Size(324, 18), 8.2f, FontStyle.Regular, MutedTextColor, ContentAlignment.MiddleLeft);

        _sevenDayLabel.Text = "7d";
        _fiveHourLabel.Text = "5h";

        _sevenDayBar.Location = new Point(48, 35);
        _sevenDayBar.Size = new Size(164, 23);
        _fiveHourBar.Location = new Point(48, 64);
        _fiveHourBar.Size = new Size(164, 23);

        Controls.AddRange(new Control[]
        {
            _titleLabel,
            _metaLabel,
            _sevenDayLabel,
            _fiveHourLabel,
            _sevenDayBar,
            _fiveHourBar,
            _sevenDayResetLabel,
            _fiveHourResetLabel,
            _footerLabel
        });
    }

    public void SetColors(Color sevenDayColor, Color fiveHourColor, Color trackColor, Color trackBorderColor)
    {
        ConfigureBar(_sevenDayBar, sevenDayColor, trackColor, trackBorderColor);
        ConfigureBar(_fiveHourBar, fiveHourColor, trackColor, trackBorderColor);
    }

    public bool UpdateSnapshot(ProviderQuotaSnapshot snapshot)
    {
        SetTextIfChanged(_titleLabel, string.IsNullOrWhiteSpace(snapshot.Subtitle)
            ? snapshot.DisplayName
            : $"{snapshot.DisplayName} · {snapshot.Subtitle}");
        SetTextIfChanged(_metaLabel, FormatMeta(snapshot));

        var sevenDay = snapshot.FindBucket("7d");
        var fiveHour = snapshot.FindBucket("5h");
        _sevenDayBar.Percent = sevenDay?.RemainingPercent;
        _fiveHourBar.Percent = fiveHour?.RemainingPercent;
        SetTextIfChanged(_sevenDayResetLabel, FormatReset(sevenDay?.ResetAt, includeDate: true));
        SetTextIfChanged(_fiveHourResetLabel, FormatReset(fiveHour?.ResetAt, includeDate: false));

        var footer = FormatFooter(snapshot);
        SetTextIfChanged(_footerLabel, footer);
        SetVisibleIfChanged(_footerLabel, !string.IsNullOrWhiteSpace(footer));

        var desiredHeight = _footerLabel.Visible ? 116 : 96;
        if (Height == desiredHeight)
        {
            return false;
        }

        Height = desiredHeight;
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var fill = new SolidBrush(CardBackColor);
        using var pen = new Pen(CardBorderColor, 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    private static string FormatMeta(ProviderQuotaSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            QuotaProviderStatus.Ok when snapshot.ProviderId == "agy" && snapshot.CheckedAt.HasValue => $"Checked {snapshot.CheckedAt.Value:HH:mm}",
            QuotaProviderStatus.Ok when snapshot.UpdatedAt != default => $"Updated {snapshot.UpdatedAt:HH:mm}",
            QuotaProviderStatus.Offline => "Offline",
            QuotaProviderStatus.Failed => "Failed",
            QuotaProviderStatus.Refreshing => "Refreshing...",
            QuotaProviderStatus.Disabled => "Disabled",
            _ => string.Empty
        };
    }

    private static string FormatFooter(ProviderQuotaSnapshot snapshot)
    {
        return snapshot.Status is QuotaProviderStatus.Offline or QuotaProviderStatus.Failed
            ? Shorten(snapshot.ErrorMessage ?? (snapshot.ProviderId == "agy" ? "AGY offline" : "Failed"))
            : string.Empty;
    }

    private static string FormatReset(DateTime? resetAt, bool includeDate)
    {
        if (!resetAt.HasValue)
        {
            return "R --";
        }

        return includeDate
            ? "R " + resetAt.Value.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)
            : "R " + resetAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static void ConfigureBar(QuotaBarControl bar, Color fillColor, Color trackColor, Color trackBorderColor)
    {
        bar.Title = string.Empty;
        bar.ShowTitle = false;
        bar.ShowPercentText = true;
        bar.FillColor = fillColor;
        bar.TrackColor = trackColor;
        bar.TrackBorderColor = trackBorderColor;
        bar.TrackBorderWidth = 2f;
        bar.TextColor = TextColor;
        bar.BackColor = CardBackColor;
    }

    private static void ConfigureLabel(Label label, Point location, Size size, float fontSize, FontStyle style, Color color, ContentAlignment align)
    {
        label.AutoSize = false;
        label.Font = new Font(FontFamily.GenericSansSerif, fontSize, style);
        label.ForeColor = color;
        label.BackColor = Color.Transparent;
        label.TextAlign = align;
        label.Location = location;
        label.Size = size;
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

    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        return text.Length <= 78 ? text : text[..78] + "...";
    }
}
