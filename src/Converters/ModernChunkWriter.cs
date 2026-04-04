using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using fNbt;

namespace LceWorldConverter;

public static class ModernChunkWriter
{
    private static readonly Dictionary<string, string> _legacyToModernMap = new();
    private static bool _isMapLoaded = false;

    private static void EnsureMapLoaded()
    {
        if (_isMapLoaded) return;

        string mapPath = Path.Combine(AppContext.BaseDirectory, "Resources", "legacy_to_modern_block_mapping.json");
        if (File.Exists(mapPath))
        {
            string json = File.ReadAllText(mapPath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map != null)
            {
                foreach (var kvp in map)
                {
                    _legacyToModernMap[kvp.Key] = kvp.Value;
                }
            }
        }
        _isMapLoaded = true;
    }

    public static string GetModernBlockName(byte id, byte meta)
    {
        EnsureMapLoaded();
        string key = $"{id}:{meta}";
        if (_legacyToModernMap.TryGetValue(key, out string? modernName))
        {
            return modernName;
        }
        
        // Fallback or generic rule mapping
        if (_legacyToModernMap.TryGetValue($"{id}:0", out modernName))
        {
            return modernName;
        }

        return "minecraft:air";
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
        if (legacyLevel.TryGet<NbtList>("TileEntities", out var oldTeList))
        {
            foreach (NbtCompound oldTe in oldTeList)
            {
                var newTe = (NbtCompound)oldTe.Clone();
                if (newTe.TryGet<NbtString>("id", out var idTag))
                {
                    string id = idTag.Value;
                    string newId = null;
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

                    if (newId != null) 
                    {
                        newTe["id"] = new NbtString("id", newId);
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
}