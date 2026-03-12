using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Converts Java Edition chunk NBT to LCE-compatible chunk NBT.
/// 
/// Java McRegion (1.6.4 and earlier):
///   Chunks have flat Blocks[32768], Data[16384], SkyLight[16384], BlockLight[16384]
///   These are identical to LCE format — direct copy.
///
/// Java Anvil (1.7+):
///   Chunks use "Sections" list with 16-block-high sub-chunks (Y=0..15).
///   Each section has Blocks[4096], Data[2048], SkyLight[2048], BlockLight[2048].
///   We must flatten these back into the single flat arrays LCE expects.
///   LCE only has 128 height (Y=0-127) so sections 0-7 only.
///
/// LCE-specific additions:
///   - TerrainPopulatedFlags (short) instead of TerrainPopulated (byte)
///   - Biomes byte[256] (4J added this)
///   - InhabitedTime (added in save version 9 / 1.6.4)
/// </summary>
public static class ChunkConverter
{
    // LCE uses 128-block height: 16x128x16 = 32768 blocks
    public const int CHUNK_BLOCKS = 32768;    // 16 * 128 * 16
    public const int CHUNK_HALF = 16384;      // nibble arrays (data, skylight, blocklight)
    public const int HEIGHTMAP_SIZE = 256;    // 16 * 16
    public const int BIOMES_SIZE = 256;       // 16 * 16

    /// <summary>
    /// Convert a Java chunk NBT root tag into LCE-compatible NBT bytes.
    /// The returned bytes are uncompressed NBT ready to be written to an LCE region file
    /// (which will handle compression).
    /// 
    /// newChunkX/Z are the remapped LCE coordinates for this chunk.
    /// </summary>
    public static byte[] ConvertChunk(NbtCompound rootTag, int newChunkX, int newChunkZ)
    {
        var level = rootTag.Get<NbtCompound>("Level");
        if (level == null) return Array.Empty<byte>();

        // Determine if this is Anvil (has Sections) or McRegion (has flat Blocks)
        bool isAnvil = level.Contains("Sections");

        byte[] blocks;
        byte[] data;
        byte[] skyLight;
        byte[] blockLight;

        if (isAnvil)
        {
            FlattenAnvilSections(level, out blocks, out data, out skyLight, out blockLight);
        }
        else
        {
            // McRegion — direct copy
            blocks = GetByteArrayOrDefault(level, "Blocks", CHUNK_BLOCKS);
            data = GetByteArrayOrDefault(level, "Data", CHUNK_HALF);
            skyLight = GetByteArrayOrDefault(level, "SkyLight", CHUNK_HALF);
            blockLight = GetByteArrayOrDefault(level, "BlockLight", CHUNK_HALF);
        }

        byte[] heightMap = GetByteArrayOrDefault(level, "HeightMap", HEIGHTMAP_SIZE);
        byte[] biomes = GetByteArrayOrDefault(level, "Biomes", BIOMES_SIZE);

        // Clamp heightmap values to 0-127 for LCE's 128-block height
        for (int i = 0; i < heightMap.Length; i++)
        {
            if (heightMap[i] > 127) heightMap[i] = 127;
        }

        long lastUpdate = level.Get<NbtLong>("LastUpdate")?.Value ?? 0;
        long inhabitedTime = level.Get<NbtLong>("InhabitedTime")?.Value ?? 0;

        // Build LCE-compatible chunk NBT
        // Using the pre-version-8 NBT format for maximum compatibility
        var lceLevel = new NbtCompound("Level")
        {
            new NbtInt("xPos", newChunkX),
            new NbtInt("zPos", newChunkZ),
            new NbtByteArray("Blocks", blocks),
            new NbtByteArray("Data", data),
            new NbtByteArray("SkyLight", skyLight),
            new NbtByteArray("BlockLight", blockLight),
            new NbtByteArray("HeightMap", heightMap),
            new NbtByteArray("Biomes", biomes),
            new NbtLong("LastUpdate", lastUpdate),
            new NbtLong("InhabitedTime", inhabitedTime),
            new NbtShort("TerrainPopulatedFlags", 0x3F) // All neighbours populated
        };

        // Copy entities (with coordinate adjustment handled elsewhere)
        if (level.Contains("Entities"))
            lceLevel.Add((NbtTag)level["Entities"]!.Clone());
        else
            lceLevel.Add(new NbtList("Entities", NbtTagType.Compound));

        // Copy tile entities
        if (level.Contains("TileEntities"))
            lceLevel.Add((NbtTag)level["TileEntities"]!.Clone());
        else
            lceLevel.Add(new NbtList("TileEntities", NbtTagType.Compound));

        // Copy tile ticks if present
        if (level.Contains("TileTicks"))
            lceLevel.Add((NbtTag)level["TileTicks"]!.Clone());

        var root = new NbtCompound("") { lceLevel };

        // Serialize to bytes
        var nbtFile = new NbtFile(root);
        using var ms = new MemoryStream();
        nbtFile.SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }

