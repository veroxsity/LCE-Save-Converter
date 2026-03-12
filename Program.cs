using fNbt;

namespace LceWorldConverter;

class Program
{
    // Use pre-v8 save format so chunk payloads are loaded via NBT path, which is the most stable conversion target.
    private const short OutputSaveVersion = 7;

    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--inspect")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LceWorldConverter --inspect <saveData.ms_path>");
                return;
            }

            InspectSaveFile(args[1]);
            return;
        }

        Console.WriteLine("=== LCE World Converter ===");
        Console.WriteLine("Converts Java Edition worlds to Minecraft Legacy Console Edition (WIN64) format.\n");

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: LceWorldConverter <java_world_path> [output_dir] [--large-world] [--all-dimensions]");
            Console.WriteLine();
            Console.WriteLine("  java_world_path  Path to Java Edition world folder (containing level.dat)");
            Console.WriteLine("  output_dir       Optional: directory to write saveData.ms into.");
            Console.WriteLine("                   Defaults to a folder named after the world in the current directory.");
            Console.WriteLine("  --large-world    Use 320-chunk (5120 block) world size instead of 54-chunk (864 block)");
            Console.WriteLine("  --all-dimensions Convert Nether and End in addition to Overworld (experimental)");
            return;
        }

        string javaWorldPath = args[0];

        // Determine output directory and path
        // If next arg is provided and isn't a flag, treat it as the output directory
        string? outputDirArg = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
        bool largeWorld = args.Contains("--large-world");
        bool convertAllDimensions = args.Contains("--all-dimensions");

        string worldName = Path.GetFileName(javaWorldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string outputDir = outputDirArg ?? Path.Combine(Directory.GetCurrentDirectory(), worldName);
        Directory.CreateDirectory(outputDir);
        string outputPath = Path.Combine(outputDir, "saveData.ms");

        if (!Directory.Exists(javaWorldPath))
        {
            Console.Error.WriteLine($"Error: Java world directory not found: {javaWorldPath}");
            return;
        }

        string levelDatPath = Path.Combine(javaWorldPath, "level.dat");
        if (!File.Exists(levelDatPath))
        {
            Console.Error.WriteLine($"Error: level.dat not found in: {javaWorldPath}");
            return;
        }

        int xzSize = largeWorld ? 320 : 54;
        int halfSize = xzSize / 2;
        int hellScale = 3;
        int hellHalfSize = (xzSize / hellScale) / 2;
        int endHalfSize = 9; // End is always 18x18 chunks

        Console.WriteLine($"Source:      {javaWorldPath}");
        Console.WriteLine($"Output:      {outputPath}");
        Console.WriteLine($"World size:  {xzSize} chunks ({xzSize * 16} blocks)");
        Console.WriteLine();

        // Start a fresh per-run mapping audit for modern palette entries that still fall back to air.
        ChunkConverter.ResetUnknownModernBlocks();

        try
        {
            using var reader = new JavaWorldReader(javaWorldPath);

            // Step 1: Read Java level.dat
            Console.Write("Reading level.dat... ");
            var javaLevelDat = reader.ReadLevelDat();
            var javaData = javaLevelDat.Get<NbtCompound>("Data")!;
            Console.WriteLine("OK");

            // Step 2: Compute recentring offset from spawn
            int spawnX = javaData.Get<NbtInt>("SpawnX")?.Value ?? 0;
            int spawnZ = javaData.Get<NbtInt>("SpawnZ")?.Value ?? 0;
            int spawnChunkX = spawnX >> 4;
            int spawnChunkZ = spawnZ >> 4;
            Console.WriteLine($"Java spawn:  ({spawnX}, {spawnZ}) -> chunk ({spawnChunkX}, {spawnChunkZ})");
            Console.WriteLine($"Recentring:  Java chunk ({spawnChunkX},{spawnChunkZ}) -> LCE chunk (0,0)");
            Console.WriteLine();

            // Step 3: Create output container
            var container = new SaveDataContainer(OutputSaveVersion);

            // Pre-create default region file entries in the order the real game expects:
            // DIM-1 (nether) first, then DIM1 (end), then overworld
            // From McRegionChunkStorage.cpp and real save analysis
            string[] defaultRegionOrder = {
                "DIM-1/r.-1.-1.mcr", "DIM-1/r.0.-1.mcr", "DIM-1/r.0.0.mcr", "DIM-1/r.-1.0.mcr",
                "DIM1/r.-1.-1.mcr", "DIM1/r.0.-1.mcr", "DIM1/r.0.0.mcr", "DIM1/r.-1.0.mcr",
                "r.-1.-1.mcr", "r.0.-1.mcr", "r.0.0.mcr", "r.-1.0.mcr"
            };
            foreach (var name in defaultRegionOrder)
                container.CreateFile(name);

            // Step 4: Convert overworld chunks
            Console.WriteLine($"Converting overworld ({xzSize}x{xzSize} chunks)...");
            int owConverted = ConvertDimension(reader, container, "",
                halfSize, spawnChunkX, spawnChunkZ);
            Console.WriteLine($"  {owConverted} chunks converted");

            // Step 6: Convert nether chunks
            int netherConverted = 0;
            int endConverted = 0;
            if (convertAllDimensions)
            {
                Console.WriteLine($"Converting nether ({xzSize / hellScale}x{xzSize / hellScale} chunks)...");
                int netherOffsetChunkX = FloorDiv(spawnChunkX, 8);
                int netherOffsetChunkZ = FloorDiv(spawnChunkZ, 8);
                netherConverted = ConvertDimension(reader, container, "DIM-1",
                    hellHalfSize, netherOffsetChunkX, netherOffsetChunkZ);
                Console.WriteLine($"  {netherConverted} chunks converted");

                // Step 7: Convert End chunks
                Console.WriteLine($"Converting end ({endHalfSize * 2}x{endHalfSize * 2} chunks)...");
                // End is converted around 0,0 (legacy End spawn/platform centered near origin).
                endConverted = ConvertDimension(reader, container, "DIM1",
                    endHalfSize, 0, 0);
                Console.WriteLine($"  {endConverted} chunks converted");
            }
            else
            {
                Console.WriteLine("Skipping nether/end conversion (use --all-dimensions to enable).");
            }

            // Step 8: Write LCE level.dat (after region files, matching real save order)
            Console.Write("Converting level.dat... ");
            byte[] lceLevelDat = LevelDatConverter.Convert(javaLevelDat, spawnChunkX, spawnChunkZ, largeWorld);
            var levelDatEntry = container.CreateFile("level.dat");
            container.WriteToFile(levelDatEntry, lceLevelDat);
            Console.WriteLine("OK");

            // Step 9: Copy and remap player data
            int blockOffsetX = spawnChunkX * 16;
            int blockOffsetZ = spawnChunkZ * 16;
            Console.Write("Copying player data... ");
            int playersCopied = CopyPlayers(javaWorldPath, container, blockOffsetX, blockOffsetZ);
            Console.WriteLine($"{playersCopied} player(s)");

            // Step 10: Save output
            Console.Write("Writing saveData.ms... ");
            container.Save(outputPath);
            Console.WriteLine("OK");

            Console.WriteLine();
            Console.WriteLine($"Conversion complete!");
            Console.WriteLine($"  Overworld: {owConverted} chunks");
            Console.WriteLine($"  Nether:    {netherConverted} chunks");
            Console.WriteLine($"  End:       {endConverted} chunks");
            Console.WriteLine($"  Output:    {outputPath}");

            var unknownBlocks = ChunkConverter.GetUnknownModernBlocksSnapshot();
            if (unknownBlocks.Count > 0)
            {
                string unknownPath = Path.Combine(outputDir, "unknown-modern-blocks.txt");
                File.WriteAllLines(unknownPath, unknownBlocks);

                Console.WriteLine();
                Console.WriteLine($"Unknown modern blocks mapped to air: {unknownBlocks.Count}");
                foreach (string blockName in unknownBlocks.Take(40))
                    Console.WriteLine($"  - {blockName}");
                if (unknownBlocks.Count > 40)
                    Console.WriteLine($"  ... and {unknownBlocks.Count - 40} more");
                Console.WriteLine($"Full list written to: {unknownPath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nError during conversion: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        int q = value / divisor;
        int r = value % divisor;
        if (r != 0 && value < 0)
            q--;
        return q;
    }

    /// <summary>
    /// Copies player .dat files from the Java world into the saveData.ms container.
    /// Remaps the player's absolute position and set-spawn by subtracting the world recentring offset.
    /// Only legacy players/ files are copied; modern playerdata/ files are not TU19-compatible.
    /// </summary>
    static int CopyPlayers(string javaWorldPath, SaveDataContainer container, int blockOffsetX, int blockOffsetZ)
    {
        // Only import legacy player files. Modern UUID playerdata can crash TU19 loaders.
        string[] candidateDirs = {
            Path.Combine(javaWorldPath, "players")
        };

        int count = 0;
        foreach (var dir in candidateDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var filePath in Directory.GetFiles(dir, "*.dat"))
            {
                try
                {
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    var player = nbtFile.RootTag;

                    // Remap absolute world position
                    var pos = player.Get<NbtList>("Pos");
                    if (pos != null && pos.Count >= 3)
                    {
                        ((NbtDouble)pos[0]).Value -= blockOffsetX;
                        ((NbtDouble)pos[2]).Value -= blockOffsetZ;
                    }

                    // Remap set-home spawn (only valid if SpawnForced = 1 or SpawnX/Z are non-zero)
                    var spawnX = player.Get<NbtInt>("SpawnX");
                    var spawnZ = player.Get<NbtInt>("SpawnZ");
                    if (spawnX != null) spawnX.Value -= blockOffsetX;
                    if (spawnZ != null) spawnZ.Value -= blockOffsetZ;

                    // Serialise back to GZip NBT bytes
                    using var ms = new MemoryStream();
                    nbtFile.SaveToStream(ms, NbtCompression.GZip);
                    byte[] remapped = ms.ToArray();

                    // Store as players/<filename> in the container
                    string entryName = "players/" + Path.GetFileName(filePath);
                    var entry = container.CreateFile(entryName);
                    container.WriteToFile(entry, remapped);
                    count++;
                }
                catch
                {
                    // Skip unreadable player files
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Converts all chunks within the LCE bounds for a given dimension.
    /// dimensionPrefix: "" for overworld, "DIM-1" for nether, "DIM1" for end
    /// </summary>
    static int ConvertDimension(
        JavaWorldReader reader,
        SaveDataContainer container,
        string dimensionPrefix,
        int halfSize,
        int offsetChunkX,
        int offsetChunkZ)
    {
        // Build a lookup of available Java region files
        var regionFiles = reader.GetRegionFiles(dimensionPrefix);
        var regionLookup = new Dictionary<(int, int), string>();
        foreach (var (rx, rz, path) in regionFiles)
            regionLookup[(rx, rz)] = path;

        // LCE region file prefix inside saveData.ms
        // From save naming: Nether = "DIM-1/", End = "DIM1/".
        string lcePrefix;
        if (dimensionPrefix == "DIM-1")
            lcePrefix = "DIM-1/";     // Nether: DIM-1/r.X.Z.mcr
        else if (dimensionPrefix == "DIM1")
            lcePrefix = "DIM1/";      // End: DIM1/r.X.Z.mcr
        else
            lcePrefix = "";           // Overworld: r.X.Z.mcr

        // Cache of open LCE region files
        var lceRegions = new Dictionary<(int, int), LceRegionFile>();
        int converted = 0;

        // Iterate LCE coordinate space
        for (int lcx = -halfSize; lcx < halfSize; lcx++)
        {
            for (int lcz = -halfSize; lcz < halfSize; lcz++)
            {
                // Map LCE chunk coord back to Java chunk coord
                int jx = lcx + offsetChunkX;
                int jz = lcz + offsetChunkZ;

                // Which Java region file contains this chunk?
                int jrx = jx >> 5;
                int jrz = jz >> 5;

                if (!regionLookup.TryGetValue((jrx, jrz), out string? regionPath))
                    continue; // No region file for this area

                // Local coords within the Java region
                int localX = ((jx % 32) + 32) % 32;
                int localZ = ((jz % 32) + 32) % 32;

                // Read the Java chunk
                NbtCompound? chunkNbt;
                try
                {
                    chunkNbt = reader.ReadChunkNbt(regionPath, localX, localZ);
                }
                catch
                {
                    continue; // Corrupted chunk, skip
                }
                if (chunkNbt == null) continue;

                // Convert chunk NBT to LCE format
                byte[] lceChunkData;
                try
                {
                    lceChunkData = ChunkConverter.ConvertChunk(chunkNbt, lcx, lcz);
                    lceChunkData = NormalizeChunkCoordinates(lceChunkData, lcx, lcz);
                    if (lceChunkData.Length == 0) continue;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error converting chunk ({lcx},{lcz}) from Java ({jx},{jz}): {ex.Message}");
                    throw;
                }

                // Which LCE region file does this chunk go into?
                int lrx = lcx >> 5;
                int lrz = lcz >> 5;

                if (!lceRegions.TryGetValue((lrx, lrz), out var lceRegion))
                {
                    string regionName = $"{lcePrefix}r.{lrx}.{lrz}.mcr";
                    lceRegion = new LceRegionFile(regionName);
                    lceRegions[(lrx, lrz)] = lceRegion;
                }

                // Local coords within the LCE region
                int lceLocalX = ((lcx % 32) + 32) % 32;
                int lceLocalZ = ((lcz % 32) + 32) % 32;

                lceRegion.WriteChunk(lceLocalX, lceLocalZ, lceChunkData);
                converted++;
            }

            // Progress indicator
            if ((lcx + halfSize) % 10 == 0)
            {
                int progress = (int)(((double)(lcx + halfSize) / (halfSize * 2)) * 100);
                Console.Write($"\r  Progress: {progress}%  ");
            }
        }
        Console.WriteLine($"\r  Progress: 100%  ");

        // Write all buffered region files to the container
        foreach (var (_, region) in lceRegions)
            region.WriteTo(container);

        return converted;
    }

    private static byte[] NormalizeChunkCoordinates(byte[] chunkData, int expectedX, int expectedZ)
    {
        using var ms = new MemoryStream(chunkData);
        var file = new NbtFile();
        file.LoadFromStream(ms, NbtCompression.None);

        var level = file.RootTag.Get<NbtCompound>("Level");
        if (level == null)
            return chunkData;

        var xPos = level.Get<NbtInt>("xPos");
        var zPos = level.Get<NbtInt>("zPos");

        if (xPos != null) xPos.Value = expectedX;
        else level.Add(new NbtInt("xPos", expectedX));

        if (zPos != null) zPos.Value = expectedZ;
        else level.Add(new NbtInt("zPos", expectedZ));

        using var outMs = new MemoryStream();
        file.SaveToStream(outMs, NbtCompression.None);
        return outMs.ToArray();
    }

    static void InspectSaveFile(string path)
    {
        byte[] fileBytes = File.ReadAllBytes(path);
        Console.WriteLine($"File size: {fileBytes.Length} bytes");
        Console.WriteLine($"First 16 bytes: {BitConverter.ToString(fileBytes, 0, 16)}");
        Console.WriteLine($"Bytes 0-3 (compressed flag): {BitConverter.ToInt32(fileBytes, 0)}");
        Console.WriteLine($"Bytes 4-7 (decomp size): {BitConverter.ToInt32(fileBytes, 4)}");
        Console.WriteLine();

        using var compStream = new MemoryStream(fileBytes, 8, fileBytes.Length - 8);
        using var zlib = new System.IO.Compression.ZLibStream(compStream, System.IO.Compression.CompressionMode.Decompress);
        using var outStream = new MemoryStream();
        zlib.CopyTo(outStream);
        byte[] raw = outStream.ToArray();

        Console.WriteLine($"Decompressed size: {raw.Length} bytes");
        Console.WriteLine($"First 16 bytes: {BitConverter.ToString(raw, 0, Math.Min(16, raw.Length))}");
        Console.WriteLine();

        uint headerOffset = BitConverter.ToUInt32(raw, 0);
        uint fileCount = BitConverter.ToUInt32(raw, 4);
        short origVer = BitConverter.ToInt16(raw, 8);
        short currVer = BitConverter.ToInt16(raw, 10);

        Console.WriteLine($"Header/footer offset: {headerOffset}");
        Console.WriteLine($"File count: {fileCount}");
        Console.WriteLine($"Original save version: {origVer}");
        Console.WriteLine($"Current save version: {currVer}");
        Console.WriteLine();

        Console.WriteLine("=== File Table ===");
        int pos = (int)headerOffset;
        for (int i = 0; i < (int)Math.Min(fileCount, 50); i++)
        {
            string name = System.Text.Encoding.Unicode.GetString(raw, pos, 128).TrimEnd('\0');
            pos += 128;
            uint length = BitConverter.ToUInt32(raw, pos); pos += 4;
            uint startOff = BitConverter.ToUInt32(raw, pos); pos += 4;
            long lastMod = BitConverter.ToInt64(raw, pos); pos += 8;
            Console.WriteLine($"  [{i}] \"{name}\" offset={startOff} len={length}");
        }

        // Check level.dat contents
        if (fileCount > 0)
        {
            pos = (int)headerOffset;
            for (int i = 0; i < (int)fileCount; i++)
            {
                string name = System.Text.Encoding.Unicode.GetString(raw, pos, 128).TrimEnd('\0');
                uint len = BitConverter.ToUInt32(raw, pos + 128);
                uint off = BitConverter.ToUInt32(raw, pos + 132);
                pos += 144;

                if (name == "level.dat" && len > 0)
                {
                    Console.WriteLine($"\n=== level.dat first 16 bytes at offset {off} ===");
                    Console.WriteLine($"  {BitConverter.ToString(raw, (int)off, Math.Min(16, (int)len))}");
                }
            }
        }

        // Decompress and inspect a real chunk from the first region file
        pos = (int)headerOffset;
        for (int i = 0; i < (int)fileCount; i++)
        {
            string name = System.Text.Encoding.Unicode.GetString(raw, pos, 128).TrimEnd('\0');
            uint len = BitConverter.ToUInt32(raw, pos + 128);
            uint off = BitConverter.ToUInt32(raw, pos + 132);
            pos += 144;

            if (!name.EndsWith(".mcr") || len <= 8192) continue;

            Console.WriteLine($"\n=== Deep inspect: {name} ===");
            // Find first chunk
            for (int ci = 0; ci < 1024; ci++)
            {
                uint chunkOff = BitConverter.ToUInt32(raw, (int)off + ci * 4);
                if (chunkOff == 0) continue;

                uint sectorNum = chunkOff >> 8;
                int chunkDataPos = (int)(off + sectorNum * 4096);

                uint compLenRaw = BitConverter.ToUInt32(raw, chunkDataPos);
                bool rleFlag = (compLenRaw & 0x80000000) != 0;
                uint compLen = compLenRaw & 0x7FFFFFFF;
                uint decompLen = BitConverter.ToUInt32(raw, chunkDataPos + 4);

                Console.WriteLine($"  Chunk index {ci}: compLen={compLen} decompLen={decompLen} rle={rleFlag}");

                // Extract compressed data
                byte[] compData = new byte[compLen];
                Buffer.BlockCopy(raw, chunkDataPos + 8, compData, 0, (int)compLen);

                try
                {
                    byte[] decompData = LceCompression.Decompress(compData, (int)decompLen);
                    Console.WriteLine($"  Decompressed OK: {decompData.Length} bytes");
                    Console.WriteLine($"  First 32 bytes: {BitConverter.ToString(decompData, 0, Math.Min(32, decompData.Length))}");

                    // Try parsing as NBT
                    try
                    {
                        var nbtFile = new fNbt.NbtFile();
                        nbtFile.LoadFromBuffer(decompData, 0, decompData.Length, fNbt.NbtCompression.None);
                        var root = nbtFile.RootTag;
                        Console.WriteLine($"  NBT root: {root.Name} ({root.TagType})");
                        foreach (var tag in root)
                            Console.WriteLine($"    {tag.Name}: {tag.TagType}");
                        // If it has Level compound, show its children
                        var level = root.Get<fNbt.NbtCompound>("Level");
                        if (level != null)
                        {
                            Console.WriteLine($"  Level compound children:");
                            foreach (var tag in level)
                                Console.WriteLine($"    {tag.Name}: {tag.TagType} {(tag is fNbt.NbtByteArray ba ? $"[{ba.Value.Length}]" : tag is fNbt.NbtInt ni ? $"={ni.Value}" : "")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  NBT parse FAILED: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Decompress FAILED: {ex.Message}");
                }
                break; // Only inspect first chunk
            }
            break; // Only inspect first region
        }
    }
}
