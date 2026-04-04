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

        return options.Direction switch
        {
            ConversionDirection.JavaToLce => ConvertJavaToLce(options, logger),
            ConversionDirection.LceToJava => ConvertLceToJava(options, logger),
            _ => throw new InvalidOperationException($"Unsupported conversion direction: {options.Direction}"),
        };
    }

    private ConversionResult ConvertJavaToLce(ConversionOptions options, IConversionLogger logger)
    {
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

        JavaLevelDatHelper.SpawnPoint spawn = JavaLevelDatHelper.ReadSpawn(javaLevelDat);
        int spawnX = spawn.X;
        int spawnZ = spawn.Z;
        int spawnY = spawn.Y;
        int spawnChunkX = FloorDiv(spawnX, 16);
        int spawnChunkZ = FloorDiv(spawnZ, 16);

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

    private ConversionResult ConvertLceToJava(ConversionOptions options, IConversionLogger logger)
    {
        string saveDataPath = options.InputPath;
        if (!File.Exists(saveDataPath))
            throw new FileNotFoundException($"saveData.ms not found: {saveDataPath}", saveDataPath);

        string outputDir = options.OutputDirectory;
        Directory.CreateDirectory(outputDir);
        DeleteStaleJavaRuntimeState(outputDir);
        DeleteLegacyOutputRegions(outputDir);

        logger.Info($"Source:      {saveDataPath}");
        logger.Info($"Output:      {outputDir}");
        logger.Info(string.Empty);

        var saveReader = new LceSaveDataReader(saveDataPath);

        if (!saveReader.TryReadLevelDat(out NbtCompound lceLevelDat))
            throw new InvalidDataException("Input saveData.ms does not contain a readable level.dat entry.");

        NbtCompound? embeddedPlayer = TryReadPrimaryLcePlayer(saveReader);
        if (embeddedPlayer != null)
            logger.Info("Embedding primary LCE player into level.dat (Data.Player).");
        else
            logger.Info("No usable LCE player entry found; Java level.dat will not include Data.Player.");

        logger.Info("Converting overworld regions...");
        var overworldStats = ConvertLceRegionsToJava(options, saveReader, outputDir, "overworld", logger);
        int overworldChunks = overworldStats.chunkCount;

        int netherChunks = 0;
        int endChunks = 0;
        if (options.ConvertAllDimensions)
        {
            logger.Info("Converting nether regions...");
            netherChunks = ConvertLceRegionsToJava(options, saveReader, outputDir, "nether", logger).chunkCount;

            logger.Info("Converting end regions...");
            endChunks = ConvertLceRegionsToJava(options, saveReader, outputDir, "end", logger).chunkCount;
        }
        else
        {
            logger.Info("Skipping nether/end conversion (use --all-dimensions to enable).");
        }

        int? spawnX = null;
        int? spawnY = null;
        int? spawnZ = null;
        if (overworldStats.densestChunk.HasValue)
        {
            var dense = overworldStats.densestChunk.Value;
            spawnX = dense.chunkX * 16 + 8;
            spawnY = Math.Clamp(dense.maxY + 1, 5, 127);
            spawnZ = dense.chunkZ * 16 + 8;
            logger.Info($"Setting Java spawn near populated area: ({spawnX}, {spawnY}, {spawnZ})");
        }

        logger.Info("Converting level.dat...");
        byte[] javaLevelDat = LevelDatConverter.ConvertLceToJava(lceLevelDat, spawnX, spawnY, spawnZ, embeddedPlayer);
        File.WriteAllBytes(Path.Combine(outputDir, "level.dat"), javaLevelDat);

        int playersCopied = 0;
        if (options.CopyPlayers)
        {
            logger.Info("Exporting player data...");
            playersCopied = ExportPlayers(saveReader, outputDir);
            logger.Info($"  {playersCopied} player(s)");
        }
        else
        {
            logger.Info("Skipping player export (use --copy-players to enable).");
        }

        logger.Info(string.Empty);
        logger.Info("Conversion complete!");
        logger.Info($"  Overworld: {overworldChunks} chunks");
        logger.Info($"  Nether:    {netherChunks} chunks");
        logger.Info($"  End:       {endChunks} chunks");
        logger.Info($"  Output:    {outputDir}");

        return new ConversionResult
        {
            SourceWorldPath = saveDataPath,
            OutputDirectory = outputDir,
            OutputPath = outputDir,
            OverworldChunks = overworldChunks,
            NetherChunks = netherChunks,
            EndChunks = endChunks,
            PlayersCopied = playersCopied,
            UnknownModernBlocks = Array.Empty<string>(),
            UnknownBlocksPath = null,
        };
    }

    private static (int chunkCount, DensestChunk? densestChunk) ConvertLceRegionsToJava(
        ConversionOptions options,
        LceSaveDataReader saveReader,
        string outputDir,
        string dimension,
        IConversionLogger logger)
    {
        int chunkCount = 0;
        DensestChunk? densestChunk = null;
        var writers = new Dictionary<(int rx, int rz), JavaRegionFileWriter>();

        foreach (var entry in saveReader.EnumerateEntries())
        {
            if (!TryMatchRegionEntry(entry.Name, dimension, out int rx, out int rz))
                continue;

            if (!saveReader.TryGetFileBytes(entry.Name, out byte[] regionBytes) || regionBytes.Length < 8192)
                continue;

            string regionDir = dimension switch
            {
                "nether" => Path.Combine(outputDir, "DIM-1", "region"),
                "end" => Path.Combine(outputDir, "DIM1", "region"),
                _ => Path.Combine(outputDir, "region"),
            };

            string regionPath = Path.Combine(regionDir, $"r.{rx}.{rz}.mca");
            if (!writers.TryGetValue((rx, rz), out JavaRegionFileWriter? writer))
            {
                writer = new JavaRegionFileWriter(regionPath);
                writers[(rx, rz)] = writer;
            }

            chunkCount += DecodeLceRegionIntoWriter(options, regionBytes, rx, rz, writer, ref densestChunk, dimension == "overworld");
        }

        foreach (var writer in writers.Values)
            writer.Save();

        logger.Info($"  {chunkCount} chunks converted");
        return (chunkCount, densestChunk);
    }

    private static void DeleteStaleJavaRuntimeState(string outputDir)
    {
        string[] staleDirs =
        [
            Path.Combine(outputDir, "playerdata"),
            Path.Combine(outputDir, "players"),
            Path.Combine(outputDir, "entities"),
            Path.Combine(outputDir, "poi"),
            Path.Combine(outputDir, "stats"),
            Path.Combine(outputDir, "advancements"),
        ];

        foreach (string dir in staleDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        string levelDatPath = Path.Combine(outputDir, "level.dat");
        if (File.Exists(levelDatPath))
            File.Delete(levelDatPath);
    }

    private static void DeleteLegacyOutputRegions(string outputDir)
    {
        string[] regionDirs =
        [
            Path.Combine(outputDir, "region"),
            Path.Combine(outputDir, "DIM-1", "region"),
            Path.Combine(outputDir, "DIM1", "region"),
        ];

        foreach (string dir in regionDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "*.mcr"))
                File.Delete(file);
            foreach (string file in Directory.GetFiles(dir, "*.mca"))
                File.Delete(file);
        }
    }

    private static NbtCompound? TryReadPrimaryLcePlayer(LceSaveDataReader saveReader)
    {
        var playerEntry = saveReader.EnumerateEntries()
            .Where(entry => entry.Name.StartsWith("players/", StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.LastModifiedTime)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (playerEntry == null)
            return null;

        if (!saveReader.TryGetFileBytes(playerEntry.Name, out byte[] playerBytes))
            return null;

        try
        {
            var playerFile = new NbtFile();
            playerFile.LoadFromBuffer(playerBytes, 0, playerBytes.Length, NbtCompression.AutoDetect);
            NbtCompound playerTag = (NbtCompound)playerFile.RootTag.Clone();
            playerTag.Name = "Player";
            return playerTag;
        }
        catch
        {
            return null;
        }
    }

    private static int DecodeLceRegionIntoWriter(
        ConversionOptions options,
        byte[] regionBytes,
        int regionX,
        int regionZ,
        JavaRegionFileWriter writer,
        ref DensestChunk? densestChunk,
        bool trackDensity)
    {
        int converted = 0;

        for (int index = 0; index < 1024; index++)
        {
            uint offsetEntry = BitConverter.ToUInt32(regionBytes, index * 4);
            if (offsetEntry == 0)
                continue;

            int sectorOffset = (int)(offsetEntry >> 8);
            if (sectorOffset <= 0)
                continue;

            int chunkPos = sectorOffset * 4096;
            if (chunkPos + 8 > regionBytes.Length)
                continue;

            uint compressedLengthRaw = BitConverter.ToUInt32(regionBytes, chunkPos);
            bool usesRle = (compressedLengthRaw & 0x80000000u) != 0;
            int compressedLength = (int)(compressedLengthRaw & 0x7FFFFFFF);
            int decompressedLength = (int)BitConverter.ToUInt32(regionBytes, chunkPos + 4);

            if (compressedLength <= 0 || chunkPos + 8 + compressedLength > regionBytes.Length)
                continue;

            byte[] compressed = new byte[compressedLength];
            Buffer.BlockCopy(regionBytes, chunkPos + 8, compressed, 0, compressedLength);

            byte[] nbtBytes;
            try
            {
                nbtBytes = usesRle
                    ? LceCompression.Decompress(compressed, decompressedLength)
                    : LceCompression.DecompressZlibOnly(compressed);
            }
            catch
            {
                continue;
            }

            int localX = index & 31;
            int localZ = index >> 5;
            int chunkX = regionX * 32 + localX;
            int chunkZ = regionZ * 32 + localZ;

            byte[] prepared = PrepareJavaChunkNbt(options, nbtBytes, chunkX, chunkZ);
            writer.WriteChunk(localX, localZ, prepared);

            if (trackDensity)
                TrackChunkDensity(prepared, chunkX, chunkZ, ref densestChunk);

            converted++;
        }

        return converted;
    }

    private static void TrackChunkDensity(byte[] chunkNbt, int chunkX, int chunkZ, ref DensestChunk? densestChunk)
    {
        try
        {
            var file = new NbtFile();
            file.LoadFromBuffer(chunkNbt, 0, chunkNbt.Length, NbtCompression.None);
            var level = file.RootTag.Get<NbtCompound>("Level") ?? file.RootTag;
            int nonAir = 0;
            int maxY = 0;

            var blocks = level.Get<NbtByteArray>("Blocks")?.Value;
            if (blocks != null && blocks.Length >= 32768)
            {
                for (int y = 0; y < 128; y++)
                {
                    bool layerHasSolid = false;
                    for (int z = 0; z < 16; z++)
                    {
                        for (int x = 0; x < 16; x++)
                        {
                            int idx = ((x * 16) + z) * 128 + y;
                            if (blocks[idx] != 0)
                            {
                                nonAir++;
                                layerHasSolid = true;
                            }
                        }
                    }

                    if (layerHasSolid)
                        maxY = y;
                }
            }
            else if (level.Get<NbtList>("Sections") is NbtList sections)
            {
                foreach (NbtTag tag in sections)
                {
                    if (tag is not NbtCompound section)
                        continue;

                    int sectionY = section.Get<NbtByte>("Y")?.Value ?? -1;
                    if (sectionY < 0 || sectionY > 15)
                        continue;

                    byte[]? secBlocks = section.Get<NbtByteArray>("Blocks")?.Value;
                    if (secBlocks == null || secBlocks.Length < 4096)
                        continue;

                    for (int i = 0; i < 4096; i++)
                    {
                        if (secBlocks[i] == 0)
                            continue;

                        nonAir++;
                        int yInSection = (i >> 8) & 0x0F;
                        int y = sectionY * 16 + yInSection;
                        if (y > maxY)
                            maxY = y;
                    }
                }
            }
            else
            {
                return;
            }

            if (densestChunk == null || nonAir > densestChunk.Value.nonAir)
            {
                densestChunk = new DensestChunk
                {
                    chunkX = chunkX,
                    chunkZ = chunkZ,
                    nonAir = nonAir,
                    maxY = maxY,
                };
            }
        }
        catch
        {
        }
    }

    private readonly struct DensestChunk
    {
        public required int chunkX { get; init; }
        public required int chunkZ { get; init; }
        public required int nonAir { get; init; }
        public required int maxY { get; init; }
    }

    private static byte[] PrepareJavaChunkNbt(ConversionOptions options, byte[] rawNbt, int chunkX, int chunkZ)
    {
        try
        {
            if (!LceChunkPayloadCodec.TryDecodeToLegacyNbt(rawNbt, out byte[] legacyNbt))
                return rawNbt;

            var file = new NbtFile();
            file.LoadFromBuffer(legacyNbt, 0, legacyNbt.Length, NbtCompression.None);

            NbtCompound root = file.RootTag;
            NbtCompound? level = root.Get<NbtCompound>("Level");

            if (level == null && root.Contains("Blocks"))
            {
                level = (NbtCompound)root.Clone();
                level.Name = "Level";
                root = new NbtCompound(string.Empty) { level };
            }

            if (level == null)
                return legacyNbt;

            NbtCompound anvilLevel = BuildAnvilLevel(options, level, chunkX, chunkZ);
            var normalized = new NbtFile(new NbtCompound(string.Empty)
            {
                new NbtInt("DataVersion", 1343),
                anvilLevel,
            });
            using var ms = new MemoryStream();
            normalized.SaveToStream(ms, NbtCompression.None);
            return ms.ToArray();
        }
        catch
        {
            return rawNbt;
        }
    }

    private static NbtCompound BuildAnvilLevel(ConversionOptions options, NbtCompound legacyLevel, int chunkX, int chunkZ)
    {
        bool isModernJava = options.TargetVersion.StartsWith("1.20.3") || options.TargetVersion.StartsWith("1.20.4") || options.TargetVersion.StartsWith("1.20.5") || options.TargetVersion.StartsWith("1.21");
        byte[] oldBlocks = legacyLevel.Get<NbtByteArray>("Blocks")?.Value ?? new byte[32768];
        byte[] oldData = legacyLevel.Get<NbtByteArray>("Data")?.Value ?? new byte[16384];
        byte[] oldSky = legacyLevel.Get<NbtByteArray>("SkyLight")?.Value ?? CreateFilledArray(16384, 0xFF);
        byte[] oldBlockLight = legacyLevel.Get<NbtByteArray>("BlockLight")?.Value ?? new byte[16384];

        var sections = new NbtList("Sections", NbtTagType.Compound);
        for (int sectionY = 0; sectionY < 8; sectionY++)
        {
            byte[] secBlocks = new byte[4096];
            byte[] secData = new byte[2048];
            byte[] secSky = new byte[2048];
            byte[] secBlockLight = new byte[2048];

            bool hasAnyBlock = false;
            for (int yInSection = 0; yInSection < 16; yInSection++)
            {
                int y = sectionY * 16 + yInSection;
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int oldIndex = ((x * 16) + z) * 128 + y;
                        int secIndex = x + z * 16 + yInSection * 256;

                        byte blockId = oldBlocks[oldIndex];
                        int oldDataNibble = GetNibble(oldData, oldIndex);

                        // LCE path block ID is 198, but Java 1.12 path block is 208
                        if (blockId == 198)
                        {
                            blockId = 208;
                        }

                        secBlocks[secIndex] = blockId;
                        if (blockId != 0)
                            hasAnyBlock = true;

                        SetNibble(secData, secIndex, (byte)oldDataNibble);
                        SetNibble(secSky, secIndex, GetNibble(oldSky, oldIndex));
                        SetNibble(secBlockLight, secIndex, GetNibble(oldBlockLight, oldIndex));
                    }
                }
            }

            if (!hasAnyBlock)
                continue;

            var section = new NbtCompound(string.Empty)
            {
                new NbtByte("Y", (byte)sectionY),
                new NbtByteArray("Blocks", secBlocks),
                new NbtByteArray("Data", secData),
                new NbtByteArray("SkyLight", secSky),
                new NbtByteArray("BlockLight", secBlockLight),
            };
            section.Name = null;
            sections.Add(section);
        }

        int[] heightMap = new int[256];
        byte[] hm = legacyLevel.Get<NbtByteArray>("HeightMap")?.Value ?? new byte[256];
        for (int i = 0; i < 256; i++)
            heightMap[i] = hm[i] & 0xFF;

        var entities = legacyLevel.Get<NbtList>("Entities")?.Clone() as NbtList ?? new NbtList("Entities", NbtTagType.Compound);
        entities.Name = "Entities";
        var tileEntities = legacyLevel.Get<NbtList>("TileEntities")?.Clone() as NbtList ?? new NbtList("TileEntities", NbtTagType.Compound);
        tileEntities.Name = "TileEntities";

        return new NbtCompound("Level")
        {
            new NbtInt("xPos", chunkX),
            new NbtInt("zPos", chunkZ),
            new NbtLong("LastUpdate", legacyLevel.Get<NbtLong>("LastUpdate")?.Value ?? 0),
            new NbtLong("InhabitedTime", legacyLevel.Get<NbtLong>("InhabitedTime")?.Value ?? 0),
            new NbtByte("TerrainPopulated", 1),
            new NbtByte("LightPopulated", 1),
            new NbtIntArray("HeightMap", heightMap),
            sections,
            entities,
            tileEntities,
            new NbtLong("RandomSeed", 0),
        };
    }

    private static byte[] CreateFilledArray(int size, byte value)
    {
        byte[] result = new byte[size];
        Array.Fill(result, value);
        return result;
    }

    private static byte GetNibble(byte[] data, int index)
    {
        if (data.Length == 0)
            return 0;

        int byteIndex = index >> 1;
        if (byteIndex >= data.Length)
            return 0;

        byte b = data[byteIndex];
        return (byte)((index & 1) == 0 ? (b & 0x0F) : ((b >> 4) & 0x0F));
    }

    private static void SetNibble(byte[] data, int index, byte value)
    {
        int byteIndex = index >> 1;
        if (byteIndex >= data.Length)
            return;

        if ((index & 1) == 0)
            data[byteIndex] = (byte)((data[byteIndex] & 0xF0) | (value & 0x0F));
        else
            data[byteIndex] = (byte)((data[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
    }

    private static int ExportPlayers(LceSaveDataReader saveReader, string outputDir)
    {
        string playersDir = Path.Combine(outputDir, "players");
        Directory.CreateDirectory(playersDir);

        int count = 0;
        foreach (var entry in saveReader.EnumerateEntries())
        {
            if (!entry.Name.StartsWith("players/", StringComparison.OrdinalIgnoreCase) || !entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!saveReader.TryGetFileBytes(entry.Name, out byte[] bytes))
                continue;

            string filename = Path.GetFileName(entry.Name.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(Path.Combine(playersDir, filename), bytes);
            count++;
        }

        return count;
    }

    private static bool TryMatchRegionEntry(string entryName, string dimension, out int rx, out int rz)
    {
        rx = 0;
        rz = 0;

        string normalized = entryName.Replace('\\', '/');

        string filename = normalized;
        if (dimension == "overworld")
        {
            if (!filename.StartsWith("r.", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        else if (dimension == "nether")
        {
            if (filename.StartsWith("DIM-1/", StringComparison.OrdinalIgnoreCase))
                filename = filename[6..];
            else if (filename.StartsWith("DIM-1", StringComparison.OrdinalIgnoreCase))
                filename = filename[5..];
            else
                return false;
        }
        else if (dimension == "end")
        {
            if (filename.StartsWith("DIM1/", StringComparison.OrdinalIgnoreCase))
                filename = filename[5..];
            else if (filename.StartsWith("DIM1", StringComparison.OrdinalIgnoreCase))
                filename = filename[4..];
            else
                return false;
        }

        filename = Path.GetFileName(filename);
        string[] parts = filename.Split('.');
        if (parts.Length != 4 || !parts[0].Equals("r", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(parts[1], out rx) && int.TryParse(parts[2], out rz);
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

                int jrx = FloorDiv(jx, 32);
                int jrz = FloorDiv(jz, 32);

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
                    // Prefer the legacy root-NBT chunk path for Java -> LCE conversion.
                    // The runtime upgrades this format itself and it is proving more
                    // compatible for mixed-version/upgraded worlds than our custom
                    // compressed-chunk writer.
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

                int lrx = FloorDiv(lcx, 32);
                int lrz = FloorDiv(lcz, 32);

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
        return LceChunkPayloadCodec.ForceChunkCoordinates(chunkData, expectedX, expectedZ);
    }
}
