using System.Reflection;
using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class ChunkConverterBlockMappingTests
{
    private static readonly MethodInfo MapModernBlockStateMethod = typeof(ChunkConverter)
        .GetMethod("MapModernBlockState", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ChunkConverter.MapModernBlockState was not found.");

    [Theory]
    [InlineData("minecraft:grass", 31, 1)]
    [InlineData("minecraft:short_grass", 31, 1)]
    [InlineData("minecraft:short_dry_grass", 31, 1)]
    [InlineData("minecraft:grass_block", 2, 0)]
    public void MapModernBlockState_MapsGrassFamilyToExpectedLegacyIds(string javaName, byte expectedId, byte expectedData)
    {
        var entry = new NbtCompound(string.Empty)
        {
            new NbtString("Name", javaName)
        };

        object? result = MapModernBlockStateMethod.Invoke(null, new object[] { entry });
        Assert.NotNull(result);

        Type stateType = result!.GetType();
        byte id = (byte)(stateType.GetProperty("Id")?.GetValue(result)
            ?? throw new InvalidOperationException("LegacyBlockState.Id not found."));
        byte data = (byte)(stateType.GetProperty("Data")?.GetValue(result)
            ?? throw new InvalidOperationException("LegacyBlockState.Data not found."));

        Assert.Equal(expectedId, id);
        Assert.Equal(expectedData, data);
    }
}
