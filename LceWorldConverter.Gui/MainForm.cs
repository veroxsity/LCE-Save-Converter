using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LceWorldConverter;

namespace LceWorldConverter.Gui;

internal sealed class MainForm : Form
{
    private static readonly Color WindowTop = Color.FromArgb(241, 247, 245);
    private static readonly Color WindowBottom = Color.FromArgb(225, 238, 233);
    private static readonly Color CardBg = Color.FromArgb(250, 252, 251);
    private static readonly Color Border = Color.FromArgb(194, 215, 206);
    private static readonly Color Accent = Color.FromArgb(17, 119, 92);
    private static readonly Color AccentHover = Color.FromArgb(22, 137, 106);
    private static readonly Color HeroText = Color.FromArgb(24, 56, 47);
    private static readonly Color MutedText = Color.FromArgb(80, 98, 91);

    private readonly TextBox _zipPathTextBox;
    private readonly TextBox _outputPathTextBox;
    private readonly ComboBox _worldTypeComboBox;
    private readonly CheckBox _allDimensionsCheckBox;
    private readonly CheckBox _copyPlayersCheckBox;
    private readonly CheckBox _preserveEntitiesCheckBox;
    private readonly Button _convertButton;
    private readonly TextBox _logTextBox;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;

    public MainForm()
    {
        Text = "LCE World Converter";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(880, 660);
        Size = new Size(980, 740);
        Font = new Font("Bahnschrift", 10F);
        DoubleBuffered = true;
        BackColor = WindowTop;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(26, 20, 26, 20),
            BackColor = WindowTop,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        shell.Controls.Add(CreateHeroCard());

        _zipPathTextBox = CreatePathTextBox();
        _outputPathTextBox = CreatePathTextBox();
        shell.Controls.Add(CreateInputCard());

        _worldTypeComboBox = CreateWorldTypeComboBox();
        _allDimensionsCheckBox = CreateOptionCheckBox("Convert Nether and End");
        _copyPlayersCheckBox = CreateOptionCheckBox("Copy numeric player data");
        _preserveEntitiesCheckBox = CreateOptionCheckBox("Preserve entities and tile data");
        shell.Controls.Add(CreateOptionsCard());

        _convertButton = CreatePrimaryButton("Convert");
        _convertButton.Click += ConvertClicked;

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 20,
            Style = ProgressBarStyle.Blocks,
            Margin = new Padding(0, 4, 0, 0),
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = "Ready",
            ForeColor = HeroText,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 6, 0, 0),
        };

        shell.Controls.Add(CreateActionCard());

        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(17, 35, 30),
            ForeColor = Color.FromArgb(193, 226, 216),
            Font = new Font("Consolas", 9.5F),
            Margin = new Padding(6),
        };

        shell.Controls.Add(CreateLogCard());
        Controls.Add(shell);
    }

    private Control CreateHeroCard()
    {
        var card = CreateCard(new Padding(18, 16, 18, 14));

        var title = new Label
        {
            AutoSize = true,
            Text = "LCE World Converter",
            ForeColor = HeroText,
            Font = new Font("Bahnschrift SemiBold", 22F, FontStyle.Bold),
        };

        var subtitle = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            Margin = new Padding(0, 8, 0, 0),
            Text = "Select a zipped Java world, choose an output folder, and convert to saveData.ms.",
            ForeColor = MutedText,
            Font = new Font("Bahnschrift", 10.5F),
        };

        var badge = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            Text = "ZIP INPUT • GUI WORKFLOW",
            ForeColor = Color.White,
            BackColor = Accent,
            Padding = new Padding(8, 4, 8, 4),
            Font = new Font("Bahnschrift", 8.5F, FontStyle.Bold),
        };

        card.Controls.Add(title);
        card.Controls.Add(subtitle);
        card.Controls.Add(badge);
        return card;
    }

    private Control CreateInputCard()
    {
        var card = CreateCard();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(CreatePathRow("World Zip", _zipPathTextBox, "Explore...", BrowseZip), 0, 0);
        layout.Controls.Add(CreatePathRow("Output Folder", _outputPathTextBox, "Browse...", BrowseOutputFolder), 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private Control CreateOptionsCard()
    {
        var card = CreateCard();
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = CardBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var worldTypeLabel = new Label
        {
            AutoSize = true,
            Text = "World Type",
            ForeColor = HeroText,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            Margin = new Padding(0, 8, 12, 0),
        };

        panel.Controls.Add(worldTypeLabel, 0, 0);
        panel.Controls.Add(_worldTypeComboBox, 1, 0);

        var checkboxFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = CardBg,
            Margin = new Padding(0, 10, 0, 0),
        };
        checkboxFlow.Controls.Add(_allDimensionsCheckBox);
        checkboxFlow.Controls.Add(_copyPlayersCheckBox);
        checkboxFlow.Controls.Add(_preserveEntitiesCheckBox);

        panel.Controls.Add(new Label(), 0, 1);
        panel.Controls.Add(checkboxFlow, 1, 1);

        card.Controls.Add(panel);
        return card;
    }

    private Control CreateActionCard()
    {
        var card = CreateCard();

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            BackColor = CardBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(_convertButton, 0, 0);
        panel.Controls.Add(_progressBar, 1, 0);
        panel.Controls.Add(_statusLabel, 2, 0);

        card.Controls.Add(panel);
        return card;
    }

    private Control CreateLogCard()
    {
        var card = CreateCard(new Padding(0), autoSize: false, dock: DockStyle.Fill);
        card.MinimumSize = new Size(0, 220);

        var header = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 38,
            Padding = new Padding(14, 10, 0, 0),
            Text = "Conversion Activity",
            ForeColor = HeroText,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            BackColor = Color.FromArgb(233, 242, 238),
        };

        var inner = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(15, 31, 27),
        };
        inner.Controls.Add(_logTextBox);

        card.Controls.Add(inner);
        card.Controls.Add(header);
        return card;
    }

    private Panel CreateCard(Padding? contentPadding = null, bool autoSize = true, DockStyle dock = DockStyle.Top)
    {
        var card = new Panel
        {
            Dock = dock,
            AutoSize = autoSize,
            Margin = new Padding(0, 0, 0, 12),
            Padding = contentPadding ?? new Padding(16, 14, 16, 14),
            BackColor = CardBg,
            BorderStyle = BorderStyle.FixedSingle,
        };

        return card;
    }

    private TextBox CreatePathTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = HeroText,
            Margin = new Padding(0),
        };
    }

    private ComboBox CreateWorldTypeComboBox()
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = HeroText,
            Margin = new Padding(0),
        };

        combo.Items.AddRange([
            "classic",
            "small",
            "medium",
            "large",
            "flat",
            "flat-small",
            "flat-medium",
            "flat-large",
        ]);
        combo.SelectedItem = "classic";
        return combo;
    }

    private static CheckBox CreateOptionCheckBox(string text)
    {
        return new CheckBox
        {
            AutoSize = true,
            Text = text,
            ForeColor = HeroText,
            Font = new Font("Bahnschrift", 9.5F),
            Margin = new Padding(0, 4, 18, 4),
        };
    }

    private Button CreatePrimaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Accent,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;

        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled)
                button.BackColor = AccentHover;
        };
        button.MouseLeave += (_, _) =>
        {
            button.BackColor = button.Enabled ? Accent : Color.FromArgb(145, 150, 148);
        };

        return button;
    }

    private Control CreatePathRow(string labelText, TextBox textBox, string buttonText, EventHandler onClick)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = false,
            Height = 36,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = CardBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            ForeColor = HeroText,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            Margin = new Padding(0, 8, 10, 0),
        };

        var button = new Button
        {
            Text = buttonText,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(236, 246, 241),
            ForeColor = HeroText,
            Cursor = Cursors.Hand,
            Margin = new Padding(12, 0, 0, 0),
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.Click += onClick;

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(textBox, 1, 0);
        panel.Controls.Add(button, 2, 0);

        return panel;
    }

    private void BrowseZip(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Zip archives (*.zip)|*.zip",
            Title = "Select Java World Zip",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _zipPathTextBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            string suggested = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetFileNameWithoutExtension(dialog.FileName));
            _outputPathTextBox.Text = suggested;
        }
    }

    private void BrowseOutputFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where saveData.ms should be written",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_outputPathTextBox.Text) ? _outputPathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputPathTextBox.Text = dialog.SelectedPath;
    }

    private async void ConvertClicked(object? sender, EventArgs e)
    {
        if (!ValidateInputs())
            return;

        string outputSavePath = Path.Combine(_outputPathTextBox.Text, "saveData.ms");
        if (File.Exists(outputSavePath))
        {
            DialogResult overwrite = MessageBox.Show(
                this,
                "The selected output folder already contains saveData.ms. Do you want to overwrite it?",
                "Overwrite Existing Output",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (overwrite != DialogResult.Yes)
                return;
        }

        ToggleBusy(true, "Converting...");
        _logTextBox.Clear();

        try
        {
            ConversionOptions options = BuildOptions();
            var logger = new UiConversionLogger(AppendLog);
            var service = new LceWorldConversionService();

            ConversionResult result = await Task.Run(() => service.Convert(options, logger));

            AppendLog(string.Empty);
            AppendLog($"Finished: {result.OutputPath}");

            MessageBox.Show(
                this,
                $"Conversion complete.\n\nOutput: {result.OutputPath}",
                "LCE World Converter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "Conversion Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            ToggleBusy(false, "Ready");
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_zipPathTextBox.Text) || !File.Exists(_zipPathTextBox.Text))
        {
            MessageBox.Show(this, "Select a valid world zip first.", "Missing Zip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!string.Equals(Path.GetExtension(_zipPathTextBox.Text), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "The input must be a .zip archive.", "Invalid Zip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            MessageBox.Show(this, "Select an output folder.", "Missing Output Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Directory.CreateDirectory(_outputPathTextBox.Text);
        return true;
    }

    private ConversionOptions BuildOptions()
    {
        string worldType = (_worldTypeComboBox.SelectedItem as string) ?? "classic";
        bool flatWorld = worldType.StartsWith("flat", StringComparison.OrdinalIgnoreCase);
        int xzSize = worldType switch
        {
            "small" or "flat-small" => 64,
            "medium" or "flat-medium" => 192,
            "large" or "flat-large" => 320,
            _ => 54,
        };

        string sizeLabel = worldType switch
        {
            "small" or "flat-small" => "Small",
            "medium" or "flat-medium" => "Medium",
            "large" or "flat-large" => "Large",
            _ => "Classic",
        };

        return new ConversionOptions
        {
            InputPath = _zipPathTextBox.Text,
            OutputDirectory = _outputPathTextBox.Text,
            XzSize = xzSize,
            SizeLabel = sizeLabel,
            FlatWorld = flatWorld,
            ConvertAllDimensions = _allDimensionsCheckBox.Checked,
            CopyPlayers = _copyPlayersCheckBox.Checked,
            PreserveEntities = _preserveEntitiesCheckBox.Checked,
        };
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _convertButton.Enabled = !isBusy;
        _worldTypeComboBox.Enabled = !isBusy;
        _allDimensionsCheckBox.Enabled = !isBusy;
        _copyPlayersCheckBox.Enabled = !isBusy;
        _preserveEntitiesCheckBox.Enabled = !isBusy;
        _statusLabel.Text = status;
        _progressBar.Style = isBusy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        _convertButton.BackColor = _convertButton.Enabled ? Accent : Color.FromArgb(145, 150, 148);
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        if (_logTextBox.TextLength > 0)
            _logTextBox.AppendText(Environment.NewLine);

        _logTextBox.AppendText(message);

        if (!string.IsNullOrWhiteSpace(message))
            _statusLabel.Text = message;
    }

    private sealed class UiConversionLogger(Action<string> appendLog) : IConversionLogger
    {
        public void Info(string message) => appendLog(message);

        public void Error(string message) => appendLog(message);
    }
}
