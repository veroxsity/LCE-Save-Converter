using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Converts Java chunk data to pre-v8 LCE chunk NBT.
/// This intentionally targets the old NBT chunk path so the game upgrades chunk storage itself.
/// </summary>
public static class ChunkConverter
{
    // Stability-first default: converted worlds should load even when source entity schemas differ.
    // Set by CLI flag (--preserve-entities) when the caller explicitly wants dynamic data retained.
    public static bool PreserveDynamicChunkData { get; set; }

    public const int CHUNK_BLOCKS = 32768;
    public const int CHUNK_NIBBLES = 16384;
    public const int HEIGHTMAP_SIZE = 256;
    public const int BIOMES_SIZE = 256;

    // Modern (1.13+) worlds can have sections above LCE's 0..127 range.
    // Use one shift value for the whole conversion run to keep chunk heights consistent.
    private static int? _globalModernSectionShift;
    private static readonly HashSet<string> _unknownModernBlocks = new(StringComparer.Ordinal);

    public static void ResetUnknownModernBlocks()
    {
        _unknownModernBlocks.Clear();
    }

    public static IReadOnlyList<string> GetUnknownModernBlocksSnapshot()
    {
        return _unknownModernBlocks.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public static byte[] ConvertChunk(NbtCompound rootTag, int newChunkX, int newChunkZ)
    {
        bool hasLegacyLevelWrapper = rootTag.Get<NbtCompound>("Level") != null;
        int dataVersion = rootTag.Get<NbtInt>("DataVersion")?.Value
            ?? rootTag.Get<NbtCompound>("Level")?.Get<NbtInt>("DataVersion")?.Value
            ?? 0;
        var sourceLevel = rootTag.Get<NbtCompound>("Level") ?? rootTag;

        // 1.13+ worlds use modern block/entity schemas even when some chunks still keep a Level wrapper
        // or have missing DataVersion tags in individual chunks.
        bool isModernChunkLayout = !hasLegacyLevelWrapper
            || dataVersion >= 1519
            || HasModernChunkSchema(sourceLevel);

        bool isAnvil = sourceLevel.Contains("Sections") || sourceLevel.Contains("sections");

        byte[] blocks;
        byte[] data;
        byte[] skyLight;
        byte[] blockLight;
        if (isAnvil)
        {
            FlattenAnvilSections(sourceLevel, isModernChunkLayout, out blocks, out data, out skyLight, out blockLight);
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

        if (isModernChunkLayout)
        {
            // Modern biome IDs can be out-of-range for TU19. Use a safe default biome.
            Array.Fill(biomes, (byte)1); // Plains
        }

        RemapBlocks(blocks, data);

        long lastUpdate = sourceLevel.Get<NbtLong>("LastUpdate")?.Value ?? 0;
        long inhabitedTime = sourceLevel.Get<NbtLong>("InhabitedTime")?.Value ?? 0;

        // Build the root + Level compound expected by NbtIo::read in old chunk path.
        // TerrainPopulatedFlags encodes which post-processing passes have been completed.
        // Value: sTerrainPopulatedAllNeighbours (1022) | sTerrainPostPostProcessed (1024) = 2046
        // Source: LevelChunk.h constants + EmptyLevelChunk.cpp initialization
        const short TERRAIN_POPULATED_FLAGS = 2046;  // 0x07FE
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
            new NbtShort("TerrainPopulatedFlags", TERRAIN_POPULATED_FLAGS),
            new NbtByteArray("Biomes", biomes),
        };

        // Compute the block-space offset applied to this chunk during world recentring.
        int sourceChunkX = sourceLevel.Get<NbtInt>("xPos")?.Value ?? newChunkX;
        int sourceChunkZ = sourceLevel.Get<NbtInt>("zPos")?.Value ?? newChunkZ;
        int blockOffsetX = (sourceChunkX - newChunkX) * 16;
        int blockOffsetZ = (sourceChunkZ - newChunkZ) * 16;

        // Modern Java chunks (1.13+) often contain entity/tile-entity data that is not
        // compatible with TU19 deserializers. Writing empty lists is safer than crashing.
        var entities = (PreserveDynamicChunkData && !isModernChunkLayout)
            ? (NbtList)CloneOrEmptyList(sourceLevel, "Entities")
            : new NbtList("Entities", NbtTagType.Compound);
        if (PreserveDynamicChunkData)
        {
            SanitizeEntities(entities);
            SanitizeLegacyItemStacks(entities);
            RemapEntityPositions(entities, blockOffsetX, blockOffsetZ);
        }
        level.Add(entities);

        var tileEntities = (PreserveDynamicChunkData && !isModernChunkLayout)
            ? CloneListOrEmpty(sourceLevel, "TileEntities", "block_entities")
            : new NbtList("TileEntities", NbtTagType.Compound);
        tileEntities.Name = "TileEntities";
        RemoveUnsupportedTileEntities(tileEntities);
        if (PreserveDynamicChunkData)
        {
            SanitizeLegacyItemStacks(tileEntities);
            RemapTileEntityPositions(tileEntities, blockOffsetX, blockOffsetZ);
        }
        level.Add(tileEntities);

        if (PreserveDynamicChunkData && !isModernChunkLayout && (sourceLevel.Contains("TileTicks") || sourceLevel.Contains("block_ticks")))
        {
            var tileTicks = CloneListOrEmpty(sourceLevel, "TileTicks", "block_ticks");
            tileTicks.Name = "TileTicks";
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

    private static NbtList CloneListOrEmpty(NbtCompound sourceLevel, params string[] names)
    {
        foreach (string name in names)
        {
            if (sourceLevel.Contains(name) && sourceLevel[name] is NbtList list)
                return (NbtList)list.Clone();
        }

        return new NbtList(names[0], NbtTagType.Compound);
    }

    private static bool HasModernChunkSchema(NbtCompound level)
    {
        if (level.Contains("block_entities") || level.Contains("entities"))
            return true;

        var sections = level.Get<NbtList>("sections") ?? level.Get<NbtList>("Sections");
        if (sections == null)
            return false;

        foreach (NbtTag sectionTag in sections)
        {
            if (sectionTag is not NbtCompound section)
                continue;

            // Post-flattening chunk formats use block_states (1.18+) or Palette/BlockStates (1.13-1.17).
            if (section.Contains("block_states") || section.Contains("Palette") || section.Contains("BlockStates"))
                return true;
        }

        return false;
    }

    private static void FlattenAnvilSections(
        NbtCompound level,
        bool isModernChunkLayout,
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

        var sections = level.Get<NbtList>("Sections") ?? level.Get<NbtList>("sections");
        if (sections == null) return;

        var decodedSections = new List<DecodedSection>();
        foreach (NbtTag sectionTag in sections)
        {
            if (sectionTag is not NbtCompound section) continue;

            int sectionY = section.Get<NbtByte>("Y")?.Value ?? -1;
            if (!TryDecodeSectionBlocks(section, out byte[] sBlocks, out byte[] sData))
                continue;

            byte[]? sSky = section.Get<NbtByteArray>("SkyLight")?.Value ?? section.Get<NbtByteArray>("sky_light")?.Value;
            byte[]? sBlock = section.Get<NbtByteArray>("BlockLight")?.Value ?? section.Get<NbtByteArray>("block_light")?.Value;

            decodedSections.Add(new DecodedSection
            {
                SectionY = sectionY,
                Blocks = sBlocks,
                Data = sData,
                SkyLight = sSky,
                BlockLight = sBlock,
                NonAirCount = CountNonAir(sBlocks)
            });
        }

        if (decodedSections.Count == 0) return;

        int sectionShift = 0;
        if (isModernChunkLayout)
        {
            // Pick the modern shift once so adjacent chunks don't end up at different elevations.
            if (_globalModernSectionShift == null)
            {
                int anchorSectionY = decodedSections
                    .OrderByDescending(s => s.NonAirCount)
                    .ThenBy(s => Math.Abs(s.SectionY - 4))
                    .First()
                    .SectionY;
                _globalModernSectionShift = anchorSectionY - 4;
            }

            sectionShift = _globalModernSectionShift.Value;
        }

        foreach (var section in decodedSections)
        {
            int remappedSectionY = section.SectionY - sectionShift;
            if (remappedSectionY < 0 || remappedSectionY > 7) continue;

            int baseY = remappedSectionY * 16;

            // Anvil section order: x + z*16 + y*256. Old chunk order: (x*16 + z)*128 + y.
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 0x0F;
                int z = (i >> 4) & 0x0F;
                int y = (i >> 8) & 0x0F;
                int globalY = baseY + y;
                int flatIndex = ((x * 16) + z) * 128 + globalY;

                blocks[flatIndex] = section.Blocks[i];

                if (section.Data != null) SetNibble(data, flatIndex, GetNibble(section.Data, i));
                if (section.SkyLight != null) SetNibble(skyLight, flatIndex, GetNibble(section.SkyLight, i));
                if (section.BlockLight != null) SetNibble(blockLight, flatIndex, GetNibble(section.BlockLight, i));
            }
        }
    }

    private sealed class DecodedSection
    {
        public int SectionY { get; init; }
        public byte[] Blocks { get; init; } = Array.Empty<byte>();
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public byte[]? SkyLight { get; init; }
        public byte[]? BlockLight { get; init; }
        public int NonAirCount { get; init; }
    }

    private static int CountNonAir(byte[] blocks)
    {
        int count = 0;
        for (int i = 0; i < blocks.Length; i++)
            if (blocks[i] != 0) count++;
        return count;
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

        var blockStatesContainer = section.Get<NbtCompound>("block_states");
        var palette = section.Get<NbtList>("Palette") ?? blockStatesContainer?.Get<NbtList>("palette");
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

        NbtLongArray? blockStatesTag = section["BlockStates"] as NbtLongArray ?? blockStatesContainer?.Get<NbtLongArray>("data");
        if (blockStatesTag == null)
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

        if (TryMapFluidBlock(name, properties, out var fluid))
            return fluid;

        if (TryMapSlabBlock(name, properties, out var slab))
            return slab;

        if (TryMapDirectionalBlock(name, properties, out var directional))
            return directional;

        if (TryMapFlattenedColoredBlock(name, out var flattenedColored))
            return flattenedColored;

        if (ModernDirectMap.TryGetValue(name, out var direct))
            return direct;

        if (TryMapColoredBlock(name, properties, out var colored))
            return colored;

        if (TryMapWoodBlock(name, properties, out var wood))
            return wood;

        if (TryMapVariantBlock(name, properties, out var variant))
            return variant;

        RecordUnknownModernBlock(name);
        return new LegacyBlockState(0, 0);
    }

    private static void RecordUnknownModernBlock(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name is "air" or "cave_air" or "void_air")
            return;

        _unknownModernBlocks.Add(name);
    }

    private static bool TryMapFluidBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        if (name != "water" && name != "lava")
            return false;

        int level = GetIntProperty(properties, "level", 0);
        level = Math.Clamp(level, 0, 15);

        bool isSource = level == 0;
        byte legacyData = (byte)level;

        if (name == "water")
        {
            // Legacy uses id 9 for still/source water and id 8 for flowing water.
            block = new LegacyBlockState(isSource ? (byte)9 : (byte)8, legacyData);
            return true;
        }

        // Legacy uses id 11 for still/source lava and id 10 for flowing lava.
        block = new LegacyBlockState(isSource ? (byte)11 : (byte)10, legacyData);
        return true;
    }

