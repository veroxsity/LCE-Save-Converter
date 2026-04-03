using LceWorldConverter;
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

        bool ok = CommandLineOptionsParser.TryParse(args, out ConversionOptions? options, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(options);
        Assert.Equal(ConversionDirection.LceToJava, options!.Direction);
        Assert.Equal(args[0], options.InputPath);
        Assert.Equal(args[1], options.OutputDirectory);
    }

    [Fact]
    public void TryParse_LegacyJavaInput_AutoSelectsJavaToLce()
    {
        string[] args =
        [
            @"C:\Users\You\saves\JavaWorld",
            @"C:\Converted\Slot"
        ];

        bool ok = CommandLineOptionsParser.TryParse(args, out ConversionOptions? options, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(options);
        Assert.Equal(ConversionDirection.JavaToLce, options!.Direction);
        Assert.Equal(args[0], options.InputPath);
        Assert.Equal(args[1], options.OutputDirectory);
    }
}
