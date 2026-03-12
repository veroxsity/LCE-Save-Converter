using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Converts Java chunk data to pre-v8 LCE chunk NBT.
/// This intentionally targets the old NBT chunk path so the game upgrades chunk storage itself.
/// </summary>
public static class ChunkConverter
{
    public const int CHUNK_BLOCKS = 32768;
    public const int CHUNK_NIBBLES = 16384;
    public const int HEIGHTMAP_SIZE = 256;
    public const int BIOMES_SIZE = 256;

    public static byte[] ConvertChunk(NbtCompound rootTag, int newChunkX, int newChunkZ)
    {
        var sourceLevel = rootTag.Get<NbtCompound>("Level");
        if (sourceLevel == null) return Array.Empty<byte>();

        bool isAnvil = sourceLevel.Contains("Sections");

        byte[] blocks;
        byte[] data;
        byte[] skyLight;
        byte[] blockLight;
        if (isAnvil)
        {
            FlattenAnvilSections(sourceLevel, out blocks, out data, out skyLight, out blockLight);
        }
        else
        {
            blocks = GetByteArrayOrDefault(sourceLevel, "Blocks", CHUNK_BLOCKS);
            data = GetByteArrayOrDefault(sourceLevel, "Data", CHUNK_NIBBLES);
            skyLight = GetByteArrayOrDefault(sourceLevel, "SkyLight", CHUNK_NIBBLES);
            blockLight = GetByteArrayOrDefault(sourceLevel, "BlockLight", CHUNK_NIBBLES);
        }

        byte[] heightMap = GetByteArrayOrDefault(sourceLevel, "HeightMap", HEIGHTMAP_SIZE);
        byte[] biomes = GetByteArrayOrDefault(sourceLevel, "Biomes", BIOMES_SIZE);

        long lastUpdate = sourceLevel.Get<NbtLong>("LastUpdate")?.Value ?? 0;
        long inhabitedTime = sourceLevel.Get<NbtLong>("InhabitedTime")?.Value ?? 0;

        // Build the root + Level compound expected by NbtIo::read in old chunk path.
        var level = new NbtCompound("Level")
        {
            new NbtInt("xPos", newChunkX),
            new NbtInt("zPos", newChunkZ),
            new NbtLong("LastUpdate", lastUpdate),
            new NbtLong("InhabitedTime", inhabitedTime),
            new NbtByteArray("Blocks", blocks),
            new NbtByteArray("Data", data),
            new NbtByteArray("SkyLight", skyLight),
            new NbtByteArray("BlockLight", blockLight),
            new NbtByteArray("HeightMap", heightMap),
            new NbtShort("TerrainPopulatedFlags", 0x3F),
            new NbtByteArray("Biomes", biomes),
        };

        // Compute the block-space offset applied to this chunk during world recentring.
        int sourceChunkX = sourceLevel.Get<NbtInt>("xPos")?.Value ?? newChunkX;
        int sourceChunkZ = sourceLevel.Get<NbtInt>("zPos")?.Value ?? newChunkZ;
        int blockOffsetX = (sourceChunkX - newChunkX) * 16;
        int blockOffsetZ = (sourceChunkZ - newChunkZ) * 16;

        var entities = (NbtList)CloneOrEmptyList(sourceLevel, "Entities");
        RemapEntityPositions(entities, blockOffsetX, blockOffsetZ);
        level.Add(entities);

        var tileEntities = (NbtList)CloneOrEmptyList(sourceLevel, "TileEntities");
        RemapTileEntityPositions(tileEntities, blockOffsetX, blockOffsetZ);
        level.Add(tileEntities);

        if (sourceLevel.Contains("TileTicks"))
        {
            var tileTicks = (NbtList)sourceLevel["TileTicks"]!.Clone();
            RemapTileTickPositions(tileTicks, blockOffsetX, blockOffsetZ);
            level.Add(tileTicks);
        }

        var root = new NbtCompound("") { level };
        var file = new NbtFile(root);

        using var ms = new MemoryStream();
        file.SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }

    private static NbtTag CloneOrEmptyList(NbtCompound sourceLevel, string name)
    {
        if (sourceLevel.Contains(name))
            return (NbtTag)sourceLevel[name]!.Clone();
        return new NbtList(name, NbtTagType.Compound);
    }

