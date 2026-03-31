using fNbt;

namespace LceWorldConverter;

internal static class JavaLevelDatHelper
{
    internal readonly record struct SpawnPoint(int X, int Y, int Z);

    public static SpawnPoint ReadSpawn(NbtCompound javaRoot)
    {
        ArgumentNullException.ThrowIfNull(javaRoot);

        NbtCompound javaData = javaRoot.Get<NbtCompound>("Data")
            ?? throw new InvalidOperationException("Java level.dat missing 'Data' compound tag");

        NbtInt? legacySpawnX = javaData.Get<NbtInt>("SpawnX");
        NbtInt? legacySpawnY = javaData.Get<NbtInt>("SpawnY");
        NbtInt? legacySpawnZ = javaData.Get<NbtInt>("SpawnZ");
        if (legacySpawnX != null && legacySpawnZ != null)
        {
            return new SpawnPoint(
                legacySpawnX.Value,
                legacySpawnY?.Value ?? 64,
                legacySpawnZ.Value);
        }

        NbtCompound? modernSpawn = javaData.Get<NbtCompound>("spawn");
        int[]? modernSpawnPos = modernSpawn?.Get<NbtIntArray>("pos")?.Value;
        if (modernSpawnPos != null && modernSpawnPos.Length >= 3)
        {
            return new SpawnPoint(
                modernSpawnPos[0],
                modernSpawnPos[1],
                modernSpawnPos[2]);
        }

        return new SpawnPoint(0, 64, 0);
    }
}
