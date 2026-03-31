using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class JavaLevelDatHelperTests
{
    [Fact]
    public void ReadSpawn_UsesLegacySpawnFieldsWhenPresent()
    {
        NbtCompound levelDat = new(string.Empty)
        {
            new NbtCompound("Data")
            {
                new NbtInt("SpawnX", 120),
                new NbtInt("SpawnY", 70),
                new NbtInt("SpawnZ", -45),
            },
        };

        JavaLevelDatHelper.SpawnPoint spawn = JavaLevelDatHelper.ReadSpawn(levelDat);

        Assert.Equal(120, spawn.X);
        Assert.Equal(70, spawn.Y);
        Assert.Equal(-45, spawn.Z);
    }

    [Fact]
    public void ReadSpawn_UsesModernSpawnCompoundWhenLegacyFieldsAreMissing()
    {
        NbtCompound levelDat = new(string.Empty)
        {
            new NbtCompound("Data")
            {
                new NbtCompound("spawn")
                {
                    new NbtIntArray("pos", [34, 72, 201]),
                    new NbtString("dimension", "minecraft:overworld"),
                },
            },
        };

        JavaLevelDatHelper.SpawnPoint spawn = JavaLevelDatHelper.ReadSpawn(levelDat);

        Assert.Equal(34, spawn.X);
        Assert.Equal(72, spawn.Y);
        Assert.Equal(201, spawn.Z);
    }
}
