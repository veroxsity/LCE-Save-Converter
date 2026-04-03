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
    public static byte[] Convert(
        NbtCompound javaRoot,
        int spawnChunkX,
        int spawnChunkZ,
        int xzSize,
        bool flatWorld,
        int? overrideSpawnY = null)
    {
        var javaData = javaRoot.Get<NbtCompound>("Data");
        if (javaData == null)
            throw new InvalidOperationException("Java level.dat missing 'Data' compound tag");

        int dataVersion = javaData.Get<NbtInt>("DataVersion")?.Value
            ?? javaRoot.Get<NbtInt>("DataVersion")?.Value
            ?? 0;
        bool isModernWorld = dataVersion >= 1519; // Java 1.13+

        int hellScale = 3;

        // Read original spawn
        JavaLevelDatHelper.SpawnPoint spawn = JavaLevelDatHelper.ReadSpawn(javaRoot);
        int spawnX = spawn.X;
        int spawnY = spawn.Y;
        int spawnZ = spawn.Z;

        // LCE TU19 chunk height is 128 blocks (0..127).
        // If a terrain-derived spawn Y is provided, prefer it.
        // For modern worlds we still keep a conservative fallback.
        if (overrideSpawnY.HasValue)
            spawnY = Math.Clamp(overrideSpawnY.Value, 1, 127);
        else
            spawnY = isModernWorld ? 64 : Math.Clamp(spawnY, 1, 127);

        // Recentre spawn so it's relative to chunk (0,0)
        int newSpawnX = spawnX - (spawnChunkX * 16);
        int newSpawnZ = spawnZ - (spawnChunkZ * 16);

        // Modern level.dat fields can contain generator names/options unknown to TU19.
        // Use conservative defaults for modern worlds to avoid world-init crashes.
        string safeGeneratorName = flatWorld
            ? "flat"
            : isModernWorld ? "default" : GetString(javaData, "generatorName", "default");
        int safeGeneratorVersion = flatWorld
            ? 0
            : isModernWorld ? 1 : GetInt(javaData, "generatorVersion");
        string safeGeneratorOptions = flatWorld
            ? GetString(javaData, "generatorOptions", "2;7,2x3,2;1;")
            : isModernWorld ? "" : GetString(javaData, "generatorOptions", "");

        // Build the LCE level.dat
        var lceData = new NbtCompound("Data")
        {
            // Standard Java fields
            new NbtLong("RandomSeed", GetLong(javaData, "RandomSeed")),
            new NbtString("generatorName", safeGeneratorName),
            new NbtInt("generatorVersion", safeGeneratorVersion),
            new NbtString("generatorOptions", safeGeneratorOptions),
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

        // Copy GameRules only for legacy worlds. Modern rule sets include unknown tags/values for TU19.
        if (!isModernWorld && javaData.Contains("GameRules"))
        {
            lceData.Add((NbtTag)javaData["GameRules"]!.Clone());
        }

        var root = new NbtCompound("") { lceData };
        var nbtFile = new NbtFile(root);

        // In this TU19 source tree, NbtIo::readCompressed no longer wraps a
        // GZip stream and instead reads raw NBT directly from the save file.
        // Therefore level.dat bytes inside saveData.ms must be uncompressed NBT.
        using var ms = new MemoryStream();
        nbtFile.SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }

    public static byte[] ConvertLceToJava(
        NbtCompound lceRoot,
        int? overrideSpawnX = null,
        int? overrideSpawnY = null,
        int? overrideSpawnZ = null,
        NbtCompound? embeddedPlayer = null)
    {
        NbtCompound lceData = lceRoot.Get<NbtCompound>("Data") ?? lceRoot;
        NbtCompound javaData = (NbtCompound)lceData.Clone();

        string[] lceOnlyFields =
        [
            "newSeaLevel",
            "hasBeenInCreative",
            "spawnBonusChest",
            "XZSize",
            "xzSize",
            "HellScale",
            "hellScale",
            "hasStronghold",
            "StrongholdX",
            "StrongholdY",
            "StrongholdZ",
            "hasStrongholdEndPortal",
            "StrongholdEndPortalX",
            "StrongholdEndPortalZ",
            "xStronghold",
            "yStronghold",
            "zStronghold",
            "hasStrongholdEP",
            "xStrongholdEP",
            "zStrongholdEP",
        ];

        foreach (string field in lceOnlyFields)
        {
            if (javaData.Contains(field))
                javaData.Remove(field);
        }

        if (overrideSpawnX.HasValue)
            UpsertTag(javaData, new NbtInt("SpawnX", overrideSpawnX.Value));
        if (overrideSpawnY.HasValue)
            UpsertTag(javaData, new NbtInt("SpawnY", overrideSpawnY.Value));
        if (overrideSpawnZ.HasValue)
            UpsertTag(javaData, new NbtInt("SpawnZ", overrideSpawnZ.Value));

        // Force a stable, known-upgradable Java baseline (1.12.2 / DataVersion 1343).
        UpsertTag(javaData, new NbtInt("version", 19133));
        UpsertTag(javaData, new NbtInt("DataVersion", 1343));
        UpsertTag(javaData, new NbtCompound("Version")
        {
            new NbtInt("Id", 1343),
            new NbtString("Name", "1.12.2"),
            new NbtByte("Snapshot", 0),
        });

        if (embeddedPlayer != null)
        {
            NbtCompound playerTag = (NbtCompound)embeddedPlayer.Clone();
            playerTag.Name = "Player";
            UpsertTag(javaData, playerTag);
        }

        UpsertTag(javaData, new NbtLong("LastPlayed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        var javaRoot = new NbtCompound(string.Empty)
        {
            new NbtCompound("Data")
        };

        foreach (NbtTag tag in javaData)
            ((NbtCompound)javaRoot["Data"]!).Add((NbtTag)tag.Clone());

        var file = new NbtFile(javaRoot);
        using var ms = new MemoryStream();
        file.SaveToStream(ms, NbtCompression.GZip);
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

    private static void UpsertTag(NbtCompound compound, NbtTag tag)
    {
        if (string.IsNullOrEmpty(tag.Name))
            return;

        if (compound.Contains(tag.Name))
            compound.Remove(tag.Name);

        compound.Add(tag);
    }

    #endregion
}