    private static bool TryMapDirectionalBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        if (name == "ladder")
        {
            byte data = MapLadderFacing(GetProperty(properties, "facing"));
            block = new LegacyBlockState(65, data);
            return true;
        }

        if (name == "vine")
        {
            byte data = MapVineFaces(properties);
            block = new LegacyBlockState(106, data);
            return true;
        }

        if (name == "lever")
        {
            byte data = MapLeverData(GetProperty(properties, "face"), GetProperty(properties, "facing"));
            if (GetBoolProperty(properties, "powered"))
                data |= 8;
            block = new LegacyBlockState(69, data);
            return true;
        }

        if (name.EndsWith("_button", StringComparison.Ordinal))
        {
            byte id = IsWoodFamilyName(name) ? (byte)143 : (byte)77;
            byte data = MapButtonFacing(GetProperty(properties, "facing"));
            if (GetBoolProperty(properties, "powered"))
                data |= 8;
            block = new LegacyBlockState(id, data);
            return true;
        }

        if (name.EndsWith("_fence_gate", StringComparison.Ordinal))
        {
            byte data = MapFenceGateFacing(GetProperty(properties, "facing"));
            if (GetBoolProperty(properties, "open"))
                data |= 4;
            block = new LegacyBlockState(107, data);
            return true;
        }

