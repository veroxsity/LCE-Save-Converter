using System;
using System.IO;
using fNbt;

namespace LceWorldConverter;

public static class ModernChunkWriter
{
    private static readonly LegacyMappingProvider MappingProvider = new();

    public static string GetModernItemName(short id)
    {
        return MappingProvider.GetModernItemName(id);
    }

    public static string GetModernBlockName(byte id, byte meta)
    {
        return MappingProvider.GetModernBlockName(id, meta);
    }

    private static string GetContextualBlockState(byte blockId, byte meta, int x, int globalY, int z, byte[] oldBlocks, byte[] oldData)
    {
        string modernState = GetModernBlockName(blockId, meta);

        (byte neighborId, byte neighborMeta) GetNeighbor(int nx, int ny, int nz)
        {
            if (nx < 0 || nx > 15 || ny < 0 || ny > 127 || nz < 0 || nz > 15) return (0, 0);
            int idx = ((nx * 16) + nz) * 128 + ny;
            byte nId = oldBlocks[idx];
            byte nMeta = GetNibble(oldData, idx);
            return (nId, nMeta);
        }

        int bracketIndex = modernState.IndexOf('[');
        string name = bracketIndex != -1 ? modernState.Substring(0, bracketIndex) : modernState;
        var properties = new Dictionary<string, string>();
        if (bracketIndex != -1)
        {
            string propsStr = modernState.Substring(bracketIndex + 1, modernState.Length - bracketIndex - 2);
            foreach (var prop in propsStr.Split(','))
            {
                var kv = prop.Split('=');
                if (kv.Length == 2) properties[kv[0]] = kv[1];
            }
        }

        bool propertiesChanged = false;

        // Doors
        if (blockId == 64 || blockId == 71 || (blockId >= 193 && blockId <= 197))
        {
            bool isTop = (meta & 8) == 8;
            properties["half"] = isTop ? "upper" : "lower";
            
            if (isTop)
            {
                properties["hinge"] = (meta & 1) == 1 ? "right" : "left";
                var (bottomId, bottomMeta) = GetNeighbor(x, globalY - 1, z);
                if (bottomId == blockId)
                {
                    int facingMeta = bottomMeta & 3;
                    properties["facing"] = facingMeta switch { 0 => "east", 1 => "south", 2 => "west", 3 => "north", _ => "east" };
                    properties["open"] = (bottomMeta & 4) == 4 ? "true" : "false";
                }
                else
                {
                    properties["facing"] = "east";
                    properties["open"] = "false";
                }
            }
            else
            {
                int facingMeta = meta & 3;
                properties["facing"] = facingMeta switch { 0 => "east", 1 => "south", 2 => "west", 3 => "north", _ => "east" };
                properties["open"] = (meta & 4) == 4 ? "true" : "false";
                var (topId, topMeta) = GetNeighbor(x, globalY + 1, z);
                if (topId == blockId)
                {
                    properties["hinge"] = (topMeta & 1) == 1 ? "right" : "left";
                }
                else
                {
                    properties["hinge"] = "left";
                }
            }
            propertiesChanged = true;
        }
        // Signs
        else if (blockId == 63 || blockId == 68)
        {
            if (blockId == 68) {
                name = "minecraft:oak_wall_sign";
                properties["facing"] = meta == 2 ? "north" : meta == 3 ? "south" : meta == 4 ? "west" : "east";
            } else {
                name = "minecraft:oak_sign";
                properties["rotation"] = meta.ToString();
            }
            properties.Remove("waterlogged");
            propertiesChanged = true;
        }
        // Torches (50, 75 unlit RS, 76 lit RS)
        else if (blockId == 50 || blockId == 75 || blockId == 76)
        {
            if (meta >= 1 && meta <= 4)
            {
                name = blockId == 50 ? "minecraft:wall_torch" : (blockId == 76 ? "minecraft:redstone_wall_torch" : "minecraft:unlit_redstone_wall_torch");
                properties["facing"] = meta == 1 ? "east" : meta == 2 ? "west" : meta == 3 ? "south" : "north";
            }
            else // Floor
            {
                name = blockId == 50 ? "minecraft:torch" : (blockId == 76 ? "minecraft:redstone_torch" : "minecraft:unlit_redstone_torch");
                properties.Remove("facing");
                properties.Remove("floor");
            }
            
            if (blockId == 76) properties["lit"] = "true";
            else if (blockId == 75) properties["lit"] = "false";
            propertiesChanged = true;
        }
        // Chests
        else if (blockId == 54 || blockId == 146) // 54 = Chest, 146 = Trapped Chest
        {
            name = blockId == 54 ? "minecraft:chest" : "minecraft:trapped_chest";
            properties["type"] = "single";

            string facing = meta == 2 ? "north" :
                            meta == 3 ? "south" :
                            meta == 4 ? "west" :
                            meta == 5 ? "east" : "south";
            properties["facing"] = facing;

            (int dx, int dz)[] dirs = { (-1, 0), (1, 0), (0, -1), (0, 1) }; // west, east, north, south
            foreach (var (dx, dz) in dirs)
            {
                var (nid, _) = GetNeighbor(x + dx, globalY, z + dz);
                if (nid == blockId)
                {
                    bool isLeft = false;

                    if (facing == "north") {
                        if (dx == -1) isLeft = true; // Neighbor is West (Right). We are Left!
                    }
                    else if (facing == "south") {
                        if (dx == 1) isLeft = true; // Neighbor is East (Right). We are Left!
                    }
                    else if (facing == "west") {
                        if (dz == 1) isLeft = true; // Neighbor is South (Right). We are Left!
                    }
                    else if (facing == "east") {
                        if (dz == -1) isLeft = true; // Neighbor is North (Right). We are Left!
                    }

                    properties["type"] = isLeft ? "right" : "left";
                    break;
                }
            }
            propertiesChanged = true;
        }
        // Stairs
        else if (IsStairLegacy(blockId))
        {
            string facing = GetStairFacing(meta);
            string half = (meta & StairUpsideDownBit) != 0 ? "top" : "bottom";

            properties["facing"] = facing;
            properties["half"] = half;
            properties["shape"] = "straight";

            (int dx, int dz) frontOffset = facing switch
            {
                "north" => (0, 1),
                "south" => (0, -1),
                "west" => (1, 0),
                "east" => (-1, 0),
                _ => (0, 0),
            };

            (int dx, int dz) backOffset = facing switch
            {
                "north" => (0, -1),
                "south" => (0, 1),
                "west" => (-1, 0),
                "east" => (1, 0),
                _ => (0, 0),
            };

            if (TryGetLegacyStairState(GetNeighbor(x + backOffset.dx, globalY, z + backOffset.dz), out string backFacing, out string backHalf) && backHalf == half)
            {
                if (TryGetRelativeSide(facing, backFacing, out string backSide))
                {
                    properties["shape"] = $"outer_{backSide}";
                }
            }
            else if (TryGetLegacyStairState(GetNeighbor(x + frontOffset.dx, globalY, z + frontOffset.dz), out string frontFacing, out string frontHalf) && frontHalf == half)
            {
                if (TryGetRelativeSide(facing, frontFacing, out string frontSide))
                {
                    properties["shape"] = $"inner_{frontSide}";
                }
            }

            propertiesChanged = true;
        }
        // Fences
        else if (IsFenceLegacy(blockId))
        {
            bool north = false;
            bool east = false;
            bool south = false;
            bool west = false;

            var (nId, _) = GetNeighbor(x, globalY, z - 1);
            var (eId, _) = GetNeighbor(x + 1, globalY, z);
            var (sId, _) = GetNeighbor(x, globalY, z + 1);
            var (wId, _) = GetNeighbor(x - 1, globalY, z);

            north = IsFenceLegacy(nId) || IsFenceGateLegacy(nId) || IsLikelySolidAttachment(nId);
            east  = IsFenceLegacy(eId) || IsFenceGateLegacy(eId) || IsLikelySolidAttachment(eId);
            south = IsFenceLegacy(sId) || IsFenceGateLegacy(sId) || IsLikelySolidAttachment(sId);
            west  = IsFenceLegacy(wId) || IsFenceGateLegacy(wId) || IsLikelySolidAttachment(wId);

            properties["north"] = north ? "true" : "false";
            properties["east"] = east ? "true" : "false";
            properties["south"] = south ? "true" : "false";
            properties["west"] = west ? "true" : "false";
            propertiesChanged = true;
        }
        // Panes / Iron Bars
        else if (IsPaneLegacy(blockId))
        {
            bool north = false;
            bool east = false;
            bool south = false;
            bool west = false;

            var (nId, _) = GetNeighbor(x, globalY, z - 1);
            var (eId, _) = GetNeighbor(x + 1, globalY, z);
            var (sId, _) = GetNeighbor(x, globalY, z + 1);
            var (wId, _) = GetNeighbor(x - 1, globalY, z);

            north = IsPaneLegacy(nId) || IsGlassLegacy(nId) || IsLikelySolidAttachment(nId);
            east  = IsPaneLegacy(eId) || IsGlassLegacy(eId) || IsLikelySolidAttachment(eId);
            south = IsPaneLegacy(sId) || IsGlassLegacy(sId) || IsLikelySolidAttachment(sId);
            west  = IsPaneLegacy(wId) || IsGlassLegacy(wId) || IsLikelySolidAttachment(wId);

            properties["north"] = north ? "true" : "false";
            properties["east"] = east ? "true" : "false";
            properties["south"] = south ? "true" : "false";
            properties["west"] = west ? "true" : "false";
            propertiesChanged = true;
        }

        if (propertiesChanged || name == "minecraft:red_bed")
        {
            if (properties.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kvp in properties) parts.Add($"{kvp.Key}={kvp.Value}");
                return $"{name}[{string.Join(",", parts)}]";
            }
            return name;
        }

