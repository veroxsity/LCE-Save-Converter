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

    public static NbtCompound BuildModernAnvilLevel(ConversionOptions options, NbtCompound legacyLevel, int chunkX, int chunkZ)
    {
        byte[] oldBlocks = legacyLevel.Get<NbtByteArray>("Blocks")?.Value ?? new byte[32768];
        byte[] oldData = legacyLevel.Get<NbtByteArray>("Data")?.Value ?? new byte[16384];

        var blockEntities = legacyLevel.Get<NbtList>("TileEntities")?.Clone() as NbtList ?? new NbtList("block_entities", NbtTagType.Compound);
        blockEntities.Name = "block_entities";
        var entities = legacyLevel.Get<NbtList>("Entities")?.Clone() as NbtList ?? new NbtList("Entities", NbtTagType.Compound);
        entities.Name = "entities";

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

                        string modernState = GetModernBlockName(blockId, meta);
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
        root.Add(sections);
        root.Add(blockEntities);
        root.Add(entities);

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