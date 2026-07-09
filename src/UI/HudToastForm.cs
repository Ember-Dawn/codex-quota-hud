using System.Drawing.Drawing2D;

namespace CodexQuotaHud;

public sealed class HudToastForm : Form
{
    private readonly Label _messageLabel = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    public HudToastForm(string message, int durationMilliseconds = 6500)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = ColorTranslator.FromHtml("#222A33");
        ForeColor = ColorTranslator.FromHtml("#F2F4F8");
        Size = new Size(330, 72);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _messageLabel.AutoSize = false;
        _messageLabel.Text = message;
        _messageLabel.Font = new Font(FontFamily.GenericSansSerif, 8.8f, FontStyle.Regular);
        _messageLabel.ForeColor = ForeColor;
        _messageLabel.BackColor = Color.Transparent;
        _messageLabel.Location = new Point(12, 10);
        _messageLabel.Size = new Size(306, 52);
        _messageLabel.TextAlign = ContentAlignment.MiddleLeft;

        Controls.Add(_messageLabel);

        _timer.Interval = durationMilliseconds;
        _timer.Tick += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowNear(Form owner)
    {
        var screen = Screen.FromControl(owner).WorkingArea;
        var preferred = new Point(owner.Right + 8, owner.Top);
        if (preferred.X + Width > screen.Right)
        {
            preferred.X = owner.Left - Width - 8;
        }

        if (preferred.Y + Height > screen.Bottom)
        {
            preferred.Y = screen.Bottom - Height;
        }

        preferred.X = Math.Clamp(preferred.X, screen.Left, screen.Right - Width);
        preferred.Y = Math.Clamp(preferred.Y, screen.Top, screen.Bottom - Height);
        Location = preferred;
        Show(owner);
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var pen = new Pen(ColorTranslator.FromHtml("#4C5A69"), 1f);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 12);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
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
