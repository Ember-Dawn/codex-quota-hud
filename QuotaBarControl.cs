using System.Drawing.Drawing2D;

namespace CodexQuotaHud;

public sealed class QuotaBarControl : Control
{
    private string _title = string.Empty;
    private double? _percent;
    private bool _showPercentText = true;
    private Color _fillColor = ColorTranslator.FromHtml("#4EA1FF");
    private Color _trackColor = ColorTranslator.FromHtml("#2A2F36");
    private Color _textColor = ColorTranslator.FromHtml("#F2F4F8");

    public QuotaBarControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 24;
        Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold);
    }

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Invalidate();
        }
    }

    public double? Percent
    {
        get => _percent;
        set
        {
            _percent = value;
            Invalidate();
        }
    }

    public bool ShowPercentText
    {
        get => _showPercentText;
        set
        {
            _showPercentText = value;
            Invalidate();
        }
    }

    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            Invalidate();
        }
    }

    public Color TrackColor
    {
        get => _trackColor;
        set
        {
            _trackColor = value;
            Invalidate();
        }
    }

    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var bounds = new Rectangle(0, 2, Width - 1, Height - 5);
        using var trackBrush = new SolidBrush(TrackColor);
        using var fillBrush = new SolidBrush(FillColor);
        using var textBrush = new SolidBrush(TextColor);
        using var trackPath = RoundedRect(bounds, bounds.Height / 2);
        e.Graphics.FillPath(trackBrush, trackPath);

        if (Percent.HasValue)
        {
            var percent = Math.Clamp(Percent.Value, 0, 100);
            var fillWidth = (int)Math.Round(bounds.Width * percent / 100);
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(bounds.Left, bounds.Top, Math.Max(fillWidth, bounds.Height), bounds.Height);
                fillBounds.Width = Math.Min(fillBounds.Width, bounds.Width);
                using var fillPath = RoundedRect(fillBounds, fillBounds.Height / 2);
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }

        var text = ShowPercentText
            ? $"{Title} {(Percent.HasValue ? $"{Percent.Value:0}%" : "--")}"
            : Title;

        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            bounds,
            TextColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
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
