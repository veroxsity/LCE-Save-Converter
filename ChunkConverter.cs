using System.Buffers.Binary;
using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Converts Java Edition chunk NBT to LCE post-version-8 binary chunk format.
/// 
/// The binary format (from OldChunkStorage::save to DataOutputStream):
///   short  saveVersion (9)
///   int    chunkX
///   int    chunkZ
///   long   gameTime
///   long   inhabitedTime
///   CompressedTileStorage  lowerBlocks (Y=0-63)
///   CompressedTileStorage  upperBlocks (Y=64-127)
///   CompressedTileStorage  lowerData
///   CompressedTileStorage  upperData
///   CompressedTileStorage  lowerSkyLight
///   CompressedTileStorage  upperSkyLight
///   CompressedTileStorage  lowerBlockLight
///   CompressedTileStorage  upperBlockLight
///   byte[256]  heightMap
///   short  terrainPopulated
///   byte[256]  biomes
///   NBT compound (entities + tileEntities + tileTicks)
/// 
/// All multi-byte values are big-endian (DataOutputStream format).
/// </summary>
public static class ChunkConverter
{
    public const int CHUNK_BLOCKS = 32768;
    public const int CHUNK_HALF = 16384;
    public const int HEIGHTMAP_SIZE = 256;
    public const int BIOMES_SIZE = 256;
    public const short SAVE_VERSION = 9;

