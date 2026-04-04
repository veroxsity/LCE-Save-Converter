using LceWorldConverter;
using LceWorldConverter.Cli;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class CommandLineOptionsParserTests
{
    [Fact]
    public void TryParse_LegacySaveDataMsInput_AutoSelectsLceToJava()
    {
        string[] args =
        [
            @"C:\Worlds\MySlot\saveData.ms",
            @"C:\Converted\Milky"
        ];

        bool ok = CommandLineOptionsParser.TryParse(args, out ConversionRequest? request, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(request);
        Assert.Equal(ConversionDirection.LceToJava, request!.Direction);
        Assert.Equal(args[0], request.InputPath);
        Assert.Equal(args[1], request.OutputDirectory);
    }

    [Fact]
    public void TryParse_LegacyJavaInput_AutoSelectsJavaToLce()
    {
        string[] args =
        [
            @"C:\Users\You\saves\JavaWorld",
            @"C:\Converted\Slot"
        ];

        bool ok = CommandLineOptionsParser.TryParse(args, out ConversionRequest? request, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(request);
        Assert.Equal(ConversionDirection.JavaToLce, request!.Direction);
        Assert.Equal(args[0], request.InputPath);
        Assert.Equal(args[1], request.OutputDirectory);
    }
}
