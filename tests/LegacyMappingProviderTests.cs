using Xunit;

namespace LceWorldConverter.Tests;

public sealed class LegacyMappingProviderTests
{
    [Fact]
    public void GetModernItemName_UsesBundledMappings()
    {
        var provider = new LegacyMappingProvider();

        string modernName = provider.GetModernItemName(1);

        Assert.False(string.IsNullOrWhiteSpace(modernName));
        Assert.StartsWith("minecraft:", modernName, StringComparison.Ordinal);
    }

    [Fact]
    public void GetModernBlockName_FallsBackToMetaZeroMapping()
    {
        var provider = new LegacyMappingProvider();

        string modernName = provider.GetModernBlockName(1, 5);

        Assert.False(string.IsNullOrWhiteSpace(modernName));
        Assert.StartsWith("minecraft:", modernName, StringComparison.Ordinal);
    }
}
