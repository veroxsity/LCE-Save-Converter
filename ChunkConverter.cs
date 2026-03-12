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

        RemapBlocks(blocks, data);

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

            if (!TryDecodeSectionBlocks(section, out byte[] sBlocks, out byte[] sData))
                continue;

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

    private static bool TryDecodeSectionBlocks(NbtCompound section, out byte[] blocks, out byte[] data)
    {
        byte[]? oldBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
        if (oldBlocks != null && oldBlocks.Length >= 4096)
        {
            blocks = oldBlocks;
            data = section.Get<NbtByteArray>("Data")?.Value ?? new byte[CHUNK_NIBBLES];
            return true;
        }

        return TryDecodePaletteSection(section, out blocks, out data);
    }

    private static bool TryDecodePaletteSection(NbtCompound section, out byte[] blocks, out byte[] data)
    {
        blocks = new byte[4096];
        data = new byte[2048];

        var palette = section.Get<NbtList>("Palette");
        if (palette == null || palette.Count == 0)
            return false;

        if (palette.Count == 1)
        {
            if (palette[0] is not NbtCompound singleEntry)
                return false;

            LegacyBlockState singleBlock = MapModernBlockState(singleEntry);
            Array.Fill(blocks, singleBlock.Id);
            if (singleBlock.Data != 0)
            {
                for (int i = 0; i < 4096; i++)
                    SetNibble(data, i, singleBlock.Data);
            }
            return true;
        }

        if (section["BlockStates"] is not NbtLongArray blockStatesTag)
            return false;

        long[] blockStates = blockStatesTag.Value;
        if (blockStates.Length == 0)
            return false;

        int bitsPerBlock = Math.Max(4, GetBitsRequired(palette.Count - 1));
        bool usePaddedLayout = blockStates.Length == GetExpectedPaddedLongCount(bitsPerBlock);

        for (int i = 0; i < 4096; i++)
        {
            int paletteIndex = usePaddedLayout
                ? ReadPaddedBlockState(blockStates, bitsPerBlock, i)
                : ReadCompactBlockState(blockStates, bitsPerBlock, i);

            if ((uint)paletteIndex >= (uint)palette.Count)
                continue;

            if (palette[paletteIndex] is not NbtCompound entry)
                continue;

            LegacyBlockState legacy = MapModernBlockState(entry);
            blocks[i] = legacy.Id;
            if (legacy.Data != 0)
                SetNibble(data, i, legacy.Data);
        }

        return true;
    }

    private static int GetBitsRequired(int value)
    {
        int bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }
        return Math.Max(bits, 1);
    }

    private static int GetExpectedPaddedLongCount(int bitsPerBlock)
    {
        int valuesPerLong = Math.Max(1, 64 / bitsPerBlock);
        return (4096 + valuesPerLong - 1) / valuesPerLong;
    }

    private static int ReadPaddedBlockState(long[] blockStates, int bitsPerBlock, int index)
    {
        int valuesPerLong = Math.Max(1, 64 / bitsPerBlock);
        int longIndex = index / valuesPerLong;
        int bitOffset = (index % valuesPerLong) * bitsPerBlock;
        ulong mask = ((1UL << bitsPerBlock) - 1UL);
        return (int)(((ulong)blockStates[longIndex] >> bitOffset) & mask);
    }

    private static int ReadCompactBlockState(long[] blockStates, int bitsPerBlock, int index)
    {
        int startBit = index * bitsPerBlock;
        int longIndex = startBit >> 6;
        int bitOffset = startBit & 63;
        ulong mask = ((1UL << bitsPerBlock) - 1UL);

        ulong value = (ulong)blockStates[longIndex] >> bitOffset;
        int bitsRead = 64 - bitOffset;
        if (bitsRead < bitsPerBlock && longIndex + 1 < blockStates.Length)
            value |= (ulong)blockStates[longIndex + 1] << bitsRead;

        return (int)(value & mask);
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

    private static LegacyBlockState MapModernBlockState(NbtCompound paletteEntry)
    {
        string name = paletteEntry.Get<NbtString>("Name")?.Value ?? "minecraft:air";
        if (name.StartsWith("minecraft:", StringComparison.Ordinal))
            name = name[10..];

        NbtCompound? properties = paletteEntry.Get<NbtCompound>("Properties");

        if (ModernDirectMap.TryGetValue(name, out var direct))
            return direct;

        if (TryMapColoredBlock(name, properties, out var colored))
            return colored;

        if (TryMapWoodBlock(name, properties, out var wood))
            return wood;

        if (TryMapVariantBlock(name, properties, out var variant))
            return variant;

        return new LegacyBlockState(0, 0);
    }

    private static bool TryMapColoredBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;
        byte color = GetColorData(GetProperty(properties, "color"));

        switch (name)
        {
            case "wool":
                block = new LegacyBlockState(35, color);
                return true;
            case "stained_glass":
                block = new LegacyBlockState(95, color);
                return true;
            case "stained_glass_pane":
                block = new LegacyBlockState(160, color);
                return true;
            case "stained_hardened_clay":
            case "terracotta":
                block = new LegacyBlockState(159, color);
                return true;
            case "concrete":
                block = new LegacyBlockState(172, color);
                return true;
            case "concrete_powder":
                block = new LegacyBlockState(12, color);
                return true;
            case "glazed_terracotta":
                block = new LegacyBlockState(159, color);
                return true;
            default:
                return false;
        }
    }

    private static bool TryMapWoodBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        string woodType = GetProperty(properties, "variant");
        if (string.IsNullOrEmpty(woodType))
            woodType = GetPrefixBeforeUnderscore(name);

        byte dataValue = woodType switch
        {
            "spruce" => (byte)1,
            "birch" => (byte)2,
            "jungle" => (byte)3,
            "acacia" => (byte)0,
            "dark_oak" => (byte)1,
            _ => (byte)0,
        };

        if (name.EndsWith("_planks", StringComparison.Ordinal) || name == "planks")
        {
            block = new LegacyBlockState(5, dataValue);
            return true;
        }

        if (name.EndsWith("_sapling", StringComparison.Ordinal) || name == "sapling")
        {
            block = new LegacyBlockState(6, dataValue);
            return true;
        }

        if (name.EndsWith("_log", StringComparison.Ordinal) || name == "log")
        {
            block = new LegacyBlockState(17, dataValue > 3 ? (byte)3 : dataValue);
            return true;
        }

        if (name.EndsWith("_leaves", StringComparison.Ordinal) || name == "leaves")
        {
            block = new LegacyBlockState(18, dataValue > 3 ? (byte)3 : dataValue);
            return true;
        }

        if (name.EndsWith("_stairs", StringComparison.Ordinal) && name.Contains("wood", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(53, 0);
            return true;
        }

        if (name.EndsWith("_door", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(64, 0);
            return true;
        }

        if (name.EndsWith("_fence", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(85, 0);
            return true;
        }

        if (name.EndsWith("_fence_gate", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(107, 0);
            return true;
        }

        if (name.EndsWith("_pressure_plate", StringComparison.Ordinal) &&
            (name.Contains("oak", StringComparison.Ordinal) ||
             name.Contains("spruce", StringComparison.Ordinal) ||
             name.Contains("birch", StringComparison.Ordinal) ||
             name.Contains("jungle", StringComparison.Ordinal) ||
             name.Contains("acacia", StringComparison.Ordinal) ||
             name.Contains("dark_oak", StringComparison.Ordinal)))
        {
            block = new LegacyBlockState(72, 0);
            return true;
        }

        return false;
    }

    private static bool TryMapVariantBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        switch (name)
        {
            case "grass_block":
                block = new LegacyBlockState(2, 0);
                return true;
            case "coarse_dirt":
            case "podzol":
                block = new LegacyBlockState(3, 0);
                return true;
            case "grass_path":
                block = new LegacyBlockState(2, 0);
                return true;
            case "cobblestone_wall":
                block = new LegacyBlockState(139, 0);
                return true;
            case "mossy_cobblestone_wall":
                block = new LegacyBlockState(139, 1);
                return true;
            case "smooth_stone_slab":
                block = new LegacyBlockState(44, 0);
                return true;
            case "stone_brick_stairs":
                block = new LegacyBlockState(109, 0);
                return true;
            case "mossy_stone_bricks":
                block = new LegacyBlockState(98, 1);
                return true;
            case "cracked_stone_bricks":
                block = new LegacyBlockState(98, 2);
                return true;
            case "chiseled_stone_bricks":
                block = new LegacyBlockState(98, 3);
                return true;
            case "red_sand":
                block = new LegacyBlockState(12, 1);
                return true;
            case "red_sandstone":
                block = new LegacyBlockState(24, 0);
                return true;
            case "red_sandstone_stairs":
                block = new LegacyBlockState(128, 0);
                return true;
            case "red_sandstone_slab":
                block = new LegacyBlockState(44, 1);
                return true;
            case "prismarine":
            case "prismarine_bricks":
            case "dark_prismarine":
                block = new LegacyBlockState(1, 0);
                return true;
            case "sea_lantern":
                block = new LegacyBlockState(89, 0);
                return true;
            case "purpur_block":
            case "purpur_pillar":
                block = new LegacyBlockState(155, 0);
                return true;
            case "purpur_stairs":
                block = new LegacyBlockState(156, 0);
                return true;
            case "purpur_slab":
                block = new LegacyBlockState(44, 0);
                return true;
            case "end_stone_bricks":
                block = new LegacyBlockState(121, 0);
                return true;
            case "magma_block":
                block = new LegacyBlockState(87, 0);
                return true;
            case "nether_wart_block":
            case "red_nether_bricks":
                block = new LegacyBlockState(112, 0);
                return true;
            case "bone_block":
                block = new LegacyBlockState(1, 0);
                return true;
            case "observer":
            case "beetroots":
            case "kelp":
            case "kelp_plant":
            case "seagrass":
            case "tall_seagrass":
            case "coral_block":
            case "tube_coral_block":
            case "brain_coral_block":
            case "bubble_coral_block":
            case "fire_coral_block":
            case "horn_coral_block":
            case "end_rod":
            case "chorus_plant":
            case "chorus_flower":
            case "end_gateway":
            case "structure_void":
                block = new LegacyBlockState(0, 0);
                return true;
            case "packed_ice":
            case "blue_ice":
                block = new LegacyBlockState(79, 0);
                return true;
            case "sunflower":
            case "lilac":
            case "rose_bush":
            case "peony":
            case "tall_grass":
            case "large_fern":
                block = new LegacyBlockState(31, 1);
                return true;
        }

        string? type = GetProperty(properties, "type");
        if (name == "stone")
        {
            block = type switch
            {
                "granite" => new LegacyBlockState(1, 0),
                "diorite" => new LegacyBlockState(1, 0),
                "andesite" => new LegacyBlockState(1, 0),
                _ => new LegacyBlockState(1, 0),
            };
            return true;
        }

        if (name == "sandstone")
        {
            block = new LegacyBlockState(24, (byte)(type switch
            {
                "chiseled" => 1,
                "smooth" => 2,
                _ => 0,
            }));
            return true;
        }

        if (name == "stone_bricks")
        {
            block = new LegacyBlockState(98, (byte)(type switch
            {
                "mossy" => 1,
                "cracked" => 2,
                "chiseled" => 3,
                _ => 0,
            }));
            return true;
        }

        if (name == "tall_grass")
        {
            string grassType = GetProperty(properties, "type");
            block = new LegacyBlockState(31, (byte)(grassType == "fern" ? 2 : 1));
            return true;
        }

        return false;
    }

    private static string GetProperty(NbtCompound? properties, string name)
    {
        return properties?.Get<NbtString>(name)?.Value ?? string.Empty;
    }

    private static string GetPrefixBeforeUnderscore(string value)
    {
        int index = value.IndexOf('_');
        return index > 0 ? value[..index] : value;
    }

    private static byte GetColorData(string color)
    {
        return color switch
        {
            "white" => 0,
            "orange" => 1,
            "magenta" => 2,
            "light_blue" => 3,
            "yellow" => 4,
            "lime" => 5,
            "pink" => 6,
            "gray" => 7,
            "light_gray" => 8,
            "cyan" => 9,
            "purple" => 10,
            "blue" => 11,
            "brown" => 12,
            "green" => 13,
            "red" => 14,
            "black" => 15,
            _ => 0,
        };
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

    /// <summary>
    /// Remaps block IDs that exist in Java 1.7–1.12 but not in LCE TU19 to safe equivalents.
    /// Block IDs 0–163 are present in both Java 1.6.4 and LCE TU19 — they are not touched.
    /// Some blocks (stained glass 95, packed ice 174, etc.) were also added to LCE in early TUs,
    /// so they are left as-is. The replacements below handle blocks that truly didn't exist in TU19.
    /// </summary>
    private static void RemapBlocks(byte[] blocks, byte[] data)
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            byte id = blocks[i];
            if (id < 165) continue; // All IDs below 165 are safe in LCE TU19

            if (BlockRemapTable.TryGetValue(id, out byte replacement))
                blocks[i] = replacement;
        }
    }

    /// <summary>
    /// Maps Java block IDs added after 1.6.4 to their nearest LCE TU19 equivalent.
    /// Replacement strategy: visual similarity first, then air for non-physical blocks.
    /// </summary>
    private readonly record struct LegacyBlockState(byte Id, byte Data);

    private static readonly Dictionary<string, LegacyBlockState> ModernDirectMap = new(StringComparer.Ordinal)
    {
        ["air"] = new LegacyBlockState(0, 0),
        ["cave_air"] = new LegacyBlockState(0, 0),
        ["void_air"] = new LegacyBlockState(0, 0),
        ["stone"] = new LegacyBlockState(1, 0),
        ["grass"] = new LegacyBlockState(2, 0),
        ["dirt"] = new LegacyBlockState(3, 0),
        ["cobblestone"] = new LegacyBlockState(4, 0),
        ["bedrock"] = new LegacyBlockState(7, 0),
        ["water"] = new LegacyBlockState(8, 0),
        ["lava"] = new LegacyBlockState(10, 0),
        ["sand"] = new LegacyBlockState(12, 0),
        ["gravel"] = new LegacyBlockState(13, 0),
        ["gold_ore"] = new LegacyBlockState(14, 0),
        ["iron_ore"] = new LegacyBlockState(15, 0),
        ["coal_ore"] = new LegacyBlockState(16, 0),
        ["oak_log"] = new LegacyBlockState(17, 0),
        ["oak_leaves"] = new LegacyBlockState(18, 0),
        ["glass"] = new LegacyBlockState(20, 0),
        ["lapis_ore"] = new LegacyBlockState(21, 0),
        ["lapis_block"] = new LegacyBlockState(22, 0),
        ["dispenser"] = new LegacyBlockState(23, 0),
        ["sandstone"] = new LegacyBlockState(24, 0),
        ["note_block"] = new LegacyBlockState(25, 0),
        ["powered_rail"] = new LegacyBlockState(27, 0),
        ["detector_rail"] = new LegacyBlockState(28, 0),
        ["sticky_piston"] = new LegacyBlockState(29, 0),
        ["cobweb"] = new LegacyBlockState(30, 0),
        ["dead_bush"] = new LegacyBlockState(32, 0),
        ["piston"] = new LegacyBlockState(33, 0),
        ["wool"] = new LegacyBlockState(35, 0),
        ["dandelion"] = new LegacyBlockState(37, 0),
        ["poppy"] = new LegacyBlockState(38, 0),
        ["brown_mushroom"] = new LegacyBlockState(39, 0),
        ["red_mushroom"] = new LegacyBlockState(40, 0),
        ["gold_block"] = new LegacyBlockState(41, 0),
        ["iron_block"] = new LegacyBlockState(42, 0),
        ["stone_slab"] = new LegacyBlockState(44, 0),
        ["bricks"] = new LegacyBlockState(45, 0),
        ["tnt"] = new LegacyBlockState(46, 0),
        ["bookshelf"] = new LegacyBlockState(47, 0),
        ["mossy_cobblestone"] = new LegacyBlockState(48, 0),
        ["obsidian"] = new LegacyBlockState(49, 0),
        ["torch"] = new LegacyBlockState(50, 0),
        ["fire"] = new LegacyBlockState(51, 0),
        ["mob_spawner"] = new LegacyBlockState(52, 0),
        ["oak_stairs"] = new LegacyBlockState(53, 0),
        ["chest"] = new LegacyBlockState(54, 0),
        ["diamond_ore"] = new LegacyBlockState(56, 0),
        ["diamond_block"] = new LegacyBlockState(57, 0),
        ["crafting_table"] = new LegacyBlockState(58, 0),
        ["farmland"] = new LegacyBlockState(60, 0),
        ["furnace"] = new LegacyBlockState(61, 0),
        ["ladder"] = new LegacyBlockState(65, 0),
        ["rail"] = new LegacyBlockState(66, 0),
        ["lever"] = new LegacyBlockState(69, 0),
        ["stone_pressure_plate"] = new LegacyBlockState(70, 0),
        ["oak_pressure_plate"] = new LegacyBlockState(72, 0),
        ["redstone_ore"] = new LegacyBlockState(73, 0),
        ["redstone_torch"] = new LegacyBlockState(76, 0),
        ["stone_button"] = new LegacyBlockState(77, 0),
        ["snow"] = new LegacyBlockState(78, 0),
        ["ice"] = new LegacyBlockState(79, 0),
        ["snow_block"] = new LegacyBlockState(80, 0),
        ["cactus"] = new LegacyBlockState(81, 0),
        ["clay"] = new LegacyBlockState(82, 0),
        ["jukebox"] = new LegacyBlockState(84, 0),
        ["oak_fence"] = new LegacyBlockState(85, 0),
        ["netherrack"] = new LegacyBlockState(87, 0),
        ["soul_sand"] = new LegacyBlockState(88, 0),
        ["glowstone"] = new LegacyBlockState(89, 0),
        ["jack_o_lantern"] = new LegacyBlockState(91, 0),
        ["stone_bricks"] = new LegacyBlockState(98, 0),
        ["brown_mushroom_block"] = new LegacyBlockState(99, 0),
        ["red_mushroom_block"] = new LegacyBlockState(100, 0),
        ["iron_bars"] = new LegacyBlockState(101, 0),
        ["glass_pane"] = new LegacyBlockState(102, 0),
        ["melon"] = new LegacyBlockState(103, 0),
        ["vine"] = new LegacyBlockState(106, 0),
        ["oak_fence_gate"] = new LegacyBlockState(107, 0),
        ["brick_stairs"] = new LegacyBlockState(108, 0),
        ["stone_brick_stairs"] = new LegacyBlockState(109, 0),
        ["mycelium"] = new LegacyBlockState(110, 0),
        ["lily_pad"] = new LegacyBlockState(111, 0),
        ["nether_bricks"] = new LegacyBlockState(112, 0),
        ["nether_brick_fence"] = new LegacyBlockState(113, 0),
        ["nether_brick_stairs"] = new LegacyBlockState(114, 0),
        ["enchanting_table"] = new LegacyBlockState(116, 0),
        ["end_stone"] = new LegacyBlockState(121, 0),
        ["sandstone_stairs"] = new LegacyBlockState(128, 0),
        ["emerald_ore"] = new LegacyBlockState(129, 0),
        ["ender_chest"] = new LegacyBlockState(130, 0),
        ["tripwire_hook"] = new LegacyBlockState(131, 0),
        ["emerald_block"] = new LegacyBlockState(133, 0),
        ["spruce_stairs"] = new LegacyBlockState(134, 0),
        ["birch_stairs"] = new LegacyBlockState(135, 0),
        ["jungle_stairs"] = new LegacyBlockState(136, 0),
        ["command_block"] = new LegacyBlockState(137, 0),
        ["beacon"] = new LegacyBlockState(138, 0),
        ["cobblestone_wall"] = new LegacyBlockState(139, 0),
        ["flower_pot"] = new LegacyBlockState(140, 0),
        ["carrots"] = new LegacyBlockState(141, 0),
        ["potatoes"] = new LegacyBlockState(142, 0),
        ["oak_button"] = new LegacyBlockState(143, 0),
        ["anvil"] = new LegacyBlockState(145, 0),
        ["trapped_chest"] = new LegacyBlockState(146, 0),
        ["light_weighted_pressure_plate"] = new LegacyBlockState(147, 0),
        ["heavy_weighted_pressure_plate"] = new LegacyBlockState(148, 0),
        ["daylight_detector"] = new LegacyBlockState(151, 0),
        ["redstone_block"] = new LegacyBlockState(152, 0),
        ["quartz_ore"] = new LegacyBlockState(153, 0),
        ["hopper"] = new LegacyBlockState(154, 0),
        ["quartz_block"] = new LegacyBlockState(155, 0),
        ["quartz_stairs"] = new LegacyBlockState(156, 0),
        ["activator_rail"] = new LegacyBlockState(157, 0),
        ["dropper"] = new LegacyBlockState(158, 0),
        ["stained_hardened_clay"] = new LegacyBlockState(159, 0),
        ["stained_glass"] = new LegacyBlockState(95, 0),
        ["stained_glass_pane"] = new LegacyBlockState(160, 0),
        ["leaves2"] = new LegacyBlockState(161, 0),
        ["log2"] = new LegacyBlockState(162, 0),
        ["acacia_stairs"] = new LegacyBlockState(163, 0),
        ["dark_oak_stairs"] = new LegacyBlockState(164, 0),
    };

    private static readonly Dictionary<byte, byte> BlockRemapTable = new()
    {
        // Java 1.7 additions
        { 174, 79  },  // Packed Ice        → Ice
        { 175, 0   },  // Double Plants (sunflower, lilac, etc.) → Air

        // Java 1.8 additions
        { 165, 0   },  // Slime Block       → Air (didn't exist in TU19)
        { 166, 0   },  // Barrier           → Air
        { 167, 96  },  // Iron Trapdoor     → Wooden Trapdoor
        { 168, 1   },  // Prismarine        → Stone
        { 169, 89  },  // Sea Lantern       → Glowstone
        { 176, 0   },  // Standing Banner   → Air
        { 177, 0   },  // Wall Banner       → Air
        { 178, 151 },  // Inverted Daylight Sensor → Daylight Sensor
        { 179, 24  },  // Red Sandstone     → Sandstone
        { 180, 128 },  // Red Sandstone Stairs → Sandstone Stairs
        { 181, 43  },  // Double Red Sandstone Slab → Double Stone Slab
        { 182, 44  },  // Red Sandstone Slab → Stone Slab
        { 183, 107 },  // Spruce Fence Gate → Oak Fence Gate
        { 184, 107 },  // Birch Fence Gate  → Oak Fence Gate
        { 185, 107 },  // Jungle Fence Gate → Oak Fence Gate
        { 186, 107 },  // Dark Oak Fence Gate → Oak Fence Gate
        { 187, 107 },  // Acacia Fence Gate → Oak Fence Gate
        { 188, 85  },  // Spruce Fence      → Oak Fence
        { 189, 85  },  // Birch Fence       → Oak Fence
        { 190, 85  },  // Jungle Fence      → Oak Fence
        { 191, 85  },  // Dark Oak Fence    → Oak Fence
        { 192, 85  },  // Acacia Fence      → Oak Fence
        { 193, 64  },  // Spruce Door       → Oak Door
        { 194, 64  },  // Birch Door        → Oak Door
        { 195, 64  },  // Jungle Door       → Oak Door
        { 196, 64  },  // Acacia Door       → Oak Door
        { 197, 64  },  // Dark Oak Door     → Oak Door

        // Java 1.9 additions
        { 198, 0   },  // End Rod           → Air
        { 199, 0   },  // Chorus Plant      → Air
        { 200, 0   },  // Chorus Flower     → Air
        { 201, 155 },  // Purpur Block      → Quartz Block
        { 202, 155 },  // Purpur Pillar     → Quartz Block
        { 203, 156 },  // Purpur Stairs     → Quartz Stairs
        { 204, 43  },  // Purpur Double Slab → Double Stone Slab
        { 205, 44  },  // Purpur Slab       → Stone Slab
        { 206, 121 },  // End Stone Bricks  → End Stone
        { 207, 0   },  // Beetroots         → Air
        { 208, 2   },  // Grass Path        → Grass
        { 209, 0   },  // End Gateway       → Air
        { 210, 137 },  // Repeating Command Block → Command Block
        { 211, 137 },  // Chain Command Block → Command Block
        { 212, 79  },  // Frosted Ice       → Ice

        // Java 1.10 additions
        { 213, 87  },  // Magma Block       → Netherrack
        { 214, 112 },  // Nether Wart Block → Nether Brick
        { 215, 112 },  // Red Nether Brick  → Nether Brick
        { 216, 1   },  // Bone Block        → Stone (no smooth equivalent)
        { 217, 0   },  // Structure Void    → Air

        // Java 1.11 additions
        { 218, 0   },  // Observer          → Air
        { 219, 0   },  // White Shulker Box → Air
        { 220, 0   },  // Orange Shulker Box → Air
        { 221, 0   },  // Magenta Shulker Box → Air
        { 222, 0   },  // Light Blue Shulker Box → Air
        { 223, 0   },  // Yellow Shulker Box → Air
        { 224, 0   },  // Lime Shulker Box  → Air
        { 225, 0   },  // Pink Shulker Box  → Air
        { 226, 0   },  // Gray Shulker Box  → Air
        { 227, 0   },  // Silver Shulker Box → Air
        { 228, 0   },  // Cyan Shulker Box  → Air
        { 229, 0   },  // Purple Shulker Box → Air
        { 230, 0   },  // Blue Shulker Box  → Air
        { 231, 0   },  // Brown Shulker Box → Air
        { 232, 0   },  // Green Shulker Box → Air
        { 233, 0   },  // Red Shulker Box   → Air
        { 234, 0   },  // Black Shulker Box → Air

        // Java 1.12 additions
        { 235, 159 },  // White Glazed Terracotta → Stained Hardened Clay
        { 236, 159 },  // Orange Glazed Terracotta → Stained Hardened Clay
        { 237, 159 },  // Magenta Glazed Terracotta → Stained Hardened Clay
        { 238, 159 },  // Light Blue Glazed Terracotta → Stained Hardened Clay
        { 239, 159 },  // Yellow Glazed Terracotta → Stained Hardened Clay
        { 240, 159 },  // Lime Glazed Terracotta → Stained Hardened Clay
        { 241, 159 },  // Pink Glazed Terracotta → Stained Hardened Clay
        { 242, 159 },  // Gray Glazed Terracotta → Stained Hardened Clay
        { 243, 159 },  // Silver Glazed Terracotta → Stained Hardened Clay (also Podzol in 1.7)
        { 244, 159 },  // Cyan Glazed Terracotta → Stained Hardened Clay
        { 245, 159 },  // Purple Glazed Terracotta → Stained Hardened Clay
        { 246, 159 },  // Blue Glazed Terracotta → Stained Hardened Clay
        { 247, 159 },  // Brown Glazed Terracotta → Stained Hardened Clay
        { 248, 159 },  // Green Glazed Terracotta → Stained Hardened Clay
        { 249, 159 },  // Red Glazed Terracotta → Stained Hardened Clay
        { 250, 159 },  // Black Glazed Terracotta → Stained Hardened Clay
        { 251, 172 },  // Concrete           → Hardened Clay
        { 252, 12  },  // Concrete Powder    → Sand
    };
}
