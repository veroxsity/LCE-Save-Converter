using fNbt;
using LceWorldConverter;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class LceChunkPayloadCodecTests
{
    [Fact]
    public void EncodeCompressedStorage_RoundTripsToLegacyChunkNbt()
    {
        NbtCompound level = CreateLevel(12, -7);

        byte[] payload = LceChunkPayloadCodec.EncodeCompressedStorage(level);

        Assert.True(LceChunkPayloadCodec.TryDecodeToLegacyNbt(payload, out byte[] legacyNbt));

        var file = new NbtFile();
        file.LoadFromBuffer(legacyNbt, 0, legacyNbt.Length, NbtCompression.None);
        NbtCompound decoded = file.RootTag.Get<NbtCompound>("Level")!;

        Assert.Equal(12, decoded.Get<NbtInt>("xPos")!.Value);
        Assert.Equal(-7, decoded.Get<NbtInt>("zPos")!.Value);
        Assert.Equal(level.Get<NbtLong>("LastUpdate")!.Value, decoded.Get<NbtLong>("LastUpdate")!.Value);
        Assert.Equal(level.Get<NbtLong>("InhabitedTime")!.Value, decoded.Get<NbtLong>("InhabitedTime")!.Value);
        Assert.Equal(level.Get<NbtShort>("TerrainPopulatedFlags")!.Value, decoded.Get<NbtShort>("TerrainPopulatedFlags")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("Blocks")!.Value, decoded.Get<NbtByteArray>("Blocks")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("Data")!.Value, decoded.Get<NbtByteArray>("Data")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("SkyLight")!.Value, decoded.Get<NbtByteArray>("SkyLight")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("BlockLight")!.Value, decoded.Get<NbtByteArray>("BlockLight")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("HeightMap")!.Value, decoded.Get<NbtByteArray>("HeightMap")!.Value);
        Assert.Equal(level.Get<NbtByteArray>("Biomes")!.Value, decoded.Get<NbtByteArray>("Biomes")!.Value);
        Assert.Empty(decoded.Get<NbtList>("TileEntities")!);
        Assert.Empty(decoded.Get<NbtList>("Entities")!);
    }

    [Fact]
    public void ForceChunkCoordinates_PatchesCompressedStorageHeader()
    {
        NbtCompound level = CreateLevel(3, 4);
        byte[] payload = LceChunkPayloadCodec.EncodeCompressedStorage(level);

        byte[] patched = LceChunkPayloadCodec.ForceChunkCoordinates(payload, -2, 15);

        Assert.True(LceChunkPayloadCodec.TryReadChunkCoordinates(patched, out int chunkX, out int chunkZ, out _));
        Assert.Equal(-2, chunkX);
        Assert.Equal(15, chunkZ);
    }

    [Fact]
    public void EncodeCompressedStorage_WritesMinimalTrailingRootCompound()
    {
        NbtCompound level = CreateLevel(8, -3);

        byte[] payload = LceChunkPayloadCodec.EncodeCompressedStorage(level);

        Assert.True(LceChunkPayloadCodec.TryGetCompressedChunkNbtOffset(payload, out int nbtOffset));
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00, 0x00 }, payload[nbtOffset..]);
    }

    private static NbtCompound CreateLevel(int chunkX, int chunkZ)
    {
        byte[] blocks = new byte[ChunkConverter.CHUNK_BLOCKS];
        byte[] data = new byte[ChunkConverter.CHUNK_NIBBLES];
        byte[] skyLight = new byte[ChunkConverter.CHUNK_NIBBLES];
        byte[] blockLight = new byte[ChunkConverter.CHUNK_NIBBLES];
        byte[] heightMap = new byte[ChunkConverter.HEIGHTMAP_SIZE];
        byte[] biomes = new byte[ChunkConverter.BIOMES_SIZE];

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int columnIndex = (x * 16) + z;
                heightMap[columnIndex] = (byte)(60 + ((x + z) % 10));
                biomes[columnIndex] = (byte)(1 + ((x + z) % 5));

                for (int y = 0; y < 128; y++)
                {
                    int index = ((x * 16) + z) * 128 + y;
                    blocks[index] = (byte)((x + z + y) % 64);

                    int nibble = (x + (z * 3) + y) & 0x0F;
                    SetNibble(data, index, nibble);
                    SetNibble(skyLight, index, 15 - (y & 0x0F));
                    SetNibble(blockLight, index, (x + z) & 0x0F);
                }
            }
        }

        var tileEntities = new NbtList("TileEntities", NbtTagType.Compound);
        NbtCompound sign = new(string.Empty)
        {
            new NbtString("id", "Sign"),
            new NbtInt("x", 10),
            new NbtInt("y", 70),
            new NbtInt("z", -5),
            new NbtString("Text1", "Hello"),
            new NbtString("Text2", "World"),
            new NbtString("Text3", string.Empty),
            new NbtString("Text4", string.Empty),
        };
        sign.Name = null;
        tileEntities.Add(sign);

        return new NbtCompound("Level")
        {
            new NbtInt("xPos", chunkX),
            new NbtInt("zPos", chunkZ),
            new NbtLong("LastUpdate", 123456789L),
            new NbtLong("InhabitedTime", 987654321L),
            new NbtByteArray("Blocks", blocks),
            new NbtByteArray("Data", data),
            new NbtByteArray("SkyLight", skyLight),
            new NbtByteArray("BlockLight", blockLight),
            new NbtByteArray("HeightMap", heightMap),
            new NbtShort("TerrainPopulatedFlags", 2046),
            new NbtByteArray("Biomes", biomes),
            new NbtList("Entities", NbtTagType.Compound),
            tileEntities,
        };
    }

    private static void SetNibble(byte[] array, int index, int value)
    {
        int byteIndex = index >> 1;
        value &= 0x0F;

        if ((index & 1) == 0)
            array[byteIndex] = (byte)((array[byteIndex] & 0xF0) | value);
        else
            array[byteIndex] = (byte)((array[byteIndex] & 0x0F) | (value << 4));
    }
}