        if (name.EndsWith("_pressure_plate", StringComparison.Ordinal))
        {
            byte id = name switch
            {
                "light_weighted_pressure_plate" => (byte)147,
                "heavy_weighted_pressure_plate" => (byte)148,
                _ when IsWoodFamilyName(name) => (byte)72,
                _ => (byte)70,
            };

            bool powered = GetBoolProperty(properties, "powered") || GetIntProperty(properties, "power", 0) > 0;
            block = new LegacyBlockState(id, (byte)(powered ? 1 : 0));
            return true;
        }

        // Doors require upper/lower metadata to behave correctly.
        if (name.EndsWith("_door", StringComparison.Ordinal) || name == "iron_door")
        {
            byte id = name == "iron_door" ? (byte)71 : (byte)64;
            string half = GetProperty(properties, "half");
            bool isUpper = half == "upper";

            if (isUpper)
            {
                // Upper half: bit 3 set, bit 0 stores hinge side, bit 1 may store powered.
                byte data = 8;
                if (GetProperty(properties, "hinge") == "right") data |= 1;
                if (GetBoolProperty(properties, "powered")) data |= 2;
                block = new LegacyBlockState(id, data);
                return true;
            }

            byte lower = MapDoorFacing(GetProperty(properties, "facing"));
            if (GetBoolProperty(properties, "open")) lower |= 4;
            block = new LegacyBlockState(id, lower);
            return true;
        }

