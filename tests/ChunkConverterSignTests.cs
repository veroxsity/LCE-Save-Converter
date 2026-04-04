using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class ChunkConverterSignTests
{
    [Fact]
    public void ConvertChunk_LegacySignTileEntity_IsSanitizedAndReducedToSafeFields()
    {
        var context = new ChunkConversionContext(preserveDynamicChunkData: false);

        var tileEntities = new NbtList("TileEntities", NbtTagType.Compound);
        tileEntities.Add(UnnamedCompound(
            new NbtString("id", "Sign"),
            new NbtInt("x", 10),
            new NbtInt("y", 64),
            new NbtInt("z", -2),
            new NbtString("Text1", "Hello\r\nWorld"),
            new NbtString("Text2", "12345678901234567890"),
            new NbtString("Text3", "\u0001Bad\tText"),
            new NbtString("Text4", string.Empty),
            new NbtString("ExtraTag", "ShouldNotSurvive")));

        var root = new NbtCompound(string.Empty)
        {
            new NbtCompound("Level")
            {
                new NbtInt("xPos", 0),
                new NbtInt("zPos", 0),
                tileEntities,
            }
        };

        byte[] chunkBytes = ChunkConverter.ConvertChunk(root, 3, 4, context);
        NbtCompound level = LoadLevel(chunkBytes);
        var converted = level.Get<NbtList>("TileEntities");

        Assert.NotNull(converted);
        Assert.Single(converted!);

        var sign = Assert.IsType<NbtCompound>(converted[0]);
        Assert.Equal("Sign", sign.Get<NbtString>("id")!.Value);
        Assert.Equal(58, sign.Get<NbtInt>("x")!.Value);
        Assert.Equal(64, sign.Get<NbtInt>("y")!.Value);
        Assert.Equal(62, sign.Get<NbtInt>("z")!.Value);
        Assert.Equal("HelloWorld", sign.Get<NbtString>("Text1")!.Value);
        Assert.Equal("123456789012345", sign.Get<NbtString>("Text2")!.Value);
        Assert.Equal("BadText", sign.Get<NbtString>("Text3")!.Value);
        Assert.Equal(string.Empty, sign.Get<NbtString>("Text4")!.Value);
        Assert.False(sign.Contains("ExtraTag"));
    }

    [Fact]
    public void ConvertChunk_ModernSignTileEntity_ExtractsFrontTextAsSafeLegacySign()
    {
        var context = new ChunkConversionContext(preserveDynamicChunkData: false);

        var messages = new NbtList("messages", NbtTagType.String)
        {
            UnnamedString("{\"text\":\"Hello\"}"),
            UnnamedString("[{\"text\":\"Big\"},{\"text\":\" Tree\"}]"),
            UnnamedString("Line\tThree"),
            UnnamedString("12345678901234567890"),
        };

        var blockEntities = new NbtList("block_entities", NbtTagType.Compound);
        blockEntities.Add(UnnamedCompound(
            new NbtString("id", "minecraft:oak_sign"),
            new NbtInt("x", 22),
            new NbtInt("y", 70),
            new NbtInt("z", 5),
            new NbtCompound("front_text")
            {
                messages,
            },
            new NbtCompound("back_text")
            {
                new NbtList("messages", NbtTagType.String),
            }));

        var root = new NbtCompound(string.Empty)
        {
            new NbtCompound("Level")
            {
                new NbtInt("xPos", 0),
                new NbtInt("zPos", 0),
                blockEntities,
                new NbtList("Sections", NbtTagType.Compound),
            }
        };

        byte[] chunkBytes = ChunkConverter.ConvertChunk(root, 0, 0, context);
        NbtCompound level = LoadLevel(chunkBytes);
        var converted = level.Get<NbtList>("TileEntities");

        Assert.NotNull(converted);
        Assert.Single(converted!);

        var sign = Assert.IsType<NbtCompound>(converted[0]);
        Assert.Equal("Sign", sign.Get<NbtString>("id")!.Value);
        Assert.Equal(22, sign.Get<NbtInt>("x")!.Value);
        Assert.Equal(70, sign.Get<NbtInt>("y")!.Value);
        Assert.Equal(5, sign.Get<NbtInt>("z")!.Value);
        Assert.Equal("Hello", sign.Get<NbtString>("Text1")!.Value);
        Assert.Equal("Big Tree", sign.Get<NbtString>("Text2")!.Value);
        Assert.Equal("LineThree", sign.Get<NbtString>("Text3")!.Value);
        Assert.Equal("123456789012345", sign.Get<NbtString>("Text4")!.Value);
    }

    [Fact]
    public void ConvertChunk_SafeSignTileEntity_IsRecenteredWithChunkShift()
    {
        var context = new ChunkConversionContext(preserveDynamicChunkData: false);

        var tileEntities = new NbtList("TileEntities", NbtTagType.Compound);
        tileEntities.Add(UnnamedCompound(
            new NbtString("id", "Sign"),
            new NbtInt("x", 10),
            new NbtInt("y", 64),
            new NbtInt("z", -2),
            new NbtString("Text1", "Hello")));

        var root = new NbtCompound(string.Empty)
        {
            new NbtCompound("Level")
            {
                new NbtInt("xPos", 0),
                new NbtInt("zPos", 0),
                tileEntities,
            }
        };

        byte[] chunkBytes = ChunkConverter.ConvertChunk(root, 3, 4, context);
        NbtCompound level = LoadLevel(chunkBytes);
        var converted = level.Get<NbtList>("TileEntities");

        var sign = Assert.IsType<NbtCompound>(Assert.Single(converted!));
        Assert.Equal(58, sign.Get<NbtInt>("x")!.Value);
        Assert.Equal(62, sign.Get<NbtInt>("z")!.Value);
    }

    private static NbtCompound LoadLevel(byte[] chunkBytes)
    {
        var file = new NbtFile();
        file.LoadFromBuffer(chunkBytes, 0, chunkBytes.Length, NbtCompression.None);
        return file.RootTag.Get<NbtCompound>("Level")!;
    }

    private static NbtCompound UnnamedCompound(params NbtTag[] tags)
    {
        var compound = new NbtCompound(string.Empty);
        compound.Name = null;
        foreach (NbtTag tag in tags)
            compound.Add(tag);
        return compound;
    }

    private static NbtString UnnamedString(string value)
    {
        var tag = new NbtString(string.Empty, value);
        tag.Name = null;
        return tag;
    }
}