        return modernState;
    }

    public static NbtCompound BuildModernAnvilLevel(ConversionOptions options, NbtCompound legacyLevel, int chunkX, int chunkZ)
    {
        byte[] oldBlocks = legacyLevel.Get<NbtByteArray>("Blocks")?.Value ?? new byte[32768];
        byte[] oldData = legacyLevel.Get<NbtByteArray>("Data")?.Value ?? new byte[16384];

        var sections = new NbtList("sections", NbtTagType.Compound);
        for (int sectionY = 0; sectionY < 8; sectionY++)
        {
            var paletteList = new List<string>();
            var paletteDict = new Dictionary<string, int>();
            int[] indices = new int[4096];

            bool hasNonAir = false;

            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int globalY = sectionY * 16 + y;
                        int legacyFlatIndex = ((x * 16) + z) * 128 + globalY;
                        
                        byte blockId = oldBlocks[legacyFlatIndex];
                        byte meta = GetNibble(oldData, legacyFlatIndex);

                        string modernState = GetContextualBlockState(blockId, meta, x, globalY, z, oldBlocks, oldData);
                        if (modernState != "minecraft:air") hasNonAir = true;

                        if (!paletteDict.TryGetValue(modernState, out int paletteIndex))
                        {
                            paletteIndex = paletteList.Count;
                            paletteDict[modernState] = paletteIndex;
                            paletteList.Add(modernState);
                        }

                        indices[y * 256 + z * 16 + x] = paletteIndex;
                    }
                }
            }

            if (!hasNonAir && sectionY > 0) continue; // Skip empty sections above Y=0, though usually we want to keep them if below terrain.

            var sectionTag = new NbtCompound();
            sectionTag.Add(new NbtByte("Y", (byte)sectionY));

            var blockStatesTag = new NbtCompound("block_states");
            var nbtPalette = new NbtList("palette", NbtTagType.Compound);
            foreach (var state in paletteList)
            {
                var pTag = new NbtCompound();
                // Extremely simple parsing, normally we'd separate Name and Properties
                int bracketIndex = state.IndexOf('[');
                if (bracketIndex != -1)
                {
                    string name = state.Substring(0, bracketIndex);
                    string propsStr = state.Substring(bracketIndex + 1, state.Length - bracketIndex - 2);
                    pTag.Add(new NbtString("Name", name));
                    
                    var propsCompound = new NbtCompound("Properties");
                    foreach (var prop in propsStr.Split(','))
                    {
                        var kv = prop.Split('=');
                        if (kv.Length == 2) propsCompound.Add(new NbtString(kv[0], kv[1]));
                    }
                    pTag.Add(propsCompound);
                }
                else
                {
                    pTag.Add(new NbtString("Name", state));
                }
                nbtPalette.Add(pTag);
            }
            blockStatesTag.Add(nbtPalette);

            if (paletteList.Count > 1)
            {
                int bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(paletteList.Count)));
                int valuesPerLong = 64 / bitsPerBlock;
                long[] dataArray = new long[(int)Math.Ceiling(4096.0 / valuesPerLong)];
                
                long currentLong = 0;
                int blocksInCurrentLong = 0;
                int longIndex = 0;

                for (int i = 0; i < 4096; i++)
                {
                    long val = indices[i];
                    currentLong |= (val << (blocksInCurrentLong * bitsPerBlock));
                    blocksInCurrentLong++;

                    if (blocksInCurrentLong >= valuesPerLong)
                    {
                        dataArray[longIndex++] = currentLong;
                        currentLong = 0;
                        blocksInCurrentLong = 0;
                    }
                }
                
                if (blocksInCurrentLong > 0)
                {
                    dataArray[longIndex] = currentLong;
                }
                
                blockStatesTag.Add(new NbtLongArray("data", dataArray));
            }

            sectionTag.Add(blockStatesTag);
            
            // Add mock Biomes
            var biomesTag = new NbtCompound("biomes");
            var biomePalette = new NbtList("palette", NbtTagType.String) { new NbtString("minecraft:plains") };
            biomesTag.Add(biomePalette);
            sectionTag.Add(biomesTag);

            sections.Add(sectionTag);
        }

        var root = new NbtCompound("");
        root.Add(new NbtInt("xPos", chunkX));
        root.Add(new NbtInt("zPos", chunkZ));
        root.Add(new NbtInt("yPos", 0));
        root.Add(new NbtString("Status", "full"));
        var blockEntities = new NbtList("block_entities", NbtTagType.Compound);
        if (legacyLevel.TryGet<NbtList>("TileEntities", out NbtList? oldTeList) && oldTeList != null)
        {
            foreach (NbtCompound oldTe in oldTeList)
            {
                var newTe = (NbtCompound)oldTe.Clone();
                if (newTe.TryGet<NbtString>("id", out NbtString? idTag) && idTag != null)
                {
                    string id = idTag.Value;
                    string? newId = null;
                    if (id == "Chest") newId = "minecraft:chest";
                    else if (id == "Furnace") newId = "minecraft:furnace";
                    else if (id == "BrewingStand") newId = "minecraft:brewing_stand";
                    else if (id == "EnchantTable") newId = "minecraft:enchanting_table";
                    else if (id == "Trap") newId = "minecraft:dispenser";
                    else if (id == "MobSpawner") newId = "minecraft:mob_spawner";
                    else if (id == "Control") newId = "minecraft:command_block";
                    else if (id == "Beacon") newId = "minecraft:beacon";
                    else if (id == "Skull") newId = "minecraft:skull";
                    else if (id == "Sign") {
                        newId = "minecraft:sign";
                        // Basic modern sign structure
                        var frontText = new NbtCompound("front_text");
                        var messages = new NbtList("messages", NbtTagType.String);
                        for (int i = 1; i <= 4; i++) {
                            messages.Add(new NbtString($@"{{""text"":""{newTe.Get<NbtString>($"Text{i}")?.Value ?? ""}""}}"));
                        }
                        frontText.Add(messages);
                        newTe.Add(frontText);
                    }
                    else if (id == "Cauldron") newId = "minecraft:cauldron";
                    else if (id == "Dropper") newId = "minecraft:dropper";
                    else if (id == "Hopper") newId = "minecraft:hopper";
                    else if (id == "Comparator") newId = "minecraft:comparator";
                    else if (id == "RecordPlayer") newId = "minecraft:jukebox";
                    else if (id == "Banner") newId = "minecraft:banner";

                    // Preserve already-modern IDs from trailing/dynamic NBT payloads.
                    if (newId == null && id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                        newId = id;

                    if (newId != null)
                    {
                        newTe["id"] = new NbtString("id", newId);
                        ChunkConverter.SanitizeLegacyItemStacks(newTe, options.TargetVersion);
                        blockEntities.Add(newTe);
                    }
                }
            }
        }
        for (int i = 0; i < 32768; i++)
        {
            if (oldBlocks[i] == 26)
            {
                int y = i % 128;
                int z = (i / 128) % 16;
                int x = i / 2048;
                var bedTe = new NbtCompound();
                bedTe.Add(new NbtString("id", "minecraft:bed"));
                bedTe.Add(new NbtInt("color", 14));
                bedTe.Add(new NbtInt("x", (chunkX * 16) + x));
                bedTe.Add(new NbtInt("y", y));
                bedTe.Add(new NbtInt("z", (chunkZ * 16) + z));
                blockEntities.Add(bedTe);
            }
        }

        // LCE double-chest inventory ownership is mirrored for north/east facings.
        // Keep blockstate orientation logic intact and only swap TE payload ownership
        // for those specific facings so GUI top/bottom rows match visual left/right.
        ReconcileDoubleChestInventories(blockEntities, oldBlocks, oldData);

        root.Add(blockEntities);
        root.Add(sections);

        return root;
    }

    private static byte GetNibble(byte[] data, int index)
    {
        int byteIndex = index / 2;
        if (byteIndex >= data.Length) return 0;

        if ((index & 1) == 0)
            return (byte)(data[byteIndex] & 0x0F);
        else
            return (byte)((data[byteIndex] >> 4) & 0x0F);
    }

    private static bool IsFenceLegacy(byte id)
    {
        return id == 85 || id == 113 || id == 188 || id == 189 || id == 190 || id == 191 || id == 192;
    }

    private const byte StairUpsideDownBit = 4;

    private static bool IsStairLegacy(byte id)
    {
        return id == 53 || id == 67 || id == 108 || id == 109 || id == 114 || id == 128 || id == 134 || id == 135 || id == 136 || id == 156 || id == 163 || id == 164 || id == 180;
    }

    private static string GetStairFacing(byte meta)
    {
        return (meta & 0x3) switch
        {
            0 => "east",
            1 => "west",
            2 => "south",
            3 => "north",
            _ => "north",
        };
    }

    private static bool TryGetRelativeSide(string currentFacing, string neighborFacing, out string side)
    {
        side = string.Empty;

        if (currentFacing == "north")
        {
            if (neighborFacing == "west") { side = "left"; return true; }
            if (neighborFacing == "east") { side = "right"; return true; }
        }
        else if (currentFacing == "south")
        {
            if (neighborFacing == "east") { side = "left"; return true; }
            if (neighborFacing == "west") { side = "right"; return true; }
        }
        else if (currentFacing == "west")
        {
            if (neighborFacing == "south") { side = "left"; return true; }
            if (neighborFacing == "north") { side = "right"; return true; }
        }
        else if (currentFacing == "east")
        {
            if (neighborFacing == "north") { side = "left"; return true; }
            if (neighborFacing == "south") { side = "right"; return true; }
        }

        return false;
    }

    private static string GetOppositeSide(string side)
    {
        return side == "left" ? "right" : "left";
    }

    private static bool TryGetLegacyStairState((byte neighborId, byte neighborMeta) neighbor, out string facing, out string half)
    {
        facing = string.Empty;
        half = string.Empty;

        if (!IsStairLegacy(neighbor.neighborId))
            return false;

        facing = GetStairFacing(neighbor.neighborMeta);
        half = (neighbor.neighborMeta & StairUpsideDownBit) != 0 ? "top" : "bottom";
        return true;
    }

    private static bool IsFenceGateLegacy(byte id)
    {
        return id == 107 || id == 183 || id == 184 || id == 185 || id == 186 || id == 187;
    }

    private static bool IsPaneLegacy(byte id)
    {
        return id == 101 || id == 102 || id == 160;
    }

    private static bool IsGlassLegacy(byte id)
    {
        return id == 20 || id == 95;
    }

    private static bool IsLikelySolidAttachment(byte id)
    {
        if (id == 0) return false;
        if (id >= 8 && id <= 11) return false;

        return id switch
        {
            6 or 27 or 28 or 30 or 31 or 32 or 37 or 38 or 39 or 40 or 50 or 51 or 55 or 59 or 63 or 65 or 66 or 68 or
            69 or 70 or 71 or 72 or 75 or 76 or 77 or 78 or 83 or 90 or 92 or 93 or 94 or 96 or 101 or 102 or 104 or 105 or
            106 or 107 or 111 or 115 or 119 or 127 or 131 or 132 or 141 or 142 or 143 or 147 or 148 or 149 or 150 or 157 or
            160 or 171 or 175 => false,
            _ => true,
        };
    }

    private static void ReconcileDoubleChestInventories(NbtList blockEntities, byte[] oldBlocks, byte[] oldData)
    {
        var byPos = new Dictionary<(int x, int y, int z), NbtCompound>();
        foreach (NbtTag tag in blockEntities)
        {
            if (tag is not NbtCompound te)
                continue;

            string id = te.Get<NbtString>("id")?.Value ?? string.Empty;
            if (id != "minecraft:chest" && id != "minecraft:trapped_chest")
                continue;

            if (!TryGetTePos(te, out int x, out int y, out int z))
                continue;

            byPos[(x, y, z)] = te;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in byPos)
        {
            int x = kvp.Key.x;
            int y = kvp.Key.y;
            int z = kvp.Key.z;
            NbtCompound te = kvp.Value;

            if (!TryGetLegacyChestFacing(oldBlocks, oldData, x, y, z, out string facing))
                continue;

            if (facing != "north" && facing != "east")
                continue;

            (int nx, int ny, int nz)? neighbor = null;
            if (facing == "north" || facing == "south")
            {
                if (byPos.ContainsKey((x - 1, y, z))) neighbor = (x - 1, y, z);
                else if (byPos.ContainsKey((x + 1, y, z))) neighbor = (x + 1, y, z);
            }
            else
            {
                if (byPos.ContainsKey((x, y, z - 1))) neighbor = (x, y, z - 1);
                else if (byPos.ContainsKey((x, y, z + 1))) neighbor = (x, y, z + 1);
            }

            if (neighbor == null)
                continue;

            int ax = x;
            int ay = y;
            int az = z;
            int bx = neighbor.Value.nx;
            int by = neighbor.Value.ny;
            int bz = neighbor.Value.nz;
            string pairKey = ax < bx || (ax == bx && (ay < by || (ay == by && az < bz)))
                ? $"{ax},{ay},{az}|{bx},{by},{bz}"
                : $"{bx},{by},{bz}|{ax},{ay},{az}";

            if (!visited.Add(pairKey))
                continue;

            NbtCompound other = byPos[(bx, by, bz)];
            NbtList? aItems = GetChestItemsList(te);
            NbtList? bItems = GetChestItemsList(other);

            if (aItems == null && bItems == null)
                continue;

            SetChestItemsList(te, bItems);
            SetChestItemsList(other, aItems);
        }
    }

    private static bool TryGetTePos(NbtCompound te, out int x, out int y, out int z)
    {
        x = te.Get<NbtInt>("x")?.Value ?? int.MinValue;
        y = te.Get<NbtInt>("y")?.Value ?? int.MinValue;
        z = te.Get<NbtInt>("z")?.Value ?? int.MinValue;
        return x != int.MinValue && y != int.MinValue && z != int.MinValue;
    }

    private static bool TryGetLegacyChestFacing(byte[] oldBlocks, byte[] oldData, int worldX, int y, int worldZ, out string facing)
    {
        facing = string.Empty;
        if (y < 0 || y > 127)
            return false;

        int lx = ((worldX % 16) + 16) % 16;
        int lz = ((worldZ % 16) + 16) % 16;
        int index = ((lx * 16) + lz) * 128 + y;
        if (index < 0 || index >= oldBlocks.Length)
            return false;

        byte id = oldBlocks[index];
        if (id != 54 && id != 146)
            return false;

        byte meta = GetNibble(oldData, index);
        facing = meta switch
        {
            2 => "north",
            3 => "south",
            4 => "west",
            5 => "east",
            _ => string.Empty,
        };

        return facing.Length > 0;
    }

    private static NbtList? GetChestItemsList(NbtCompound te)
    {
        return te.Get<NbtList>("Items") ?? te.Get<NbtList>("items");
    }

    private static void SetChestItemsList(NbtCompound te, NbtList? items)
    {
        te.Remove("Items");
        te.Remove("items");
        if (items != null)
        {
            var cloned = (NbtList)items.Clone();
            cloned.Name = "Items";
            te.Add(cloned);
        }
    }
}
