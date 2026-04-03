using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class LevelDatConverterTests
{
    [Fact]
    public void ConvertLceToJava_WritesStable1122VersionMetadata()
    {
        NbtCompound lceRoot = new(string.Empty)
        {
            new NbtCompound("Data")
            {
                new NbtLong("RandomSeed", 123456789L),
                new NbtInt("SpawnX", 0),
                new NbtInt("SpawnY", 64),
                new NbtInt("SpawnZ", 0),
                new NbtInt("version", 19132),
            },
        };

        byte[] converted = LevelDatConverter.ConvertLceToJava(lceRoot);

        var file = new NbtFile();
        file.LoadFromBuffer(converted, 0, converted.Length, NbtCompression.AutoDetect);

        NbtCompound data = file.RootTag.Get<NbtCompound>("Data")!;
        Assert.Equal(19133, data.Get<NbtInt>("version")?.Value);
        Assert.Equal(1343, data.Get<NbtInt>("DataVersion")?.Value);

        NbtCompound? versionTag = data.Get<NbtCompound>("Version");
        Assert.NotNull(versionTag);
        Assert.Equal(1343, versionTag!.Get<NbtInt>("Id")?.Value);
        Assert.Equal("1.12.2", versionTag.Get<NbtString>("Name")?.Value);
        Assert.Equal((byte)0, versionTag.Get<NbtByte>("Snapshot")?.Value);
    }
}
