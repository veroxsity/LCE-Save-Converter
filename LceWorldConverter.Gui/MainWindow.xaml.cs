using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using LceWorldConverter;

namespace LceWorldConverter.Gui;

public partial class MainWindow : Window
{
    private readonly string[] _stepTitles = ["Choose Direction", "Configure Options", "Review And Convert"];
    private readonly string[] _stepSubtitles =
    [
        "Choose what kind of conversion you want to run.",
        "Set paths and options for the selected conversion direction.",
        "Check the summary, then run the conversion and watch the live log."
    ];

    private int _currentStep;
    private ConversionDirection _selectedDirection = ConversionDirection.JavaToLce;

    private TextBox JavaInputTextBox = null!;
    private TextBox JavaOutputTextBox = null!;
    private ComboBox JavaWorldTypeComboBox = null!;
    private CheckBox JavaAllDimensionsCheckBox = null!;
    private CheckBox JavaCopyPlayersCheckBox = null!;
    private CheckBox JavaPreserveEntitiesCheckBox = null!;
    private TextBox LceInputTextBox = null!;
    private TextBox LceOutputTextBox = null!;
    private ComboBox LceTargetVersionComboBox = null!;
    private CheckBox LceAllDimensionsCheckBox = null!;
    private CheckBox LceCopyPlayersCheckBox = null!;

    public MainWindow()
    {
        InitializeComponent();
        BuildStep1Content();
        PopulateWorldTypes();
        PopulateTargetVersions();
        ApplyDirectionState();
        GoToStep(0);
    }

    private void BuildStep1Content()
    {
        JavaInputTextBox = CreatePathTextBox(PathDropKind.JavaInput, JavaInputTextBox_Drop);
        JavaOutputTextBox = CreatePathTextBox(PathDropKind.Directory, JavaOutputTextBox_Drop);
        JavaWorldTypeComboBox = CreateWorldTypeComboBox();
        JavaAllDimensionsCheckBox = CreateCheckBox("Convert Nether and End");
        JavaCopyPlayersCheckBox = CreateCheckBox("Copy numeric player data");
        JavaPreserveEntitiesCheckBox = CreateCheckBox("Preserve entities and tile data");

        LceInputTextBox = CreatePathTextBox(PathDropKind.AnyFile, LceInputTextBox_Drop);
        LceOutputTextBox = CreatePathTextBox(PathDropKind.Directory, LceOutputTextBox_Drop);
        LceTargetVersionComboBox = CreateTargetVersionComboBox();
        LceAllDimensionsCheckBox = CreateCheckBox("Export Nether and End");
        LceCopyPlayersCheckBox = CreateCheckBox("Export players/*.dat");

        Step1Root.Children.Add(BuildJavaStepPanel());
        Step1Root.Children.Add(BuildLceStepPanel());
    }

    private void PopulateWorldTypes()
    {
        JavaWorldTypeComboBox.Items.Add("classic");
        JavaWorldTypeComboBox.Items.Add("small");
        JavaWorldTypeComboBox.Items.Add("medium");
        JavaWorldTypeComboBox.Items.Add("large");
        JavaWorldTypeComboBox.Items.Add("flat");
        JavaWorldTypeComboBox.Items.Add("flat-small");
        JavaWorldTypeComboBox.Items.Add("flat-medium");
        JavaWorldTypeComboBox.Items.Add("flat-large");
        JavaWorldTypeComboBox.SelectedItem = "classic";
    }

    private void PopulateTargetVersions()
    {
        LceTargetVersionComboBox.Items.Add("1.12.2");
        LceTargetVersionComboBox.Items.Add("1.13.2");
        LceTargetVersionComboBox.Items.Add("1.14.4");
        LceTargetVersionComboBox.Items.Add("1.15.2");
        LceTargetVersionComboBox.Items.Add("1.16.5");
        LceTargetVersionComboBox.Items.Add("1.17.1");
        LceTargetVersionComboBox.Items.Add("1.18.2");
        LceTargetVersionComboBox.Items.Add("1.19.4");
        LceTargetVersionComboBox.Items.Add("1.20.4");
        LceTargetVersionComboBox.Items.Add("1.21.4");
        LceTargetVersionComboBox.Items.Add("1.21.11");
        LceTargetVersionComboBox.SelectedItem = "1.21.11";
    }