    private static void FlattenAnvilSections(
        NbtCompound level,
        out byte[] blocks,
        out byte[] data,
        out byte[] skyLight,
        out byte[] blockLight)
    {
        blocks = new byte[CHUNK_BLOCKS];
        data = new byte[CHUNK_NIBBLES];
        skyLight = new byte[CHUNK_NIBBLES];
        blockLight = new byte[CHUNK_NIBBLES];
        Array.Fill(skyLight, (byte)0xFF);

        var sections = level.Get<NbtList>("Sections");
        if (sections == null) return;

        foreach (NbtTag sectionTag in sections)
        {
            if (sectionTag is not NbtCompound section) continue;

            int sectionY = section.Get<NbtByte>("Y")?.Value ?? -1;
            if (sectionY < 0 || sectionY > 7) continue; // LCE chunk height is 128

            byte[]? sBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
            if (sBlocks == null || sBlocks.Length < 4096) continue;

            byte[]? sData = section.Get<NbtByteArray>("Data")?.Value;
            byte[]? sSky = section.Get<NbtByteArray>("SkyLight")?.Value;
            byte[]? sBlock = section.Get<NbtByteArray>("BlockLight")?.Value;

            int baseY = sectionY * 16;

            // Anvil section order: x + z*16 + y*256. Old chunk order: (x*16 + z)*128 + y.
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 0x0F;
                int z = (i >> 4) & 0x0F;
                int y = (i >> 8) & 0x0F;
                int globalY = baseY + y;
                int flatIndex = ((x * 16) + z) * 128 + globalY;

                blocks[flatIndex] = sBlocks[i];

                if (sData != null) SetNibble(data, flatIndex, GetNibble(sData, i));
                if (sSky != null) SetNibble(skyLight, flatIndex, GetNibble(sSky, i));
                if (sBlock != null) SetNibble(blockLight, flatIndex, GetNibble(sBlock, i));
            }
        }
    }

    private static int GetNibble(byte[] arr, int index)
    {
        int b = arr[index >> 1];
        return (index & 1) == 0 ? (b & 0x0F) : ((b >> 4) & 0x0F);
    }

    private static void SetNibble(byte[] arr, int index, int value)
    {
        int i = index >> 1;
        value &= 0x0F;
        if ((index & 1) == 0)
            arr[i] = (byte)((arr[i] & 0xF0) | value);
        else
            arr[i] = (byte)((arr[i] & 0x0F) | (value << 4));
    }

    private static byte[] GetByteArrayOrDefault(NbtCompound tag, string name, int defaultSize)
    {
        if (!tag.Contains(name))
            return new byte[defaultSize];

        var nbtTag = tag[name]!;
        if (nbtTag is NbtByteArray bytes)
        {
            if (bytes.Value.Length >= defaultSize)
                return bytes.Value[..defaultSize];

            byte[] padded = new byte[defaultSize];
            Buffer.BlockCopy(bytes.Value, 0, padded, 0, bytes.Value.Length);
            return padded;
        }

        if (nbtTag is NbtIntArray ints)
        {
            byte[] result = new byte[defaultSize];
            int len = Math.Min(ints.Value.Length, defaultSize);
            for (int i = 0; i < len; i++)
                result[i] = (byte)Math.Clamp(ints.Value[i], 0, 255);
            return result;
        }

        return new byte[defaultSize];
    }

    // Remap entity positions: subtract block offset from the Pos double list [x, y, z].
    private static void RemapEntityPositions(NbtList entities, int blockOffsetX, int blockOffsetZ)
    {
        if (blockOffsetX == 0 && blockOffsetZ == 0) return;
        foreach (NbtTag tag in entities)
        {
            if (tag is not NbtCompound entity) continue;
            var pos = entity.Get<NbtList>("Pos");
            if (pos != null && pos.Count >= 3)
            {
                ((NbtDouble)pos[0]).Value -= blockOffsetX;
                ((NbtDouble)pos[2]).Value -= blockOffsetZ;
            }

            // Remap riding/passengers recursively
            var riding = entity.Get<NbtCompound>("Riding");
            if (riding != null)
            {
                var inner = new NbtList("tmp", NbtTagType.Compound) { (NbtCompound)riding.Clone() };
                RemapEntityPositions(inner, blockOffsetX, blockOffsetZ);
            }
        }
    }

    // Remap tile entity positions: subtract block offset from integer x/z fields.
    private static void RemapTileEntityPositions(NbtList tileEntities, int blockOffsetX, int blockOffsetZ)
    {
        if (blockOffsetX == 0 && blockOffsetZ == 0) return;
        foreach (NbtTag tag in tileEntities)
        {
            if (tag is not NbtCompound te) continue;
            var xTag = te.Get<NbtInt>("x");
            var zTag = te.Get<NbtInt>("z");
            if (xTag != null) xTag.Value -= blockOffsetX;
            if (zTag != null) zTag.Value -= blockOffsetZ;
        }
    }

    // Remap tile tick positions: subtract block offset from integer x/z fields.
    private static void RemapTileTickPositions(NbtList tileTicks, int blockOffsetX, int blockOffsetZ)
    {
        if (blockOffsetX == 0 && blockOffsetZ == 0) return;
        foreach (NbtTag tag in tileTicks)
        {
            if (tag is not NbtCompound tick) continue;
            var xTag = tick.Get<NbtInt>("x");
            var zTag = tick.Get<NbtInt>("z");
            if (xTag != null) xTag.Value -= blockOffsetX;
            if (zTag != null) zTag.Value -= blockOffsetZ;
        }
    }
}
