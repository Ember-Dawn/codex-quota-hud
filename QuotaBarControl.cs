using System.Drawing.Drawing2D;

namespace CodexQuotaHud;

public sealed class QuotaBarControl : Control
{
    private string _title = string.Empty;
    private double? _percent;
    private bool _showPercentText = true;
    private bool _showTitle = true;
    private Color _fillColor = ColorTranslator.FromHtml("#4EA1FF");
    private Color _trackColor = ColorTranslator.FromHtml("#303740");
    private Color _trackBorderColor = ColorTranslator.FromHtml("#7A8796");
    private Color _textColor = ColorTranslator.FromHtml("#F2F4F8");
    private float _trackBorderWidth = 2f;

    public QuotaBarControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 22;
        Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
    }

    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    public double? Percent
    {
        get => _percent;
        set { _percent = value; Invalidate(); }
    }

    public bool ShowPercentText
    {
        get => _showPercentText;
        set { _showPercentText = value; Invalidate(); }
    }

    public bool ShowTitle
    {
        get => _showTitle;
        set { _showTitle = value; Invalidate(); }
    }

    public Color FillColor
    {
        get => _fillColor;
        set { _fillColor = value; Invalidate(); }
    }

    public Color TrackColor
    {
        get => _trackColor;
        set { _trackColor = value; Invalidate(); }
    }

    public Color TrackBorderColor
    {
        get => _trackBorderColor;
        set { _trackBorderColor = value; Invalidate(); }
    }

    public float TrackBorderWidth
    {
        get => _trackBorderWidth;
        set { _trackBorderWidth = Math.Max(1f, value); Invalidate(); }
    }

    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var inset = TrackBorderWidth / 2f + 1f;
        var trackBounds = new RectangleF(inset, inset, Width - inset * 2f, Height - inset * 2f);
        if (trackBounds.Width <= 0 || trackBounds.Height <= 0)
        {
            return;
        }

        using var trackBrush = new SolidBrush(TrackColor);
        using var fillBrush = new SolidBrush(FillColor);
        using var borderPen = new Pen(TrackBorderColor, TrackBorderWidth);
        using var trackPath = RoundedRect(trackBounds, 8f);

        e.Graphics.FillPath(trackBrush, trackPath);

        if (Percent.HasValue)
        {
            var percent = Math.Clamp(Percent.Value, 0, 100);
            var inner = RectangleF.Inflate(trackBounds, -TrackBorderWidth, -TrackBorderWidth);
            var fillWidth = inner.Width * (float)(percent / 100d);
            if (fillWidth > 0 && inner.Width > 0 && inner.Height > 0)
            {
                var state = e.Graphics.Save();
                e.Graphics.SetClip(trackPath);
                e.Graphics.FillRectangle(fillBrush, inner.Left, inner.Top, fillWidth, inner.Height);
                e.Graphics.Restore(state);
            }
        }

        e.Graphics.DrawPath(borderPen, trackPath);

        var text = BuildText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                Rectangle.Round(trackBounds),
                TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private string BuildText()
    {
        var percentText = Percent.HasValue ? $"{Percent.Value:0}%" : "--";
        return (ShowTitle, ShowPercentText) switch
        {
            (true, true) => $"{Title} {percentText}",
            (true, false) => Title,
            (false, true) => percentText,
            _ => string.Empty
        };
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        radius = Math.Max(1f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