    /// <summary>
    /// Flattens Anvil section-based chunk data into flat arrays matching McRegion/LCE format.
    /// Only processes sections Y=0 through Y=7 (block heights 0-127).
    /// 
    /// Anvil sections each contain:
    ///   Blocks: byte[4096]  (16x16x16, YZX order)
    ///   Data: byte[2048]    (nibble array)
    ///   SkyLight: byte[2048]
    ///   BlockLight: byte[2048]
    ///   Y: byte (section index 0-15)
    ///
    /// LCE flat format:
    ///   Blocks: byte[32768] (16x128x16, YZX order — Y changes fastest)
    ///   Data/SkyLight/BlockLight: byte[16384] nibble arrays
    /// </summary>
    private static void FlattenAnvilSections(
        NbtCompound level,
        out byte[] blocks,
        out byte[] data,
        out byte[] skyLight,
        out byte[] blockLight)
    {
        blocks = new byte[CHUNK_BLOCKS];
        data = new byte[CHUNK_HALF];
        skyLight = new byte[CHUNK_HALF];
        blockLight = new byte[CHUNK_HALF];

        // Fill skylight with max (15) by default — same as LCE does for empty sections
        Array.Fill(skyLight, (byte)0xFF);

        var sections = level.Get<NbtList>("Sections");
        if (sections == null) return;

        foreach (NbtTag sectionTag in sections)
        {
            if (sectionTag is not NbtCompound section) continue;
            var yTag = section.Get<NbtByte>("Y");
            if (yTag == null) continue;
            int sectionY = yTag.Value;
            if (sectionY < 0 || sectionY > 7) continue; // Only Y=0-7 (blocks 0-127)

            byte[]? sBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
            byte[]? sData = section.Get<NbtByteArray>("Data")?.Value;
            byte[]? sSkyLight = section.Get<NbtByteArray>("SkyLight")?.Value;
            byte[]? sBlockLight = section.Get<NbtByteArray>("BlockLight")?.Value;

            if (sBlocks == null) continue;

            // Anvil section block order: Y*16*16 + Z*16 + X (YZX within the 16x16x16 section)
            // LCE flat block order: Y*16*16 + Z*16 + X (YZX, but Y spans full 0-127)
            // So section Y=s, local y within section = ly
            // Global Y = s*16 + ly
            // Flat index = globalY * 256 + z * 16 + x
            // Section index = ly * 256 + z * 16 + x

            int baseY = sectionY * 16;

            // Copy block data
            for (int i = 0; i < 4096; i++)
            {
                int ly = i / 256;        // local y within section (0-15)
                int remainder = i % 256;  // z*16 + x
                int globalY = baseY + ly;
                int flatIndex = globalY * 256 + remainder;
                blocks[flatIndex] = sBlocks[i];
            }

            // Copy nibble arrays (data, skylight, blocklight)
            // Each byte holds 2 nibbles: low nibble = even index, high nibble = odd index
            if (sData != null)
                CopyNibbleSection(sData, data, baseY);
            if (sSkyLight != null)
                CopyNibbleSection(sSkyLight, skyLight, baseY);
            if (sBlockLight != null)
                CopyNibbleSection(sBlockLight, blockLight, baseY);
        }
    }

    /// <summary>
    /// Copies a 2048-byte nibble array from an Anvil section into the flat LCE nibble array.
    /// Nibble arrays: each byte = 2 values. Index i has low nibble at byte i/2 low bits,
    /// high nibble at byte i/2 high bits (when i is odd).
    /// </summary>
    private static void CopyNibbleSection(byte[] sectionNibbles, byte[] flatNibbles, int baseY)
    {
        for (int i = 0; i < 4096; i++)
        {
            int ly = i / 256;
            int remainder = i % 256;
            int globalY = baseY + ly;
            int flatBlockIndex = globalY * 256 + remainder;

            // Get nibble value from section
            int sNibbleIndex = i / 2;
            int sNibbleValue;
            if ((i & 1) == 0)
                sNibbleValue = sectionNibbles[sNibbleIndex] & 0x0F;
            else
                sNibbleValue = (sectionNibbles[sNibbleIndex] >> 4) & 0x0F;

            // Set nibble value in flat array
            int fNibbleIndex = flatBlockIndex / 2;
            if ((flatBlockIndex & 1) == 0)
                flatNibbles[fNibbleIndex] = (byte)((flatNibbles[fNibbleIndex] & 0xF0) | sNibbleValue);
            else
                flatNibbles[fNibbleIndex] = (byte)((flatNibbles[fNibbleIndex] & 0x0F) | (sNibbleValue << 4));
        }
    }

    private static byte[] GetByteArrayOrDefault(NbtCompound tag, string name, int defaultSize)
    {
        var arr = tag.Get<NbtByteArray>(name);
        if (arr != null && arr.Value.Length >= defaultSize)
            return arr.Value[..defaultSize]; // Trim if longer (e.g. Java 1.8+ heightmap is int[])
        
        byte[] result = new byte[defaultSize];
        if (arr != null)
            Buffer.BlockCopy(arr.Value, 0, result, 0, Math.Min(arr.Value.Length, defaultSize));
        return result;
    }
}