        // Preserve stairs direction and upside-down flag.
        if (TryGetStairsId(name, out byte stairsId))
        {
            byte data = MapStairsFacing(GetProperty(properties, "facing"));
            if (GetProperty(properties, "half") == "top") data |= 4;
            block = new LegacyBlockState(stairsId, data);
            return true;
        }

        // Trapdoors need face/open/top bits.
        if (name.EndsWith("trapdoor", StringComparison.Ordinal))
        {
            byte data = MapTrapdoorFacing(GetProperty(properties, "facing"));
            if (GetBoolProperty(properties, "open")) data |= 4;
            if (GetProperty(properties, "half") == "top") data |= 8;
            block = new LegacyBlockState(96, data);
            return true;
        }

        return false;
    }

    private static bool TryMapSlabBlock(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        string slabType = GetProperty(properties, "type");
        bool isTop = slabType == "top";
        bool isDouble = slabType == "double";

        // Modern wood slabs (oak/spruce/birch/jungle/acacia/dark_oak)
        if (name.EndsWith("_slab", StringComparison.Ordinal) &&
            TryGetWoodSlabVariant(name, out byte woodVariant))
        {
            if (isDouble)
            {
                block = new LegacyBlockState(125, woodVariant); // double wood slab
                return true;
            }

            byte data = (byte)(woodVariant | (isTop ? 8 : 0));
            block = new LegacyBlockState(126, data); // wood slab half
            return true;
        }

        if (name.EndsWith("_slab", StringComparison.Ordinal) &&
            TryGetStoneSlabVariant(name, out byte slabVariant))
        {
            if (isDouble)
            {
                block = new LegacyBlockState(43, slabVariant); // double stone slab family
                return true;
            }

            byte data = (byte)(slabVariant | (isTop ? 8 : 0));
            block = new LegacyBlockState(44, data); // stone slab family
            return true;
        }

        return false;
    }

    private static bool TryGetWoodSlabVariant(string name, out byte variant)
    {
        variant = 0;
        return name switch
        {
            "oak_slab" => true,
            "spruce_slab" => (variant = 1) == 1,
            "birch_slab" => (variant = 2) == 2,
            "jungle_slab" => (variant = 3) == 3,
            "acacia_slab" => (variant = 4) == 4,
            "dark_oak_slab" => (variant = 5) == 5,
            _ => false,
        };
    }

    private static bool TryGetStoneSlabVariant(string name, out byte variant)
    {
        variant = 0;
        return name switch
        {
            // Legacy slab variant 0: stone-like slabs.
            "stone_slab" => true,
            "smooth_stone_slab" => true,
            "andesite_slab" => true,
            "polished_andesite_slab" => true,
            "diorite_slab" => true,
            "polished_diorite_slab" => true,
            "granite_slab" => true,
            "polished_granite_slab" => true,

            // Legacy slab variant 1: sandstone slabs.
            "sandstone_slab" => (variant = 1) == 1,
            "smooth_sandstone_slab" => (variant = 1) == 1,
            "cut_sandstone_slab" => (variant = 1) == 1,
            "red_sandstone_slab" => (variant = 1) == 1,
            "smooth_red_sandstone_slab" => (variant = 1) == 1,
            "cut_red_sandstone_slab" => (variant = 1) == 1,

            // Legacy slab variant 3: cobblestone-like slabs.
            "cobblestone_slab" => (variant = 3) == 3,
            "mossy_cobblestone_slab" => (variant = 3) == 3,
            "cobbled_deepslate_slab" => (variant = 3) == 3,
            "deepslate_brick_slab" => (variant = 3) == 3,
            "deepslate_tile_slab" => (variant = 3) == 3,

            // Legacy slab variant 4: brick slabs.
            "brick_slab" => (variant = 4) == 4,

            // Legacy slab variant 5: stone brick slabs.
            "stone_brick_slab" => (variant = 5) == 5,
            "mossy_stone_brick_slab" => (variant = 5) == 5,

            // Legacy slab variant 6: nether brick slabs.
            "nether_brick_slab" => (variant = 6) == 6,
            "red_nether_brick_slab" => (variant = 6) == 6,

            // Legacy slab variant 7: quartz-like fallback slabs.
            "quartz_slab" => (variant = 7) == 7,
            "smooth_quartz_slab" => (variant = 7) == 7,
            "purpur_slab" => (variant = 7) == 7,
            "prismarine_slab" => (variant = 7) == 7,
            "prismarine_brick_slab" => (variant = 7) == 7,
            "dark_prismarine_slab" => (variant = 7) == 7,
            _ => false,
        };
    }

    private static bool TryGetStairsId(string name, out byte id)
    {
        id = 0;
        switch (name)
        {
            case "oak_stairs":
            case "spruce_stairs":
            case "birch_stairs":
            case "jungle_stairs":
            case "acacia_stairs":
            case "dark_oak_stairs":
                id = 53;
                return true;
            case "cobblestone_stairs":
                id = 67;
                return true;
            case "brick_stairs":
                id = 108;
                return true;
            case "stone_brick_stairs":
                id = 109;
                return true;
            case "nether_brick_stairs":
                id = 114;
                return true;
            case "sandstone_stairs":
            case "red_sandstone_stairs":
                id = 128;
                return true;
            case "quartz_stairs":
            case "purpur_stairs":
                id = 156;
                return true;
            default:
                return false;
        }
    }

    private static byte MapStairsFacing(string facing)
    {
        return facing switch
        {
            "east" => 0,  // StairTile::DIR_EAST
            "west" => 1,  // StairTile::DIR_WEST
            "south" => 2, // StairTile::DIR_SOUTH
            "north" => 3, // StairTile::DIR_NORTH
            _ => 0,
        };
    }

    private static byte MapLadderFacing(string facing)
    {
        return facing switch
        {
            "north" => 2,
            "south" => 3,
            "west" => 4,
            "east" => 5,
            _ => 2,
        };
    }

    private static byte MapButtonFacing(string facing)
    {
        // ButtonTile_SPU::updateShape uses dir 1..4 for wall buttons.
        return facing switch
        {
            "east" => 1,
            "west" => 2,
            "south" => 3,
            "north" => 4,
            _ => 1,
        };
    }

    private static byte MapFenceGateFacing(string facing)
    {
        // Direction_SPU constants: SOUTH=0, WEST=1, NORTH=2, EAST=3.
        return facing switch
        {
            "south" => 0,
            "west" => 1,
            "north" => 2,
            "east" => 3,
            _ => 0,
        };
    }

    private static byte MapVineFaces(NbtCompound? properties)
    {
        byte data = 0;
        // VineTile_SPU flags are 1<<Direction where SOUTH=0, WEST=1, NORTH=2, EAST=3.
        if (GetBoolProperty(properties, "south")) data |= 1;
        if (GetBoolProperty(properties, "west")) data |= 2;
        if (GetBoolProperty(properties, "north")) data |= 4;
        if (GetBoolProperty(properties, "east")) data |= 8;
        return data;
    }

    private static byte MapLeverData(string face, string facing)
    {
        // Legacy lever data (low 3 bits) from TU19 renderer:
        // wall: 1..4, floor: 5/6, ceiling: 0/7. Bit 3 (value 8) is powered.
        return face switch
        {
            "floor" => (byte)((facing == "east" || facing == "west") ? 6 : 5),
            "ceiling" => (byte)((facing == "east" || facing == "west") ? 7 : 0),
            _ => MapButtonFacing(facing),
        };
    }

    private static byte MapDoorFacing(string facing)
    {
        // Legacy lower-door direction bits.
        return facing switch
        {
            "east" => 0,
            "south" => 1,
            "west" => 2,
            "north" => 3,
            _ => 0,
        };
    }

    private static byte MapTrapdoorFacing(string facing)
    {
        // TrapDoorTile::getPlacedOnFaceDataValue mapping.
        return facing switch
        {
            "north" => 0,
            "south" => 1,
            "west" => 2,
            "east" => 3,
            _ => 0,
        };
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

    private static bool TryMapFlattenedColoredBlock(string name, out LegacyBlockState block)
    {
        block = default;

        if (TrySplitColorPrefix(name, out byte colorData, out string suffix))
        {
            switch (suffix)
            {
                case "wool":
                    block = new LegacyBlockState(35, colorData);
                    return true;
                case "stained_glass":
                case "glass":
                    block = new LegacyBlockState(95, colorData);
                    return true;
                case "stained_glass_pane":
                case "glass_pane":
                    block = new LegacyBlockState(160, colorData);
                    return true;
                case "stained_hardened_clay":
                case "terracotta":
                    block = new LegacyBlockState(159, colorData);
                    return true;
                case "concrete":
                    block = new LegacyBlockState(172, colorData);
                    return true;
                case "concrete_powder":
                    block = new LegacyBlockState(12, colorData);
                    return true;
                case "glazed_terracotta":
                    block = new LegacyBlockState(159, colorData);
                    return true;
                case "carpet":
                    block = new LegacyBlockState(171, colorData);
                    return true;
            }
        }

        return false;
    }

    private static bool TrySplitColorPrefix(string name, out byte colorData, out string suffix)
    {
        colorData = 0;
        suffix = string.Empty;

        foreach (var colorName in ColorNames)
        {
            string prefix = colorName + "_";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            colorData = GetColorData(colorName);
            suffix = name[prefix.Length..];
            return true;
        }

        return false;
    }

    private static readonly string[] ColorNames =
    {
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "silver", "cyan", "purple", "blue", "brown", "green", "red", "black"
    };

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

        if (TryMapCommonModernFallback(name, properties, out block))
            return true;

        if (name == "redstone_lamp")
        {
            bool isLit = GetBoolProperty(properties, "lit");
            block = new LegacyBlockState(isLit ? (byte)124 : (byte)123, 0);
            return true;
        }

        switch (name)
        {
            case "deepslate":
            case "polished_deepslate":
            case "tuff":
            case "calcite":
            case "dripstone_block":
                block = new LegacyBlockState(1, 0);   // Stone
                return true;
            case "cobbled_deepslate":
                block = new LegacyBlockState(4, 0);   // Cobblestone
                return true;
            case "deepslate_bricks":
            case "deepslate_tiles":
                block = new LegacyBlockState(98, 0);  // Stone Bricks
                return true;
            case "cracked_deepslate_bricks":
            case "cracked_deepslate_tiles":
                block = new LegacyBlockState(98, 2);  // Cracked Stone Bricks
                return true;
            case "chiseled_deepslate":
                block = new LegacyBlockState(98, 3);  // Chiseled Stone Bricks
                return true;
            case "deepslate_coal_ore":
                block = new LegacyBlockState(16, 0);  // Coal Ore
                return true;
            case "deepslate_iron_ore":
                block = new LegacyBlockState(15, 0);  // Iron Ore
                return true;
            case "deepslate_copper_ore":
                block = new LegacyBlockState(15, 0);  // Closest legacy ore: Iron Ore
                return true;
            case "deepslate_gold_ore":
                block = new LegacyBlockState(14, 0);  // Gold Ore
                return true;
            case "deepslate_redstone_ore":
                block = new LegacyBlockState(73, 0);  // Redstone Ore
                return true;
            case "deepslate_lapis_ore":
                block = new LegacyBlockState(21, 0);  // Lapis Ore
                return true;
            case "deepslate_diamond_ore":
                block = new LegacyBlockState(56, 0);  // Diamond Ore
                return true;
            case "deepslate_emerald_ore":
                block = new LegacyBlockState(129, 0); // Emerald Ore
                return true;
            case "deepslate_tile_stairs":
            case "deepslate_brick_stairs":
            case "cobbled_deepslate_stairs":
                block = new LegacyBlockState(67, 0);  // Cobblestone Stairs
                return true;
            case "deepslate_tile_slab":
            case "deepslate_brick_slab":
            case "cobbled_deepslate_slab":
                block = new LegacyBlockState(44, 3);  // Cobblestone Slab
                return true;
            case "deepslate_tile_wall":
            case "deepslate_brick_wall":
            case "cobbled_deepslate_wall":
                block = new LegacyBlockState(139, 0); // Cobblestone Wall
                return true;
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
            case "smooth_sandstone":
                block = new LegacyBlockState(24, 2);
                return true;
            case "chiseled_sandstone":
            case "cut_sandstone":
                block = new LegacyBlockState(24, 1);
                return true;
            case "smooth_red_sandstone":
            case "chiseled_red_sandstone":
            case "cut_red_sandstone":
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

    private static bool TryMapCommonModernFallback(string name, NbtCompound? properties, out LegacyBlockState block)
    {
        block = default;

        if (name.EndsWith("_button", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(IsWoodFamilyName(name) ? (byte)143 : (byte)77, 0);
            return true;
        }

        if (name.EndsWith("_wall_sign", StringComparison.Ordinal))
        {
            byte data = MapWallSignFacing(GetProperty(properties, "facing"));
            block = new LegacyBlockState(68, data);
            return true;
        }

        if (name.EndsWith("_sign", StringComparison.Ordinal))
        {
            byte data = MapStandingSignRotation(GetIntProperty(properties, "rotation", 0));
            block = new LegacyBlockState(63, data);
            return true;
        }

        if (name.EndsWith("_wall", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(139, 0);
            return true;
        }

        if (name.EndsWith("_bed", StringComparison.Ordinal))
        {
            byte data = MapBedFacing(GetProperty(properties, "facing"));
            if (GetProperty(properties, "part") == "head")
                data |= 8;
            block = new LegacyBlockState(26, data);
            return true;
        }

        if (name.EndsWith("_banner", StringComparison.Ordinal))
        {
            block = new LegacyBlockState(0, 0);
            return true;
        }

        switch (name)
        {
            case "barrier":
            case "structure_void":
                block = new LegacyBlockState(0, 0);
                return true;
            case "bamboo":
                block = new LegacyBlockState(83, 0);
                return true;
            case "cake":
                block = new LegacyBlockState(92, 0);
                return true;
            case "brewing_stand":
                block = new LegacyBlockState(117, 0);
                return true;
            case "barrel":
                block = new LegacyBlockState(54, 0);
                return true;
            case "blast_furnace":
                block = new LegacyBlockState(61, 0);
                return true;
            case "campfire":
                block = new LegacyBlockState(50, 0);
                return true;
            case "azure_bluet":
            case "blue_orchid":
                block = new LegacyBlockState(38, 0);
                return true;
            case "attached_melon_stem":
                block = new LegacyBlockState(105, 7);
                return true;
            case "attached_pumpkin_stem":
                block = new LegacyBlockState(104, 7);
                return true;
            case "andesite":
            case "diorite":
            case "granite":
            case "polished_andesite":
            case "polished_diorite":
            case "polished_granite":
                block = new LegacyBlockState(1, 0);
                return true;
            case "andesite_stairs":
            case "diorite_stairs":
            case "granite_stairs":
            case "polished_andesite_stairs":
            case "polished_diorite_stairs":
            case "polished_granite_stairs":
                block = new LegacyBlockState(109, 0);
                return true;
            case "bubble_column":
                block = new LegacyBlockState(9, 0);
                return true;
            case "amethyst_block":
            case "budding_amethyst":
            case "amethyst_cluster":
                block = new LegacyBlockState(20, 0);
                return true;
        }

        return false;
    }

    private static void SanitizeLegacyItemStacks(NbtTag tag)
    {
        if (tag is NbtList list)
        {
            foreach (NbtTag child in list)
                SanitizeLegacyItemStacks(child);
            return;
        }

        if (tag is not NbtCompound compound)
            return;

        if (LooksLikeItemStack(compound))
            NormalizeOrClearItemStack(compound);

        foreach (NbtTag child in compound)
            SanitizeLegacyItemStacks(child);
    }

    private static void SanitizeEntities(NbtList entities)
    {
        for (int i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i] is not NbtCompound entity)
            {
                entities.RemoveAt(i);
                continue;
            }

            if (!IsItemEntity(entity))
                continue;

            NbtCompound? item = entity.Get<NbtCompound>("Item") ?? entity.Get<NbtCompound>("item");
            if (item == null)
            {
                // Item entities without an Item payload are known to crash TU19 loaders.
                entities.RemoveAt(i);
                continue;
            }

            item.Name = "Item";
            SetCompoundTag(entity, item);
            if (entity.Contains("item"))
                entity.Remove("item");

            NormalizeOrClearItemStack(item);

            short itemId = item.Get<NbtShort>("id")?.Value ?? 0;
            int count = item.Get<NbtByte>("Count")?.Value ?? 0;
            if (itemId <= 0 || count <= 0)
                entities.RemoveAt(i);
        }
    }

    private static void RemoveUnsupportedTileEntities(NbtList tileEntities)
    {
        for (int i = tileEntities.Count - 1; i >= 0; i--)
        {
            if (tileEntities[i] is not NbtCompound tileEntity)
            {
                tileEntities.RemoveAt(i);
                continue;
            }

            string id = tileEntity.Get<NbtString>("id")?.Value ?? string.Empty;
            if (id is "Control" or "minecraft:command_block" or "CommandBlock")
                tileEntities.RemoveAt(i);
        }
    }

    private static bool IsItemEntity(NbtCompound entity)
    {
        string id = entity.Get<NbtString>("id")?.Value ?? string.Empty;
        return id is "Item" or "item" or "minecraft:item";
    }

    private static bool LooksLikeItemStack(NbtCompound compound)
    {
        return compound.Contains("id") &&
               (compound.Contains("Count") || compound.Contains("Slot") || compound.Contains("Damage"));
    }

    private static void NormalizeOrClearItemStack(NbtCompound stack)
    {
        int id;
        if (!TryReadLegacyItemId(stack, out id) || id < 0 || id > 31999)
        {
            SetCompoundTag(stack, new NbtShort("id", 0));
            SetCompoundTag(stack, new NbtByte("Count", 0));
            SetCompoundTag(stack, new NbtShort("Damage", 0));
            return;
        }

        SetCompoundTag(stack, new NbtShort("id", (short)Math.Clamp(id, 0, short.MaxValue)));

        int count = stack.Get<NbtByte>("Count")?.Value ?? 1;
        SetCompoundTag(stack, new NbtByte("Count", (byte)Math.Clamp(count, 0, 64)));

        short damage = stack.Get<NbtShort>("Damage")?.Value
            ?? (short)Math.Clamp(stack.Get<NbtInt>("Damage")?.Value ?? 0, short.MinValue, short.MaxValue);
        SetCompoundTag(stack, new NbtShort("Damage", damage));
    }

    private static bool TryReadLegacyItemId(NbtCompound stack, out int id)
    {
        id = 0;

        if (!stack.Contains("id"))
            return false;

        NbtTag idTag = stack["id"]!;
        switch (idTag)
        {
            case NbtByte b:
                id = b.Value;
                return true;
            case NbtShort s:
                id = s.Value;
                return true;
            case NbtInt i:
                id = i.Value;
                return true;
            case NbtString str:
                return TryMapModernItemNameToLegacyId(str.Value, out id);
            default:
                return false;
        }
    }

    private static bool TryMapModernItemNameToLegacyId(string name, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.StartsWith("minecraft:", StringComparison.Ordinal))
            name = name[10..];

        return name switch
        {
            "air" => true,
            "stone" => (id = 1) == 1,
            "grass" => (id = 2) == 2,
            "dirt" => (id = 3) == 3,
            "cobblestone" => (id = 4) == 4,
            "planks" => (id = 5) == 5,
            "sand" => (id = 12) == 12,
            "gravel" => (id = 13) == 13,
            "coal" => (id = 263) == 263,
            "iron_ingot" => (id = 265) == 265,
            "gold_ingot" => (id = 266) == 266,
            "diamond" => (id = 264) == 264,
            "stick" => (id = 280) == 280,
            "torch" => (id = 50) == 50,
            _ => false,
        };
    }

    private static void SetCompoundTag(NbtCompound compound, NbtTag tag)
    {
        string? tagName = tag.Name;
        if (string.IsNullOrEmpty(tagName))
            return;

        if (compound.Contains(tagName))
            compound.Remove(tagName);
        compound.Add(tag);
    }

    private static bool IsWoodFamilyName(string name)
    {
        return name.Contains("oak", StringComparison.Ordinal)
            || name.Contains("spruce", StringComparison.Ordinal)
            || name.Contains("birch", StringComparison.Ordinal)
            || name.Contains("jungle", StringComparison.Ordinal)
            || name.Contains("acacia", StringComparison.Ordinal)
            || name.Contains("dark_oak", StringComparison.Ordinal)
            || name.Contains("mangrove", StringComparison.Ordinal)
            || name.Contains("cherry", StringComparison.Ordinal)
            || name.Contains("bamboo", StringComparison.Ordinal)
            || name.Contains("crimson", StringComparison.Ordinal)
            || name.Contains("warped", StringComparison.Ordinal);
    }

    private static byte MapBedFacing(string facing)
    {
        return facing switch
        {
            "south" => 0,
            "west" => 1,
            "north" => 2,
            "east" => 3,
            _ => 0,
        };
    }

    private static byte MapWallSignFacing(string facing)
    {
        return facing switch
        {
            "north" => 2,
            "south" => 3,
            "west" => 4,
            "east" => 5,
            _ => 2,
        };
    }

    private static byte MapStandingSignRotation(int rotation)
    {
        return (byte)(rotation & 0x0F);
    }

    private static string GetProperty(NbtCompound? properties, string name)
    {
        return properties?.Get<NbtString>(name)?.Value ?? string.Empty;
    }

    private static bool GetBoolProperty(NbtCompound? properties, string name)
    {
        return string.Equals(GetProperty(properties, name), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetIntProperty(NbtCompound? properties, string name, int defaultValue)
    {
        string value = GetProperty(properties, name);
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
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
    /// Remaps post-1.6.4 Java IDs and then sanitizes every block ID to the IDs that exist in LCE TU19.
    /// This mirrors BlockReplacements::replace() behavior in the game source to avoid null Tile dereferences.
    /// Valid LCE tile IDs are 0..160 and 170..173 (Tile.h constants).
    /// </summary>
    private static void RemapBlocks(byte[] blocks, byte[] data)
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            byte id = blocks[i];
            if (id == 137)
                id = 1;

            if (id >= 165 && BlockRemapTable.TryGetValue(id, out byte replacement))
                id = replacement;

            if (!IsValidLceTileId(id))
                id = 0;

            blocks[i] = id;
        }
    }

    private static bool IsValidLceTileId(byte id)
    {
        return id <= 160 || (id >= 170 && id <= 173);
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
        ["command_block"] = new LegacyBlockState(1, 0),
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
        { 210, 1   },  // Repeating Command Block → Stone
        { 211, 1   },  // Chain Command Block → Stone
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
