using LceWorldConverter.Gui;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void AutoFillJavaOutput_UsesSharedDefaultDirectoryRule()
    {
        var viewModel = new MainWindowViewModel
        {
            JavaInputPath = @"C:\Worlds\Milky.zip",
        };

        viewModel.AutoFillJavaOutput(@"D:\Desktop");

        Assert.Equal(Path.Combine(@"D:\Desktop", "Milky"), viewModel.JavaOutputPath);
    }

    [Fact]
    public void ReviewSummary_ReflectsLceToJavaState()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedDirection = ConversionDirection.LceToJava,
            LceInputPath = @"C:\Worlds\saveData.ms",
            LceOutputPath = @"D:\Recovered",
            LceTargetVersion = "1.21.11",
            LceAllDimensions = true,
            LceCopyPlayers = true,
        };

        string summary = viewModel.ReviewSummary;

        Assert.Contains("Direction: LCE -> Java", summary, StringComparison.Ordinal);
        Assert.Contains("Target version: 1.21.11", summary, StringComparison.Ordinal);
        Assert.Contains("Export Nether and End", summary, StringComparison.Ordinal);
        Assert.Contains("Export players/*.dat", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildCurrentRequest_UsesSharedValidation()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedDirection = ConversionDirection.JavaToLce,
            JavaInputPath = @"C:\MissingWorld",
            JavaOutputPath = @"D:\Converted",
            JavaWorldType = "flat-medium",
        };

        bool ok = viewModel.TryBuildCurrentRequest(out ConversionRequest? request, out string title, out string message);

        Assert.False(ok);
        Assert.Null(request);
        Assert.Equal("Invalid Input", title);
        Assert.Contains("existing world folder or .zip archive", message, StringComparison.Ordinal);
    }
}
