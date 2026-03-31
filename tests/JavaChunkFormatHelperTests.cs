using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class JavaChunkFormatHelperTests
{
    [Fact]
    public void Inspect_ClassifiesLegacyAnvilChunkEvenWhenDataVersionIsModern()
    {
        NbtList sections = new("Sections", NbtTagType.Compound);
        sections.Add(CreateUnnamedCompound(
            new NbtByte("Y", 4),
            new NbtByteArray("Blocks", new byte[4096]),
            new NbtByteArray("Data", new byte[2048])));

        NbtCompound chunk = new(string.Empty)
        {
            new NbtInt("DataVersion", 4671),
            new NbtCompound("Level")
            {
                new NbtInt("xPos", 14),
                new NbtInt("zPos", 12),
                sections,
            },
        };

        JavaChunkFormatInfo info = JavaChunkFormatHelper.Inspect(chunk);

        Assert.Equal(JavaChunkFormat.LegacyAnvil, info.Format);
        Assert.False(info.UsesModernContentSchema);
        Assert.False(info.RequiresSectionShift);
    }

    [Fact]
    public void Inspect_ClassifiesExtendedHeightModernChunkUsingSignedSectionY()
    {
        NbtList sections = new("sections", NbtTagType.Compound);
        sections.Add(CreateUnnamedCompound(
            new NbtByte("Y", unchecked((byte)-4)),
            new NbtCompound("block_states")));
        sections.Add(CreateUnnamedCompound(
            new NbtByte("Y", 19),
            new NbtCompound("block_states")));

        NbtCompound chunk = new(string.Empty)
        {
            new NbtInt("DataVersion", 3953),
            sections,
            new NbtList("block_entities", NbtTagType.Compound),
        };

        JavaChunkFormatInfo info = JavaChunkFormatHelper.Inspect(chunk);

        Assert.Equal(JavaChunkFormat.ModernExtendedHeight, info.Format);
        Assert.True(info.UsesModernContentSchema);
        Assert.True(info.RequiresSectionShift);
        Assert.Equal(-4, info.MinSectionY);
        Assert.Equal(19, info.MaxSectionY);
    }

    [Fact]
    public void Inspect_ClassifiesLegacyBlockArrayChunk()
    {
        NbtCompound chunk = new(string.Empty)
        {
            new NbtInt("xPos", 1),
            new NbtInt("zPos", 2),
            new NbtByteArray("Blocks", new byte[32768]),
            new NbtByteArray("Data", new byte[16384]),
        };

        JavaChunkFormatInfo info = JavaChunkFormatHelper.Inspect(chunk);

        Assert.Equal(JavaChunkFormat.LegacyBlockArray, info.Format);
        Assert.False(info.IsSectionBased);
        Assert.False(info.UsesModernContentSchema);
    }

    private static NbtCompound CreateUnnamedCompound(params NbtTag[] tags)
    {
        NbtCompound compound = new(string.Empty);
        compound.Name = null;
        foreach (NbtTag tag in tags)
            compound.Add(tag);
        return compound;
    }
}