    public static byte[] ConvertChunk(NbtCompound rootTag, int newChunkX, int newChunkZ)
    {
        var level = rootTag.Get<NbtCompound>("Level");
        if (level == null) return Array.Empty<byte>();

        bool isAnvil = level.Contains("Sections");

        byte[] blocks, data, skyLight, blockLight;
        if (isAnvil)
            FlattenAnvilSections(level, out blocks, out data, out skyLight, out blockLight);
        else
        {
            blocks = GetByteArrayOrDefault(level, "Blocks", CHUNK_BLOCKS);
            data = GetByteArrayOrDefault(level, "Data", CHUNK_HALF);
            skyLight = GetByteArrayOrDefault(level, "SkyLight", CHUNK_HALF);
            blockLight = GetByteArrayOrDefault(level, "BlockLight", CHUNK_HALF);
        }

        byte[] heightMap = GetByteArrayOrDefault(level, "HeightMap", HEIGHTMAP_SIZE);
        byte[] biomes = GetByteArrayOrDefault(level, "Biomes", BIOMES_SIZE);
        for (int i = 0; i < heightMap.Length; i++)
            if (heightMap[i] > 127) heightMap[i] = 127;

        long lastUpdate = level.Get<NbtLong>("LastUpdate")?.Value ?? 0;
        long inhabitedTime = level.Get<NbtLong>("InhabitedTime")?.Value ?? 0;

        // Build the binary chunk format
        using var ms = new MemoryStream();
        
        // Header: version, coords, time
        WriteBigEndianShort(ms, SAVE_VERSION);
        WriteBigEndianInt(ms, newChunkX);
        WriteBigEndianInt(ms, newChunkZ);
        WriteBigEndianLong(ms, lastUpdate);
        WriteBigEndianLong(ms, inhabitedTime);

        // Compressed tile storages (lower=Y0-63, upper=Y64-127)
        ms.Write(CompressedTileStorageWriter.WriteBlockStorage(blocks, 0));
        ms.Write(CompressedTileStorageWriter.WriteBlockStorage(blocks, 64));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(data, 0));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(data, 64));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(skyLight, 0));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(skyLight, 64));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(blockLight, 0));
        ms.Write(CompressedTileStorageWriter.WriteNibbleStorage(blockLight, 64));

        // Heightmap and terrain flags
        ms.Write(heightMap);
        WriteBigEndianShort(ms, 0x3F); // terrainPopulated all neighbours
        ms.Write(biomes);

        // NBT compound with entities, tile entities, tile ticks
        var nbtTag = new NbtCompound("");

        if (level.Contains("Entities"))
            nbtTag.Add((NbtTag)level["Entities"]!.Clone());
        else
            nbtTag.Add(new NbtList("Entities", NbtTagType.Compound));

        if (level.Contains("TileEntities"))
            nbtTag.Add((NbtTag)level["TileEntities"]!.Clone());
        else
            nbtTag.Add(new NbtList("TileEntities", NbtTagType.Compound));

        if (level.Contains("TileTicks"))
            nbtTag.Add((NbtTag)level["TileTicks"]!.Clone());

        // Write NBT uncompressed (NbtIo::write format = no compression)
        var nbtFile = new NbtFile(nbtTag);
        using var nbtMs = new MemoryStream();
        nbtFile.SaveToStream(nbtMs, NbtCompression.None);
        ms.Write(nbtMs.ToArray());

        return ms.ToArray();
    }

    #region Big-endian writers

    private static void WriteBigEndianShort(Stream s, short val)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buf, val);
        s.Write(buf);
    }

    private static void WriteBigEndianInt(Stream s, int val)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, val);
        s.Write(buf);
    }

    private static void WriteBigEndianLong(Stream s, long val)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buf, val);
        s.Write(buf);
    }

    #endregion

    #region Anvil flattening

    private static void FlattenAnvilSections(
        NbtCompound level,
        out byte[] blocks, out byte[] data,
        out byte[] skyLight, out byte[] blockLight)
    {
        blocks = new byte[CHUNK_BLOCKS];
        data = new byte[CHUNK_HALF];
        skyLight = new byte[CHUNK_HALF];
        blockLight = new byte[CHUNK_HALF];
        Array.Fill(skyLight, (byte)0xFF);

        var sections = level.Get<NbtList>("Sections");
        if (sections == null) return;

        foreach (NbtTag sectionTag in sections)
        {
            if (sectionTag is not NbtCompound section) continue;
            var yTag = section.Get<NbtByte>("Y");
            if (yTag == null) continue;
            int sectionY = yTag.Value;
            if (sectionY < 0 || sectionY > 7) continue;

            byte[]? sBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
            byte[]? sData = section.Get<NbtByteArray>("Data")?.Value;
            byte[]? sSkyLight = section.Get<NbtByteArray>("SkyLight")?.Value;
            byte[]? sBlockLight = section.Get<NbtByteArray>("BlockLight")?.Value;
            if (sBlocks == null) continue;

            int baseY = sectionY * 16;
            for (int i = 0; i < 4096; i++)
            {
                int ly = i / 256;
                int remainder = i % 256;
                int globalY = baseY + ly;
                blocks[globalY * 256 + remainder] = sBlocks[i];
            }

            if (sData != null) CopyNibbleSection(sData, data, baseY);
            if (sSkyLight != null) CopyNibbleSection(sSkyLight, skyLight, baseY);
            if (sBlockLight != null) CopyNibbleSection(sBlockLight, blockLight, baseY);
        }
    }

    private static void CopyNibbleSection(byte[] sectionNibbles, byte[] flatNibbles, int baseY)
    {
        for (int i = 0; i < 4096; i++)
        {
            int ly = i / 256;
            int remainder = i % 256;
            int globalY = baseY + ly;
            int flatBlockIndex = globalY * 256 + remainder;

            int sNibbleValue = (i & 1) == 0
                ? sectionNibbles[i / 2] & 0x0F
                : (sectionNibbles[i / 2] >> 4) & 0x0F;

            int fNibbleIndex = flatBlockIndex / 2;
            if ((flatBlockIndex & 1) == 0)
                flatNibbles[fNibbleIndex] = (byte)((flatNibbles[fNibbleIndex] & 0xF0) | sNibbleValue);
            else
                flatNibbles[fNibbleIndex] = (byte)((flatNibbles[fNibbleIndex] & 0x0F) | (sNibbleValue << 4));
        }
    }

    #endregion

    private static byte[] GetByteArrayOrDefault(NbtCompound tag, string name, int defaultSize)
    {
        if (!tag.Contains(name))
            return new byte[defaultSize];

        var nbtTag = tag[name]!;
        if (nbtTag is NbtByteArray byteArr)
        {
            if (byteArr.Value.Length >= defaultSize)
                return byteArr.Value[..defaultSize];
            byte[] padded = new byte[defaultSize];
            Buffer.BlockCopy(byteArr.Value, 0, padded, 0, byteArr.Value.Length);
            return padded;
        }
        if (nbtTag is NbtIntArray intArr)
        {
            byte[] result = new byte[defaultSize];
            int len = Math.Min(intArr.Value.Length, defaultSize);
            for (int i = 0; i < len; i++)
                result[i] = (byte)Math.Clamp(intArr.Value[i], 0, 255);
            return result;
        }
        return new byte[defaultSize];
    }
}
