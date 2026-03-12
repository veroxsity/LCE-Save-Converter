using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Converts a Java Edition level.dat to LCE format.
/// Based on LevelData::setTagData() from the LCE TU19 source.
///
/// Copies all standard Java fields and adds LCE-specific fields:
///   XZSize, HellScale, newSeaLevel, hasBeenInCreative, spawnBonusChest,
///   hasStronghold, StrongholdX/Y/Z, hasStrongholdEndPortal,
///   StrongholdEndPortalX/Z
///
/// Also adjusts spawn coordinates for the world recentring.
/// </summary>
public static class LevelDatConverter
{
    /// <summary>
    /// Reads a Java level.dat and produces an LCE-compatible level.dat as bytes.
    /// spawnChunkX/Z are the Java spawn chunk coords used for recentring.
    /// </summary>
    public static byte[] Convert(NbtCompound javaRoot, int spawnChunkX, int spawnChunkZ, bool largeWorld)
    {
        var javaData = javaRoot.Get<NbtCompound>("Data");
        if (javaData == null)
            throw new InvalidOperationException("Java level.dat missing 'Data' compound tag");

        int xzSize = largeWorld ? 320 : 54;
        int hellScale = 3;

        // Read original spawn
        int spawnX = javaData.Get<NbtInt>("SpawnX")?.Value ?? 0;
        int spawnY = javaData.Get<NbtInt>("SpawnY")?.Value ?? 64;
        int spawnZ = javaData.Get<NbtInt>("SpawnZ")?.Value ?? 0;

        // Recentre spawn so it's relative to chunk (0,0)
        int newSpawnX = spawnX - (spawnChunkX * 16);
        int newSpawnZ = spawnZ - (spawnChunkZ * 16);

        // Build the LCE level.dat
        var lceData = new NbtCompound("Data")
        {
            // Standard Java fields
            new NbtLong("RandomSeed", GetLong(javaData, "RandomSeed")),
            new NbtString("generatorName", GetString(javaData, "generatorName", "default")),
            new NbtInt("generatorVersion", GetInt(javaData, "generatorVersion")),
            new NbtString("generatorOptions", GetString(javaData, "generatorOptions", "")),
            new NbtInt("GameType", GetInt(javaData, "GameType")),
            new NbtByte("MapFeatures", GetBool(javaData, "MapFeatures", true)),
            new NbtInt("SpawnX", newSpawnX),
            new NbtInt("SpawnY", spawnY),
            new NbtInt("SpawnZ", newSpawnZ),
            new NbtLong("Time", GetLong(javaData, "Time")),
            new NbtLong("DayTime", GetLong(javaData, "DayTime")),
            new NbtLong("SizeOnDisk", 0),
            new NbtLong("LastPlayed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            new NbtString("LevelName", GetString(javaData, "LevelName", "Converted World")),
            new NbtInt("version", 19133),
            new NbtInt("rainTime", GetInt(javaData, "rainTime")),
            new NbtByte("raining", GetBool(javaData, "raining", false)),
            new NbtInt("thunderTime", GetInt(javaData, "thunderTime")),
            new NbtByte("thundering", GetBool(javaData, "thundering", false)),
            new NbtByte("hardcore", GetBool(javaData, "hardcore", false)),
            new NbtByte("allowCommands", GetBool(javaData, "allowCommands", false)),
            new NbtByte("initialized", GetBool(javaData, "initialized", true)),

            // LCE-specific fields (from LevelData::setTagData)
            new NbtByte("newSeaLevel", 1),
            new NbtByte("hasBeenInCreative", GetBool(javaData, "hasBeenInCreative", false)),
            new NbtByte("spawnBonusChest", GetBool(javaData, "spawnBonusChest", false)),

            // Stronghold — let LCE locate on first load
            new NbtByte("hasStronghold", 0),
            new NbtInt("StrongholdX", 0),
            new NbtInt("StrongholdY", 0),
            new NbtInt("StrongholdZ", 0),
            new NbtByte("hasStrongholdEndPortal", 0),
            new NbtInt("StrongholdEndPortalX", 0),
            new NbtInt("StrongholdEndPortalZ", 0),

            // World size
            new NbtInt("XZSize", xzSize),
            new NbtInt("HellScale", hellScale),
        };

        // Copy GameRules if present
        if (javaData.Contains("GameRules"))
        {
            lceData.Add((NbtTag)javaData["GameRules"]!.Clone());
        }

        var root = new NbtCompound("") { lceData };
        var nbtFile = new NbtFile(root);

        // LCE stores level.dat as raw uncompressed NBT inside saveData.ms
        // (the container itself is zlib-compressed on disk)
        using var ms = new MemoryStream();
        nbtFile.SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }

    #region Helpers

    private static long GetLong(NbtCompound tag, string name, long def = 0)
        => tag.Get<NbtLong>(name)?.Value ?? def;

    private static int GetInt(NbtCompound tag, string name, int def = 0)
        => tag.Get<NbtInt>(name)?.Value ?? def;

    private static string GetString(NbtCompound tag, string name, string def = "")
        => tag.Get<NbtString>(name)?.Value ?? def;

    private static byte GetBool(NbtCompound tag, string name, bool def = false)
    {
        var b = tag.Get<NbtByte>(name);
        if (b != null) return b.Value;
        return (byte)(def ? 1 : 0);
    }

    #endregion
}
