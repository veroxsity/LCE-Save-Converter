using System.Drawing;
using System.Windows.Forms;
using LceWorldConverter;

namespace LceWorldConverter.Gui;

internal sealed class MainForm : Form
{
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
        MinimumSize = new Size(840, 620);
        Size = new Size(920, 680);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headerLabel = new Label
        {
            AutoSize = true,
            Text = "Pick a zipped Java world, choose an output folder, and convert it into saveData.ms.",
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12),
        };
        layout.Controls.Add(headerLabel);

        _zipPathTextBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        layout.Controls.Add(CreatePathRow("World Zip", _zipPathTextBox, "Explore...", BrowseZip));

        _outputPathTextBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        layout.Controls.Add(CreatePathRow("Output Folder", _outputPathTextBox, "Browse...", BrowseOutputFolder));

        _worldTypeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _worldTypeComboBox.Items.AddRange([
            "classic",
            "small",
            "medium",
            "large",
            "flat",
            "flat-small",
            "flat-medium",
            "flat-large",
        ]);
        _worldTypeComboBox.SelectedItem = "classic";

        _allDimensionsCheckBox = new CheckBox { AutoSize = true, Text = "Convert Nether and End" };
        _copyPlayersCheckBox = new CheckBox { AutoSize = true, Text = "Copy numeric player data" };
        _preserveEntitiesCheckBox = new CheckBox { AutoSize = true, Text = "Preserve entities and tile data" };

        layout.Controls.Add(CreateOptionsPanel());

        _convertButton = new Button
        {
            AutoSize = true,
            Text = "Convert",
            Padding = new Padding(14, 8, 14, 8),
        };
        _convertButton.Click += ConvertClicked;

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Blocks,
            Height = 24,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = "Ready",
            TextAlign = ContentAlignment.MiddleLeft,
        };

        layout.Controls.Add(CreateActionPanel());

        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10F),
        };
        layout.Controls.Add(_logTextBox);

        Controls.Add(layout);
    }

    private Control CreatePathRow(string labelText, TextBox textBox, string buttonText, EventHandler onClick)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
        };
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(textBox, 1, 0);

        var button = new Button
        {
            Text = buttonText,
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 0, 0),
        };
        button.Click += onClick;
        panel.Controls.Add(button, 2, 0);

        return panel;
    }

    private Control CreateOptionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var worldTypeLabel = new Label
        {
            AutoSize = true,
            Text = "World Type",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
        };
        panel.Controls.Add(worldTypeLabel, 0, 0);
        panel.Controls.Add(_worldTypeComboBox, 1, 0);

        var checkboxFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0),
        };
        checkboxFlow.Controls.Add(_allDimensionsCheckBox);
        checkboxFlow.Controls.Add(_copyPlayersCheckBox);
        checkboxFlow.Controls.Add(_preserveEntitiesCheckBox);

        panel.Controls.Add(new Label(), 0, 1);
        panel.Controls.Add(checkboxFlow, 1, 1);

        return panel;
    }

    private Control CreateActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(_convertButton, 0, 0);
        panel.Controls.Add(_progressBar, 1, 0);
        panel.Controls.Add(_statusLabel, 2, 0);

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