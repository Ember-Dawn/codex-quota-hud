using System.Text.RegularExpressions;

namespace CodexQuotaHud;

public sealed class SettingsForm : Form
{
    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly (string Text, int Seconds)[] RefreshOptions =
    {
        ("30 sec", 30),
        ("1 min", 60),
        ("5 min", 300),
        ("10 min", 600),
        ("20 min", 1200)
    };

    private readonly ComboBox _autoRefreshCombo = new();
    private readonly TextBox _sevenDayColorBox = new();
    private readonly TextBox _fiveHourColorBox = new();
    private readonly TextBox _trackBorderColorBox = new();
    private readonly Panel _sevenDaySwatch = new();
    private readonly Panel _fiveHourSwatch = new();
    private readonly Panel _trackBorderSwatch = new();

    public AppSettings ResultSettings { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        ResultSettings = settings.Clone();
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(390, 258);
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font(FontFamily.GenericSansSerif, 9f);

        BuildUi();
        LoadFields(ResultSettings);
    }

    public static bool IsValidHexColor(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value);
    }

    private void BuildUi()
    {
        var titleLabel = new Label
        {
            Text = "Settings",
            Font = new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Bold),
            Location = new Point(14, 12),
            Size = new Size(200, 24)
        };

        var autoRefreshLabel = new Label
        {
            Text = "Auto Refresh",
            Location = new Point(14, 48),
            Size = new Size(120, 20)
        };

        _autoRefreshCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoRefreshCombo.Location = new Point(14, 70);
        _autoRefreshCombo.Size = new Size(120, 24);
        foreach (var option in RefreshOptions)
        {
            _autoRefreshCombo.Items.Add(option.Text);
        }

        var colorsLabel = new Label
        {
            Text = "Colors",
            Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold),
            Location = new Point(14, 106),
            Size = new Size(120, 20)
        };

        AddColorRow("7d Color", _sevenDayColorBox, _sevenDaySwatch, y: 132, addChangeButton: false);
        AddColorRow("5h Color", _fiveHourColorBox, _fiveHourSwatch, y: 162, addChangeButton: false);
        AddColorRow("Track Border Color", _trackBorderColorBox, _trackBorderSwatch, y: 192, addChangeButton: true);

        var resetDefaultsButton = new Button
        {
            Text = "Reset Defaults",
            Location = new Point(14, 226),
            Size = new Size(110, 26)
        };
        resetDefaultsButton.Click += (_, _) => LoadFields(AppSettings.Default());

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(254, 226),
            Size = new Size(58, 26)
        };

        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(318, 226),
            Size = new Size(58, 26)
        };
        saveButton.Click += SaveButton_Click;

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[]
        {
            titleLabel,
            autoRefreshLabel,
            _autoRefreshCombo,
            colorsLabel,
            resetDefaultsButton,
            cancelButton,
            saveButton
        });
    }

    private void AddColorRow(string labelText, TextBox textBox, Panel swatch, int y, bool addChangeButton)
    {
        var label = new Label
        {
            Text = labelText,
            Location = new Point(14, y + 3),
            Size = new Size(118, 20)
        };

        textBox.Location = new Point(138, y);
        textBox.Size = new Size(110, 24);
        textBox.CharacterCasing = CharacterCasing.Upper;
        textBox.TextChanged += (_, _) => UpdateSwatch(textBox, swatch);

        swatch.Location = new Point(258, y + 1);
        swatch.Size = new Size(34, 22);
        swatch.BorderStyle = BorderStyle.FixedSingle;

        Controls.AddRange(new Control[] { label, textBox, swatch });

        if (!addChangeButton)
        {
            return;
        }

        var changeButton = new Button
        {
            Text = "Change",
            Location = new Point(302, y - 1),
            Size = new Size(74, 26)
        };
        changeButton.Click += (_, _) => PickColor(textBox);
        Controls.Add(changeButton);
    }

    private void LoadFields(AppSettings settings)
    {
        var optionIndex = Array.FindIndex(RefreshOptions, option => option.Seconds == settings.AutoRefreshSeconds);
        _autoRefreshCombo.SelectedIndex = optionIndex >= 0 ? optionIndex : 1;
        _sevenDayColorBox.Text = settings.SevenDayColor.ToUpperInvariant();
        _fiveHourColorBox.Text = settings.FiveHourColor.ToUpperInvariant();
        _trackBorderColorBox.Text = settings.TrackBorderColor.ToUpperInvariant();
        UpdateSwatch(_sevenDayColorBox, _sevenDaySwatch);
        UpdateSwatch(_fiveHourColorBox, _fiveHourSwatch);
        UpdateSwatch(_trackBorderColorBox, _trackBorderSwatch);
    }

    private static void UpdateSwatch(TextBox textBox, Panel swatch)
    {
        swatch.BackColor = IsValidHexColor(textBox.Text)
            ? ColorTranslator.FromHtml(textBox.Text)
            : SystemColors.Control;
    }

    private void PickColor(TextBox textBox)
    {
        using var dialog = new ColorDialog { FullOpen = true };
        if (IsValidHexColor(textBox.Text))
        {
            dialog.Color = ColorTranslator.FromHtml(textBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            textBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (!IsValidHexColor(_sevenDayColorBox.Text) ||
            !IsValidHexColor(_fiveHourColorBox.Text) ||
            !IsValidHexColor(_trackBorderColorBox.Text))
        {
            MessageBox.Show(this, "Invalid color value. Please use #RRGGBB format.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedIndex = Math.Max(0, _autoRefreshCombo.SelectedIndex);
        ResultSettings = new AppSettings
        {
            AutoRefreshSeconds = RefreshOptions[selectedIndex].Seconds,
            SevenDayColor = _sevenDayColorBox.Text.ToUpperInvariant(),
            FiveHourColor = _fiveHourColorBox.Text.ToUpperInvariant(),
            TrackBorderColor = _trackBorderColorBox.Text.ToUpperInvariant()
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
