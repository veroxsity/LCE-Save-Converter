using fNbt;

namespace LceWorldConverter;

public sealed class SpawnEstimationService
{
    public int? EstimateSafeSpawnY(
        JavaWorldReader reader,
        int spawnX,
        int sourceSpawnY,
        int spawnZ,
        int spawnChunkX,
        int spawnChunkZ)
    {
        try
        {
            int regionX = FloorDiv(spawnChunkX, 32);
            int regionZ = FloorDiv(spawnChunkZ, 32);
            string? regionPath = reader.GetRegionFiles(string.Empty)
                .FirstOrDefault(region => region.rx == regionX && region.rz == regionZ)
                .path;
            if (regionPath == null)
                return null;

            int localChunkX = ((spawnChunkX % 32) + 32) % 32;
            int localChunkZ = ((spawnChunkZ % 32) + 32) % 32;
            NbtCompound? root = reader.ReadChunkNbt(regionPath, localChunkX, localChunkZ);
            if (root == null)
                return null;

            var level = root.Get<NbtCompound>("Level") ?? root;
            int lx = ((spawnX % 16) + 16) % 16;
            int lz = ((spawnZ % 16) + 16) % 16;
            int idx2d = lx + (lz * 16);

            var hmBytes = level.Get<NbtByteArray>("HeightMap")?.Value;
            if (hmBytes != null && hmBytes.Length >= 256)
            {
                int height = hmBytes[idx2d] & 0xFF;
                if (height > 0)
                    return Math.Clamp(height + 1, 1, 127);
            }

            var hmInts = level.Get<NbtIntArray>("HeightMap")?.Value;
            if (hmInts != null && hmInts.Length >= 256)
            {
                int height = hmInts[idx2d];
                if (height > 0)
                    return Math.Clamp(height + 1, 1, 127);
            }

            var blocks = level.Get<NbtByteArray>("Blocks")?.Value;
            if (blocks != null && blocks.Length >= 32768)
            {
                for (int y = 127; y >= 1; y--)
                {
                    int flatIndex = y * 256 + lz * 16 + lx;
                    if (blocks[flatIndex] != 0)
                        return Math.Clamp(y + 1, 1, 127);
                }
            }

            var sections = level.Get<NbtList>("Sections") ?? level.Get<NbtList>("sections");
            if (sections != null)
            {
                int maxY = -1;
                foreach (NbtTag tag in sections)
                {
                    if (tag is not NbtCompound section)
                        continue;

                    int sectionY = section.Get<NbtByte>("Y")?.Value ?? -1;
                    if (sectionY < 0 || sectionY > 7)
                        continue;

                    var sectionBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
                    if (sectionBlocks == null || sectionBlocks.Length < 4096)
                        continue;

                    for (int y = 15; y >= 0; y--)
                    {
                        int index = lx + lz * 16 + y * 256;
                        if (sectionBlocks[index] != 0)
                        {
                            int globalY = sectionY * 16 + y;
                            if (globalY > maxY)
                                maxY = globalY;
                            break;
                        }
                    }
                }

                if (maxY >= 0)
                    return Math.Clamp(maxY + 1, 1, 127);
            }

            return Math.Clamp(sourceSpawnY, 1, 127);
        }
        catch
        {
            return Math.Clamp(sourceSpawnY, 1, 127);
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor));

        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && value < 0)
            quotient--;

        return quotient;
    }
}
