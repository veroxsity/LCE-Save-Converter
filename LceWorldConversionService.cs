using fNbt;

namespace LceWorldConverter;

public sealed class LceWorldConversionService
{
    private const short OutputOriginalSaveVersion = 7;
    private const short OutputCurrentSaveVersion = 9;

    public ConversionResult Convert(ConversionOptions options, IConversionLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        logger ??= NullConversionLogger.Instance;

        using var preparedWorld = PreparedJavaWorld.Open(options.InputPath);

        string javaWorldPath = preparedWorld.WorldPath;
        string outputDir = options.OutputDirectory;
        Directory.CreateDirectory(outputDir);

        string outputPath = Path.Combine(outputDir, "saveData.ms");
        string levelDatPath = Path.Combine(javaWorldPath, "level.dat");
        if (!File.Exists(levelDatPath))
            throw new FileNotFoundException($"level.dat not found in: {javaWorldPath}", levelDatPath);

        int halfSize = options.XzSize / 2;
        int hellScale = 3;
        int hellHalfSize = (options.XzSize / hellScale) / 2;
        int endHalfSize = 9;

        logger.Info($"Source:      {options.InputPath}");
        if (preparedWorld.IsArchive)
            logger.Info($"Extracted:    {javaWorldPath}");
        logger.Info($"Output:      {outputPath}");
        logger.Info($"World size:  {options.SizeLabel} ({options.XzSize} chunks / {options.XzSize * 16} blocks)");
        logger.Info($"World type:  {(options.FlatWorld ? "Flat" : "Default")}");
        logger.Info(string.Empty);

        ChunkConverter.ResetUnknownModernBlocks();
        ChunkConverter.PreserveDynamicChunkData = options.PreserveEntities;

        using var reader = new JavaWorldReader(javaWorldPath);

        logger.Info("Reading level.dat...");
        var javaLevelDat = reader.ReadLevelDat();
        var javaData = javaLevelDat.Get<NbtCompound>("Data")!;

        int spawnX = javaData.Get<NbtInt>("SpawnX")?.Value ?? 0;
        int spawnZ = javaData.Get<NbtInt>("SpawnZ")?.Value ?? 0;
        int spawnY = javaData.Get<NbtInt>("SpawnY")?.Value ?? 64;
        int spawnChunkX = spawnX >> 4;
        int spawnChunkZ = spawnZ >> 4;

        logger.Info($"Java spawn:  ({spawnX}, {spawnZ}) -> chunk ({spawnChunkX}, {spawnChunkZ})");
        logger.Info($"Recentring:  Java chunk ({spawnChunkX},{spawnChunkZ}) -> LCE chunk (0,0)");

        int? estimatedSpawnY = EstimateSafeSpawnY(reader, spawnX, spawnY, spawnZ, spawnChunkX, spawnChunkZ);
        if (estimatedSpawnY.HasValue)
            logger.Info($"Spawn Y:     {spawnY} -> {estimatedSpawnY.Value} (terrain-adjusted)");
        else
            logger.Info($"Spawn Y:     {spawnY} (source/default)");

        logger.Info(string.Empty);

        var container = new SaveDataContainer(OutputOriginalSaveVersion, OutputCurrentSaveVersion);

        string[] defaultRegionOrder =
        [
            "DIM-1r.-1.-1.mcr", "DIM-1r.0.-1.mcr", "DIM-1r.0.0.mcr", "DIM-1r.-1.0.mcr",
            "DIM1/r.-1.-1.mcr", "DIM1/r.0.-1.mcr", "DIM1/r.0.0.mcr", "DIM1/r.-1.0.mcr",
            "r.-1.-1.mcr", "r.0.-1.mcr", "r.0.0.mcr", "r.-1.0.mcr"
        ];
        foreach (string name in defaultRegionOrder)
            container.CreateFile(name);

        logger.Info($"Converting overworld ({options.XzSize}x{options.XzSize} chunks)...");
        int owConverted = ConvertDimension(reader, container, "overworld", string.Empty, halfSize, spawnChunkX, spawnChunkZ, logger);
        logger.Info($"  {owConverted} chunks converted");

        int netherConverted = 0;
        int endConverted = 0;
        if (options.ConvertAllDimensions)
        {
            logger.Info($"Converting nether ({options.XzSize / hellScale}x{options.XzSize / hellScale} chunks)...");
            int netherOffsetChunkX = FloorDiv(spawnChunkX, 8);
            int netherOffsetChunkZ = FloorDiv(spawnChunkZ, 8);
            netherConverted = ConvertDimension(reader, container, "nether", "DIM-1", hellHalfSize, netherOffsetChunkX, netherOffsetChunkZ, logger);
            logger.Info($"  {netherConverted} chunks converted");

            logger.Info($"Converting end ({endHalfSize * 2}x{endHalfSize * 2} chunks)...");
            endConverted = ConvertDimension(reader, container, "end", "DIM1", endHalfSize, 0, 0, logger);
            logger.Info($"  {endConverted} chunks converted");
        }
        else
        {
            logger.Info("Skipping nether/end conversion (use --all-dimensions to enable).");
        }

        logger.Info("Converting level.dat...");
        byte[] lceLevelDat = LevelDatConverter.Convert(javaLevelDat, spawnChunkX, spawnChunkZ, options.XzSize, options.FlatWorld, estimatedSpawnY);
        var levelDatEntry = container.CreateFile("level.dat");
        container.WriteToFile(levelDatEntry, lceLevelDat);

        int blockOffsetX = spawnChunkX * 16;
        int blockOffsetZ = spawnChunkZ * 16;
        int playersCopied = 0;
        if (options.CopyPlayers)
        {
            logger.Info("Copying player data...");
            playersCopied = CopyPlayers(javaWorldPath, container, blockOffsetX, blockOffsetZ);
            logger.Info($"  {playersCopied} player(s)");
        }
        else
        {
            logger.Info("Skipping player data import (use --copy-players to enable).");
        }

        logger.Info("Writing saveData.ms...");
        container.Save(outputPath);

        IReadOnlyList<string> unknownBlocks = ChunkConverter.GetUnknownModernBlocksSnapshot();
        string? unknownPath = null;
        if (unknownBlocks.Count > 0)
        {
            unknownPath = Path.Combine(outputDir, "unknown-modern-blocks.txt");
            File.WriteAllLines(unknownPath, unknownBlocks);

            logger.Info(string.Empty);
            logger.Info($"Unknown modern blocks mapped to air: {unknownBlocks.Count}");
            foreach (string blockName in unknownBlocks.Take(40))
                logger.Info($"  - {blockName}");
            if (unknownBlocks.Count > 40)
                logger.Info($"  ... and {unknownBlocks.Count - 40} more");
            logger.Info($"Full list written to: {unknownPath}");
        }

        logger.Info(string.Empty);
        logger.Info("Conversion complete!");
        logger.Info($"  Overworld: {owConverted} chunks");
        logger.Info($"  Nether:    {netherConverted} chunks");
        logger.Info($"  End:       {endConverted} chunks");
        logger.Info($"  Output:    {outputPath}");

        return new ConversionResult
        {
            SourceWorldPath = javaWorldPath,
            OutputDirectory = outputDir,
            OutputPath = outputPath,
            OverworldChunks = owConverted,
            NetherChunks = netherConverted,
            EndChunks = endConverted,
            PlayersCopied = playersCopied,
            UnknownModernBlocks = unknownBlocks,
            UnknownBlocksPath = unknownPath,
        };
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

    private static int? EstimateSafeSpawnY(
        JavaWorldReader reader,
        int spawnX,
        int sourceSpawnY,
        int spawnZ,
        int spawnChunkX,
        int spawnChunkZ)
    {
        try
        {
            int regionX = spawnChunkX >> 5;
            int regionZ = spawnChunkZ >> 5;
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

    private static int CopyPlayers(string javaWorldPath, SaveDataContainer container, int blockOffsetX, int blockOffsetZ)
    {
        string[] candidateDirs =
        [
            Path.Combine(javaWorldPath, "players")
        ];

        int count = 0;
        foreach (string dir in candidateDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string filePath in Directory.GetFiles(dir, "*.dat"))
            {
                try
                {
                    string fileStem = Path.GetFileNameWithoutExtension(filePath);
                    if (!ulong.TryParse(fileStem, out ulong parsedPlayerId))
                        continue;

                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    var player = nbtFile.RootTag;

                    var pos = player.Get<NbtList>("Pos");
                    if (pos != null && pos.Count >= 3)
                    {
                        ((NbtDouble)pos[0]).Value -= blockOffsetX;
                        ((NbtDouble)pos[2]).Value -= blockOffsetZ;
                    }

                    var spawnX = player.Get<NbtInt>("SpawnX");
                    var spawnZ = player.Get<NbtInt>("SpawnZ");
                    if (spawnX != null)
                        spawnX.Value -= blockOffsetX;
                    if (spawnZ != null)
                        spawnZ.Value -= blockOffsetZ;

                    using var ms = new MemoryStream();
                    nbtFile.SaveToStream(ms, NbtCompression.None);
                    byte[] remapped = ms.ToArray();

                    string entryName = "players/" + parsedPlayerId + ".dat";
                    var entry = container.CreateFile(entryName);
                    container.WriteToFile(entry, remapped);
                    count++;
                }
                catch
                {
                }
            }
        }

        return count;
    }

    private static int ConvertDimension(
        JavaWorldReader reader,
        SaveDataContainer container,
        string dimensionLabel,
        string dimensionPrefix,
        int halfSize,
        int offsetChunkX,
        int offsetChunkZ,
        IConversionLogger logger)
    {
        var regionFiles = reader.GetRegionFiles(dimensionPrefix);
        var regionLookup = new Dictionary<(int, int), string>();
        foreach (var (rx, rz, path) in regionFiles)
            regionLookup[(rx, rz)] = path;

        string lcePrefix = dimensionPrefix switch
        {
            "DIM-1" => "DIM-1",
            "DIM1" => "DIM1/",
            _ => string.Empty,
        };

        var lceRegions = new Dictionary<(int, int), LceRegionFile>();
        int converted = 0;
        int lastProgressBucket = -1;
        int totalRows = halfSize * 2;

        for (int lcx = -halfSize; lcx < halfSize; lcx++)
        {
            for (int lcz = -halfSize; lcz < halfSize; lcz++)
            {
                int jx = lcx + offsetChunkX;
                int jz = lcz + offsetChunkZ;

                int jrx = jx >> 5;
                int jrz = jz >> 5;

                if (!regionLookup.TryGetValue((jrx, jrz), out string? regionPath))
                    continue;

                int localX = ((jx % 32) + 32) % 32;
                int localZ = ((jz % 32) + 32) % 32;

                NbtCompound? chunkNbt;
                try
                {
                    chunkNbt = reader.ReadChunkNbt(regionPath, localX, localZ);
                }
                catch
                {
                    continue;
                }

                if (chunkNbt == null)
                    continue;

                byte[] lceChunkData;
                try
                {
                    lceChunkData = ChunkConverter.ConvertChunk(chunkNbt, lcx, lcz);
                    lceChunkData = CanonicalizeChunkNbt(lceChunkData, lcx, lcz);
                    if (lceChunkData.Length == 0)
                        continue;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error converting chunk ({lcx},{lcz}) from Java ({jx},{jz}): {ex.Message}");
                    throw;
                }

                int lrx = lcx >> 5;
                int lrz = lcz >> 5;

                if (!lceRegions.TryGetValue((lrx, lrz), out LceRegionFile? lceRegion))
                {
                    string regionName = $"{lcePrefix}r.{lrx}.{lrz}.mcr";
                    lceRegion = new LceRegionFile(regionName);
                    lceRegions[(lrx, lrz)] = lceRegion;
                }

                int lceLocalX = ((lcx % 32) + 32) % 32;
                int lceLocalZ = ((lcz % 32) + 32) % 32;

                lceRegion.WriteChunk(lceLocalX, lceLocalZ, lceChunkData);
                converted++;
            }

            int completedRows = lcx + halfSize + 1;
            int progress = (int)(((double)completedRows / totalRows) * 100);
            int currentBucket = Math.Min(progress / 10, 10);
            if (currentBucket != lastProgressBucket)
            {
                if (converted == 0)
                {
                    logger.Info($"  {dimensionLabel}: scanning {completedRows}/{totalRows} chunk rows, no populated chunks found yet ({Math.Min(progress, 100)}%)");
                }
                else
                {
                    logger.Info($"  {dimensionLabel}: scanned {completedRows}/{totalRows} chunk rows, converted {converted} chunks so far ({Math.Min(progress, 100)}%)");
                }
                lastProgressBucket = currentBucket;
            }
        }

        foreach (var (_, region) in lceRegions)
            region.WriteTo(container);

        return converted;
    }

    private static byte[] CanonicalizeChunkNbt(byte[] chunkData, int expectedX, int expectedZ)
    {
        try
        {
            using var ms = new MemoryStream(chunkData);
            var file = new NbtFile();
            file.LoadFromStream(ms, NbtCompression.None);

            NbtCompound root = file.RootTag;
            NbtCompound? level = root.Get<NbtCompound>("Level");

            if (level == null && root.Contains("Blocks"))
            {
                level = (NbtCompound)root.Clone();
                level.Name = "Level";
                root = new NbtCompound(string.Empty) { level };
                file = new NbtFile(root);
            }

            if (level == null)
                return Array.Empty<byte>();

            UpsertTag(level, new NbtInt("xPos", expectedX));
            UpsertTag(level, new NbtInt("zPos", expectedZ));

            EnsureByteArrayTag(level, "Blocks", ChunkConverter.CHUNK_BLOCKS);
            EnsureByteArrayTag(level, "Data", ChunkConverter.CHUNK_NIBBLES);
            EnsureByteArrayTag(level, "SkyLight", ChunkConverter.CHUNK_NIBBLES, fillByte: 0xFF);
            EnsureByteArrayTag(level, "BlockLight", ChunkConverter.CHUNK_NIBBLES);
            EnsureByteArrayTag(level, "HeightMap", ChunkConverter.HEIGHTMAP_SIZE);
            EnsureByteArrayTag(level, "Biomes", ChunkConverter.BIOMES_SIZE, fillByte: 1);

            if (!level.Contains("TerrainPopulatedFlags") && !level.Contains("TerrainPopulated"))
                UpsertTag(level, new NbtShort("TerrainPopulatedFlags", 2046));

            EnsureListTag(level, "Entities", NbtTagType.Compound);
            EnsureListTag(level, "TileEntities", NbtTagType.Compound);

            using var outMs = new MemoryStream();
            file.SaveToStream(outMs, NbtCompression.None);
            return outMs.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static void EnsureByteArrayTag(NbtCompound level, string name, int requiredLength, byte fillByte = 0)
    {
        byte[]? data = level.Get<NbtByteArray>(name)?.Value;
        if (data != null && data.Length == requiredLength)
            return;

        var corrected = new byte[requiredLength];
        if (fillByte != 0)
            Array.Fill(corrected, fillByte);

        if (data != null)
            Buffer.BlockCopy(data, 0, corrected, 0, Math.Min(data.Length, requiredLength));

        UpsertTag(level, new NbtByteArray(name, corrected));
    }

    private static void EnsureListTag(NbtCompound level, string name, NbtTagType listType)
    {
        if (level[name] is NbtList list && list.ListType == listType)
            return;

        UpsertTag(level, new NbtList(name, listType));
    }

    private static void UpsertTag(NbtCompound compound, NbtTag tag)
    {
        string? name = tag.Name;
        if (string.IsNullOrEmpty(name))
            return;

        if (compound.Contains(name))
            compound.Remove(name);
        compound.Add(tag);
    }
}