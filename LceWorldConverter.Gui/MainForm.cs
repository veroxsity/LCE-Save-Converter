using System.Drawing;
using System.Windows.Forms;
using LceWorldConverter;

namespace LceWorldConverter.Gui;

internal sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(17, 119, 92);
    private static readonly Color AccentHover = Color.FromArgb(22, 137, 106);
    private static readonly Color HeroText = Color.FromArgb(24, 56, 47);

    private readonly TextBox _javaInputTextBox;
    private readonly TextBox _javaOutputTextBox;
    private readonly ComboBox _javaWorldTypeComboBox;
    private readonly CheckBox _javaAllDimensionsCheckBox;
    private readonly CheckBox _javaCopyPlayersCheckBox;
    private readonly CheckBox _javaPreserveEntitiesCheckBox;
    private readonly Button _javaConvertButton;

    private readonly TextBox _lceInputTextBox;
    private readonly TextBox _lceOutputTextBox;
    private readonly CheckBox _lceAllDimensionsCheckBox;
    private readonly CheckBox _lceCopyPlayersCheckBox;
    private readonly Button _lceConvertButton;

    private readonly TextBox _logTextBox;
    private readonly Label _statusLabel;

    public MainForm()
    {
        Text = "LCE World Converter";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);
        Size = new Size(800, 600);
        Font = new Font("Bahnschrift", 10F);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        shell.Controls.Add(CreateHeader(), 0, 0);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(18, 8),
        };

        _javaInputTextBox = CreatePathTextBox();
        _javaOutputTextBox = CreatePathTextBox();
        _javaWorldTypeComboBox = CreateWorldTypeComboBox();
        _javaAllDimensionsCheckBox = CreateCheckBox("Convert Nether and End");
        _javaCopyPlayersCheckBox = CreateCheckBox("Copy numeric player data");
        _javaPreserveEntitiesCheckBox = CreateCheckBox("Preserve entities and tile data");
        _javaConvertButton = CreatePrimaryButton("Convert To LCE");
        _javaConvertButton.Click += ConvertJavaToLceClicked;

        _lceInputTextBox = CreatePathTextBox();
        _lceOutputTextBox = CreatePathTextBox();
        _lceAllDimensionsCheckBox = CreateCheckBox("Export Nether and End");
        _lceCopyPlayersCheckBox = CreateCheckBox("Export players/*.dat");
        _lceConvertButton = CreatePrimaryButton("Convert To Java");
        _lceConvertButton.Click += ConvertLceToJavaClicked;

        tabs.TabPages.Add(CreateJavaToLceTab());
        tabs.TabPages.Add(CreateLceToJavaTab());
        shell.Controls.Add(tabs, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            Text = "Ready",
            ForeColor = HeroText,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            Padding = new Padding(4, 2, 0, 0),
        };

        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5F),
            BackColor = Color.FromArgb(17, 35, 30),
            ForeColor = Color.FromArgb(193, 226, 216),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var logPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 0),
        };
        logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logPanel.Controls.Add(_statusLabel, 0, 0);
        logPanel.Controls.Add(_logTextBox, 0, 1);

        shell.Controls.Add(logPanel, 0, 2);
        Controls.Add(shell);
    }

    private Control CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            Padding = new Padding(0, 0, 0, 12),
        };

        var title = new Label
        {
            Text = "LCE World Converter",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Bahnschrift SemiBold", 22F, FontStyle.Bold),
            ForeColor = HeroText,
        };

        var subtitle = new Label
        {
            Text = "Convert in both directions: Java world folder/zip <-> LCE saveData.ms",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Bahnschrift", 10.5F),
            ForeColor = Color.FromArgb(75, 95, 88),
        };

        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private TabPage CreateJavaToLceTab()
    {
        var page = new TabPage("Java -> LCE")
        {
            BackColor = Color.White,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreatePathRow("Java World", _javaInputTextBox, "Browse...", BrowseJavaInput), 0, 0);
        layout.Controls.Add(CreatePathRow("Output Folder", _javaOutputTextBox, "Browse...", BrowseJavaOutput), 0, 1);
        layout.Controls.Add(CreateJavaOptionsPanel(), 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 12, 0, 0),
        };
        actions.Controls.Add(_javaConvertButton);

        layout.Controls.Add(actions, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private Control CreateJavaOptionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "World Type",
            AutoSize = true,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            Margin = new Padding(0, 8, 8, 0),
        }, 0, 0);
        panel.Controls.Add(_javaWorldTypeComboBox, 1, 0);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        checks.Controls.Add(_javaAllDimensionsCheckBox);
        checks.Controls.Add(_javaCopyPlayersCheckBox);
        checks.Controls.Add(_javaPreserveEntitiesCheckBox);

        panel.Controls.Add(new Label(), 0, 1);
        panel.Controls.Add(checks, 1, 1);
        return panel;
    }

    private TabPage CreateLceToJavaTab()
    {
        var page = new TabPage("LCE -> Java")
        {
            BackColor = Color.White,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreatePathRow("saveData.ms", _lceInputTextBox, "Browse...", BrowseLceInput), 0, 0);
        layout.Controls.Add(CreatePathRow("Output Folder", _lceOutputTextBox, "Browse...", BrowseLceOutput), 0, 1);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(140, 12, 0, 0),
            WrapContents = true,
        };
        checks.Controls.Add(_lceAllDimensionsCheckBox);
        checks.Controls.Add(_lceCopyPlayersCheckBox);
        layout.Controls.Add(checks, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 12, 0, 0),
        };
        actions.Controls.Add(_lceConvertButton);
        layout.Controls.Add(actions, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    private static TextBox CreatePathTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
        };
    }

    private static ComboBox CreateWorldTypeComboBox()
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
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

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(0, 4, 16, 4),
        };
    }

    private Button CreatePrimaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(18, 10, 18, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
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

    private Control CreatePathRow(string labelText, TextBox textBox, string buttonText, EventHandler clickHandler)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = false,
            Height = 38,
            Margin = new Padding(0, 0, 0, 8),
        };

        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Font = new Font("Bahnschrift", 10F, FontStyle.Bold),
            Margin = new Padding(0, 10, 8, 0),
        }, 0, 0);

        row.Controls.Add(textBox, 1, 0);

        var browse = new Button
        {
            Text = buttonText,
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        browse.Click += clickHandler;
        row.Controls.Add(browse, 2, 0);

        return row;
    }

    private void BrowseJavaInput(object? sender, EventArgs e)
    {
        using var menu = new ContextMenuStrip();
        menu.Items.Add("Select Folder", null, (_, _) => BrowseJavaFolder());
        menu.Items.Add("Select Zip", null, (_, _) => BrowseJavaZip());
        menu.Show(Cursor.Position);
    }

    private void BrowseJavaFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Java world folder",
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _javaInputTextBox.Text = dialog.SelectedPath;
        AutoFillJavaOutput();
    }

    private void BrowseJavaZip()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Zip archives (*.zip)|*.zip",
            Title = "Select Java world zip",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _javaInputTextBox.Text = dialog.FileName;
        AutoFillJavaOutput();
    }

    private void AutoFillJavaOutput()
    {
        if (!string.IsNullOrWhiteSpace(_javaOutputTextBox.Text))
            return;

        string name = Directory.Exists(_javaInputTextBox.Text)
            ? Path.GetFileName(_javaInputTextBox.Text)
            : Path.GetFileNameWithoutExtension(_javaInputTextBox.Text);

        if (string.IsNullOrWhiteSpace(name))
            name = "ConvertedWorld";

        _javaOutputTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
    }

    private void BrowseJavaOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose output folder for saveData.ms",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_javaOutputTextBox.Text) ? _javaOutputTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _javaOutputTextBox.Text = dialog.SelectedPath;
    }

    private void BrowseLceInput(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "LCE save files (saveData.ms)|saveData.ms|All files (*.*)|*.*",
            Title = "Select saveData.ms",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _lceInputTextBox.Text = dialog.FileName;

        if (string.IsNullOrWhiteSpace(_lceOutputTextBox.Text))
        {
            string worldName = Path.GetFileNameWithoutExtension(dialog.FileName);
            if (string.IsNullOrWhiteSpace(worldName))
                worldName = "JavaWorld";
            _lceOutputTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), worldName + "-java");
        }
    }

    private void BrowseLceOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose output folder for Java world files",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_lceOutputTextBox.Text) ? _lceOutputTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _lceOutputTextBox.Text = dialog.SelectedPath;
    }

    private async void ConvertJavaToLceClicked(object? sender, EventArgs e)
    {
        if (!ValidateJavaInputs())
            return;

        ConversionOptions options = BuildJavaToLceOptions();
        await RunConversionAsync(options);
    }

    private async void ConvertLceToJavaClicked(object? sender, EventArgs e)
    {
        if (!ValidateLceInputs())
            return;

        ConversionOptions options = new ConversionOptions
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = _lceInputTextBox.Text,
            OutputDirectory = _lceOutputTextBox.Text,
            XzSize = 54,
            SizeLabel = "Classic",
            FlatWorld = false,
            ConvertAllDimensions = _lceAllDimensionsCheckBox.Checked,
            CopyPlayers = _lceCopyPlayersCheckBox.Checked,
            PreserveEntities = false,
        };

        await RunConversionAsync(options);
    }

    private ConversionOptions BuildJavaToLceOptions()
    {
        string worldType = (_javaWorldTypeComboBox.SelectedItem as string) ?? "classic";
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
            Direction = ConversionDirection.JavaToLce,
            InputPath = _javaInputTextBox.Text,
            OutputDirectory = _javaOutputTextBox.Text,
            XzSize = xzSize,
            SizeLabel = sizeLabel,
            FlatWorld = flatWorld,
            ConvertAllDimensions = _javaAllDimensionsCheckBox.Checked,
            CopyPlayers = _javaCopyPlayersCheckBox.Checked,
            PreserveEntities = _javaPreserveEntitiesCheckBox.Checked,
        };
    }

    private async Task RunConversionAsync(ConversionOptions options)
    {
        ToggleBusy(true, "Converting...");
        _logTextBox.Clear();

        try
        {
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
            MessageBox.Show(this, ex.Message, "Conversion Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleBusy(false, "Ready");
        }
    }

    private bool ValidateJavaInputs()
    {
        if (string.IsNullOrWhiteSpace(_javaInputTextBox.Text))
        {
            MessageBox.Show(this, "Select a Java world folder or zip first.", "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        bool isFolder = Directory.Exists(_javaInputTextBox.Text);
        bool isZip = File.Exists(_javaInputTextBox.Text) && string.Equals(Path.GetExtension(_javaInputTextBox.Text), ".zip", StringComparison.OrdinalIgnoreCase);
        if (!isFolder && !isZip)
        {
            MessageBox.Show(this, "Input must be an existing world folder or .zip file.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_javaOutputTextBox.Text))
        {
            MessageBox.Show(this, "Select an output folder.", "Missing Output", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Directory.CreateDirectory(_javaOutputTextBox.Text);
        return true;
    }

    private bool ValidateLceInputs()
    {
        if (string.IsNullOrWhiteSpace(_lceInputTextBox.Text) || !File.Exists(_lceInputTextBox.Text))
        {
            MessageBox.Show(this, "Select a valid saveData.ms file.", "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_lceOutputTextBox.Text))
        {
            MessageBox.Show(this, "Select an output folder.", "Missing Output", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Directory.CreateDirectory(_lceOutputTextBox.Text);
        return true;
    }

    private void ToggleBusy(bool busy, string status)
    {
        _javaConvertButton.Enabled = !busy;
        _lceConvertButton.Enabled = !busy;
        _statusLabel.Text = status;

        Color disabledColor = Color.FromArgb(145, 150, 148);
        _javaConvertButton.BackColor = _javaConvertButton.Enabled ? Accent : disabledColor;
        _lceConvertButton.BackColor = _lceConvertButton.Enabled ? Accent : disabledColor;
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
