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

    private readonly MainWindowViewModel _viewModel = new();
    private int _currentStep;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        ApplyDirectionState();
        GoToStep(0);
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
        bool javaSelected = _viewModel.SelectedDirection == ConversionDirection.JavaToLce;

        JavaStepPanel.Visibility = javaSelected ? Visibility.Visible : Visibility.Collapsed;
        LceStepPanel.Visibility = javaSelected ? Visibility.Collapsed : Visibility.Visible;

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

    private void JavaDirectionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectedDirection = ConversionDirection.JavaToLce;
        ApplyDirectionState();
    }

    private void LceDirectionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectedDirection = ConversionDirection.LceToJava;
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
        if (_viewModel.TryBuildCurrentRequest(out _, out string title, out string message))
            return true;

        ShowWarning(title, message);
        return false;
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildCurrentRequest(out ConversionRequest? request, out string title, out string message))
        {
            ShowWarning(title, message);
            return;
        }

        await RunConversionAsync(request!);
    }

    private async Task RunConversionAsync(ConversionRequest request)
    {
        ToggleBusy(true, "Converting...");
        LogTextBox.Clear();

        try
        {
            var logger = new UiConversionLogger(AppendLog);
            var service = new LceWorldConversionService();
            ConversionResult result = await Task.Run(() => service.Convert(request, logger));

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
        string? path = BrowseForFolder("Select Java world folder", _viewModel.JavaInputPath);
        if (path is null)
            return;

        _viewModel.JavaInputPath = path;
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

        _viewModel.JavaInputPath = dialog.FileName;
        AutoFillJavaOutput();
    }

    private void BrowseJavaOutputButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFolder("Choose output folder for saveData.ms", _viewModel.JavaOutputPath);
        if (path is not null)
            _viewModel.JavaOutputPath = path;
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

        _viewModel.LceInputPath = dialog.FileName;
        AutoFillLceOutput();
    }

    private void BrowseLceOutputButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFolder("Choose output folder for Java world files", _viewModel.LceOutputPath);
        if (path is not null)
            _viewModel.LceOutputPath = path;
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
        _viewModel.AutoFillJavaOutput(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    private void AutoFillLceOutput()
    {
        _viewModel.AutoFillLceOutput(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
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

        _viewModel.JavaInputPath = path;
        AutoFillJavaOutput();
    }

    private void LceInputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string path = files[0];
        if (!File.Exists(path))
            return;

        _viewModel.LceInputPath = path;
        AutoFillLceOutput();
    }

    private void JavaOutputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && Directory.Exists(files[0]))
            _viewModel.JavaOutputPath = files[0];
    }

    private void LceOutputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && Directory.Exists(files[0]))
            _viewModel.LceOutputPath = files[0];
    }
}