    private void GoToStep(int step)
    {
        if (step < 0 || step > 2)
            return;

        _currentStep = step;
        Step0View.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1View.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2View.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepTitleText.Text = _stepTitles[step];
        StepSubtitleText.Text = _stepSubtitles[step];
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step == 1 ? "Review" : "Next";

        UpdateStepBadges(step);

        if (step == 2)
        {
            ConvertButton.Content = _selectedDirection == ConversionDirection.JavaToLce ? "Convert To LCE" : "Convert To Java";
            ReviewSummaryText.Text = BuildReviewSummary();
        }
    }

    private void UpdateStepBadges(int activeStep)
    {
        UpdateStepBadge(Step0Badge, activeStep >= 0);
        UpdateStepBadge(Step1Badge, activeStep >= 1);
        UpdateStepBadge(Step2Badge, activeStep >= 2);
    }

    private static void UpdateStepBadge(Border badge, bool active)
    {
        badge.Background = active
            ? new SolidColorBrush(Color.FromRgb(21, 116, 92))
            : new SolidColorBrush(Color.FromRgb(200, 212, 207));

        if (badge.Child is TextBlock text)
            text.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 53, 45));
    }

    private void ApplyDirectionState()
    {
        bool javaSelected = _selectedDirection == ConversionDirection.JavaToLce;

        Step1Root.Children[0].Visibility = javaSelected ? Visibility.Visible : Visibility.Collapsed;
        Step1Root.Children[1].Visibility = javaSelected ? Visibility.Collapsed : Visibility.Visible;

        ApplyDirectionCardStyle(JavaDirectionCard, javaSelected);
        ApplyDirectionCardStyle(LceDirectionCard, !javaSelected);
    }

    private static void ApplyDirectionCardStyle(Border card, bool selected)
    {
        card.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(21, 116, 92))
            : new SolidColorBrush(Color.FromRgb(214, 224, 218));
        card.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        card.Background = selected
            ? new SolidColorBrush(Color.FromRgb(248, 252, 250))
            : Brushes.White;
    }

    private string BuildReviewSummary()
    {
        if (_selectedDirection == ConversionDirection.JavaToLce)
        {
            string worldType = JavaWorldTypeComboBox.SelectedItem as string ?? "classic";
            string dimensions = JavaAllDimensionsCheckBox.IsChecked == true ? "Convert Overworld, Nether, and End" : "Convert Overworld only";
            string players = JavaCopyPlayersCheckBox.IsChecked == true ? "Copy numeric player data" : "Do not copy numeric player data";
            string entities = JavaPreserveEntitiesCheckBox.IsChecked == true ? "Preserve entities and tile data" : "Skip entities and tile data";

            return
                $"Direction: Java -> LCE{Environment.NewLine}" +
                $"World type: {worldType}{Environment.NewLine}" +
                $"Input: {FormatSummaryPath(JavaInputTextBox.Text)}{Environment.NewLine}" +
                $"Output folder: {FormatSummaryPath(JavaOutputTextBox.Text)}{Environment.NewLine}" +
                $"Dimensions: {dimensions}{Environment.NewLine}" +
                $"Players: {players}{Environment.NewLine}" +
                $"Entities: {entities}";
        }

        string lceDimensions = LceAllDimensionsCheckBox.IsChecked == true ? "Export Nether and End" : "Export Overworld only";
        string lcePlayers = LceCopyPlayersCheckBox.IsChecked == true ? "Export players/*.dat" : "Do not export players";
        string targetVersion = LceTargetVersionComboBox.Text ?? "1.12.2";

        return
            $"Direction: LCE -> Java{Environment.NewLine}" +
            $"Target version: {targetVersion}{Environment.NewLine}" +
            $"Input: {FormatSummaryPath(LceInputTextBox.Text)}{Environment.NewLine}" +
            $"Output folder: {FormatSummaryPath(LceOutputTextBox.Text)}{Environment.NewLine}" +
            $"Dimensions: {lceDimensions}{Environment.NewLine}" +
            $"Players: {lcePlayers}";
    }

    private static string FormatSummaryPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "Not set" : path;
    }

    private void JavaDirectionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _selectedDirection = ConversionDirection.JavaToLce;
        ApplyDirectionState();
    }

    private void LceDirectionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _selectedDirection = ConversionDirection.LceToJava;
        ApplyDirectionState();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(_currentStep - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1 && !ValidateCurrentStep())
            return;

        GoToStep(_currentStep + 1);
    }

    private bool ValidateCurrentStep()
    {
        return _selectedDirection == ConversionDirection.JavaToLce
            ? ValidateJavaInputs()
            : ValidateLceInputs();
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDirection == ConversionDirection.JavaToLce)
        {
            if (!ValidateJavaInputs())
                return;

            await RunConversionAsync(BuildJavaToLceOptions());
        }
        else
        {
            if (!ValidateLceInputs())
                return;

            await RunConversionAsync(BuildLceToJavaOptions());
        }
    }

    private ConversionOptions BuildJavaToLceOptions()
    {
        string worldType = JavaWorldTypeComboBox.SelectedItem as string ?? "classic";
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
            InputPath = JavaInputTextBox.Text,
            OutputDirectory = JavaOutputTextBox.Text,
            XzSize = xzSize,
            SizeLabel = sizeLabel,
            FlatWorld = flatWorld,
            ConvertAllDimensions = JavaAllDimensionsCheckBox.IsChecked == true,
            CopyPlayers = JavaCopyPlayersCheckBox.IsChecked == true,
            PreserveEntities = JavaPreserveEntitiesCheckBox.IsChecked == true,
        };
    }

    private ConversionOptions BuildLceToJavaOptions()
    {
        return new ConversionOptions
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = LceInputTextBox.Text,
            OutputDirectory = LceOutputTextBox.Text,
            TargetVersion = string.IsNullOrWhiteSpace(LceTargetVersionComboBox.Text) ? "1.12.2" : LceTargetVersionComboBox.Text,
            XzSize = 54,
            SizeLabel = "Classic",
            FlatWorld = false,
            ConvertAllDimensions = LceAllDimensionsCheckBox.IsChecked == true,
            CopyPlayers = LceCopyPlayersCheckBox.IsChecked == true,
            PreserveEntities = false,
        };
    }

    private async Task RunConversionAsync(ConversionOptions options)
    {
        ToggleBusy(true, "Converting...");
        LogTextBox.Clear();

        try
        {
            var logger = new UiConversionLogger(AppendLog);
            var service = new LceWorldConversionService();
            ConversionResult result = await Task.Run(() => service.Convert(options, logger));

            AppendLog(string.Empty);
            AppendLog($"Finished: {result.OutputPath}");

            MessageBox.Show(
                this,
                $"Conversion complete.{Environment.NewLine}{Environment.NewLine}Output: {result.OutputPath}",
                "LCE World Converter",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Conversion Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleBusy(false, "Ready");
        }
    }

    private bool ValidateJavaInputs()
    {
        if (string.IsNullOrWhiteSpace(JavaInputTextBox.Text))
        {
            ShowWarning("Missing Input", "Select a Java world folder or zip first.");
            return false;
        }

        bool isFolder = Directory.Exists(JavaInputTextBox.Text);
        bool isZip = File.Exists(JavaInputTextBox.Text) &&
            string.Equals(Path.GetExtension(JavaInputTextBox.Text), ".zip", StringComparison.OrdinalIgnoreCase);

        if (!isFolder && !isZip)
        {
            ShowWarning("Invalid Input", "Input must be an existing world folder or .zip file.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(JavaOutputTextBox.Text))
        {
            ShowWarning("Missing Output", "Select an output folder.");
            return false;
        }

        Directory.CreateDirectory(JavaOutputTextBox.Text);
        return true;
    }

    private bool ValidateLceInputs()
    {
        if (string.IsNullOrWhiteSpace(LceInputTextBox.Text) || !File.Exists(LceInputTextBox.Text))
        {
            ShowWarning("Missing Input", "Select a valid saveData.ms file.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(LceOutputTextBox.Text))
        {
            ShowWarning("Missing Output", "Select an output folder.");
            return false;
        }

        Directory.CreateDirectory(LceOutputTextBox.Text);
        return true;
    }

    private static void ShowWarning(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ToggleBusy(bool busy, string status)
    {
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        ConvertButton.IsEnabled = !busy;
        BackButton.IsEnabled = !busy;
        NextButton.IsEnabled = !busy;
        StatusText.Text = status;
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (LogTextBox.Text.Length > 0)
                LogTextBox.AppendText(Environment.NewLine);

            LogTextBox.AppendText(message);
            LogTextBox.ScrollToEnd();

            if (!string.IsNullOrWhiteSpace(message))
                StatusText.Text = message;
        });
    }

    private void BrowseJavaFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFolder("Select Java world folder", JavaInputTextBox.Text);
        if (path is null)
            return;

        JavaInputTextBox.Text = path;
        AutoFillJavaOutput();
    }

    private void BrowseJavaZipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Zip archives (*.zip)|*.zip",
            Title = "Select Java world zip",
        };

        if (dialog.ShowDialog() != true)
            return;

        JavaInputTextBox.Text = dialog.FileName;
        AutoFillJavaOutput();
    }

    private void BrowseJavaOutputButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFolder("Choose output folder for saveData.ms", JavaOutputTextBox.Text);
        if (path is not null)
            JavaOutputTextBox.Text = path;
    }

    private void BrowseLceInputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "LCE save files (saveData.ms)|saveData.ms|All files (*.*)|*.*",
            Title = "Select saveData.ms",
        };

        if (dialog.ShowDialog() != true)
            return;

        LceInputTextBox.Text = dialog.FileName;
        AutoFillLceOutput();
    }

    private void BrowseLceOutputButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFolder("Choose output folder for Java world files", LceOutputTextBox.Text);
        if (path is not null)
            LceOutputTextBox.Text = path;
    }

    private static string? BrowseForFolder(string title, string currentPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };

        if (Directory.Exists(currentPath))
            dialog.InitialDirectory = currentPath;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void AutoFillJavaOutput()
    {
        if (!string.IsNullOrWhiteSpace(JavaOutputTextBox.Text))
            return;

        string name = Directory.Exists(JavaInputTextBox.Text)
            ? Path.GetFileName(JavaInputTextBox.Text)
            : Path.GetFileNameWithoutExtension(JavaInputTextBox.Text);

        if (string.IsNullOrWhiteSpace(name))
            name = "ConvertedWorld";

        JavaOutputTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
    }

    private void AutoFillLceOutput()
    {
        if (!string.IsNullOrWhiteSpace(LceOutputTextBox.Text))
            return;

        string worldName = Path.GetFileNameWithoutExtension(LceInputTextBox.Text);
        if (string.IsNullOrWhiteSpace(worldName))
            worldName = "JavaWorld";

        LceOutputTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), worldName + "-java");
    }

    private void PathTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DirectoryTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && Directory.Exists(files[0]))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void JavaInputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string path = files[0];
        if (!Directory.Exists(path) && !(File.Exists(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)))
            return;

        JavaInputTextBox.Text = path;
        AutoFillJavaOutput();
    }

    private void LceInputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string path = files[0];
        if (!File.Exists(path))
            return;

        LceInputTextBox.Text = path;
        AutoFillLceOutput();
    }

    private void JavaOutputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && Directory.Exists(files[0]))
            JavaOutputTextBox.Text = files[0];
    }

    private void LceOutputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && Directory.Exists(files[0]))
            LceOutputTextBox.Text = files[0];
    }

    private StackPanel BuildJavaStepPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateCard(
            "Source And Output",
            "Choose a Java world source and the folder where the converted saveData.ms should be written.",
            CreatePathField("Java World", "Select a world folder or a .zip backup.", JavaInputTextBox, CreateButtonRow(
                CreateSecondaryButton("Folder", BrowseJavaFolderButton_Click),
                CreateSecondaryButton("Zip", BrowseJavaZipButton_Click))),
            CreatePathField("Output Folder", "Choose the folder that should receive the converted package.", JavaOutputTextBox, CreateButtonRow(
                CreateSecondaryButton("Choose Folder", BrowseJavaOutputButton_Click)))));

        panel.Children.Add(CreateCard(
            "Options",
            "Match the target legacy world type, then decide which extra data should be carried over.",
            CreateOptionRow("World Type", "Controls the target world bounds and whether the result is flat.", JavaWorldTypeComboBox),
            CreateWrapPanel(JavaAllDimensionsCheckBox, JavaCopyPlayersCheckBox, JavaPreserveEntitiesCheckBox)));
        return panel;
    }

    private StackPanel BuildLceStepPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateCard(
            "Source And Output",
            "Choose the source saveData.ms package and the folder where the Java world should be exported.",
            CreatePathField("saveData.ms", "Drop or browse for the legacy save package.", LceInputTextBox, CreateButtonRow(
                CreateSecondaryButton("Browse Save", BrowseLceInputButton_Click))),
            CreatePathField("Output Folder", "Choose the folder that should receive the Java world.", LceOutputTextBox, CreateButtonRow(
                CreateSecondaryButton("Choose Folder", BrowseLceOutputButton_Click)))));

        panel.Children.Add(CreateCard(
            "Options",
            "Choose your target Java Edition version and select whether to export additional dimensions and player data.",
            CreateOptionRow("Target Version", "The minimum Java Edition version that will run this world.", LceTargetVersionComboBox),
            CreateWrapPanel(LceAllDimensionsCheckBox, LceCopyPlayersCheckBox)));
        return panel;
    }

    private Border CreateCard(string title, string description, params UIElement[] content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 53, 45)),
            Margin = new Thickness(0, 0, 0, 6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(77, 96, 89)),
            Margin = new Thickness(0, 0, 0, 16),
        });

        foreach (UIElement element in content)
            stack.Children.Add(element);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 224, 218)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24, 20, 24, 20),
            Margin = new Thickness(0, 0, 0, 16),
            Child = stack,
        };
    }

    private FrameworkElement CreatePathField(string label, string hint, TextBox textBox, FrameworkElement buttons)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 53, 45)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 124, 118)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(textBox);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        stack.Children.Add(row);
        return stack;
    }

    private FrameworkElement CreateOptionRow(string label, string hint, FrameworkElement input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var copy = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        copy.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 53, 45)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        copy.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 124, 118)),
            TextWrapping = TextWrapping.Wrap,
        });

        grid.Children.Add(copy);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return grid;
    }

    private static WrapPanel CreateWrapPanel(params CheckBox[] checkBoxes)
    {
        var panel = new WrapPanel();
        foreach (CheckBox checkBox in checkBoxes)
            panel.Children.Add(checkBox);

        return panel;
    }

    private static StackPanel CreateButtonRow(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (Button button in buttons)
            panel.Children.Add(button);

        return panel;
    }

    private TextBox CreatePathTextBox(PathDropKind dropKind, DragEventHandler dropHandler)
    {
        var textBox = new TextBox
        {
            Height = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 12, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 224, 218)),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
        };

        textBox.PreviewDragOver += dropKind == PathDropKind.Directory ? DirectoryTextBox_PreviewDragOver : PathTextBox_PreviewDragOver;
        textBox.Drop += dropHandler;
        return textBox;
    }

    private static ComboBox CreateWorldTypeComboBox()
    {
        return new ComboBox
        {
            Width = 240,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    private static ComboBox CreateTargetVersionComboBox()
    {
        return new ComboBox
        {
            Width = 240,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsEditable = true,
        };
    }

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            Content = text,
            Margin = new Thickness(0, 0, 18, 10),
        };
    }

    private Button CreateSecondaryButton(string text, RoutedEventHandler clickHandler)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 53, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 224, 218)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 0, 12, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
        };
        button.Click += clickHandler;
        return button;
    }

    private enum PathDropKind
    {
        JavaInput,
        AnyFile,
        Directory,
    }

    private sealed class UiConversionLogger(Action<string> appendLog) : IConversionLogger
    {
        public void Info(string message) => appendLog(message);
        public void Error(string message) => appendLog(message);
    }
}
