using fNbt;

namespace LceWorldConverter;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--inspect")
        {
            InspectSaveFile(args[1]);
            return;
        }

        Console.WriteLine("=== LCE World Converter ===");
        Console.WriteLine("Converts Java Edition worlds to Minecraft Legacy Console Edition (WIN64) format.\n");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: LceWorldConverter <java_world_path> <output_savedata.ms> [--large-world]");
            Console.WriteLine();
            Console.WriteLine("  java_world_path    Path to Java Edition world folder (containing level.dat)");
            Console.WriteLine("  output_savedata.ms Path for the output LCE save file");
            Console.WriteLine("  --large-world      Use 320-chunk (5120 block) world size instead of 54-chunk (864 block)");
            return;
        }

        string javaWorldPath = args[0];
        string outputPath = args[1];
        bool largeWorld = args.Length > 2 && args[2] == "--large-world";

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

        try
        {
            var reader = new JavaWorldReader(javaWorldPath);

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
            var container = new SaveDataContainer();

            // Step 4: Write LCE level.dat
            Console.Write("Converting level.dat... ");
            byte[] lceLevelDat = LevelDatConverter.Convert(javaLevelDat, spawnChunkX, spawnChunkZ, largeWorld);
            var levelDatEntry = container.CreateFile("level.dat");
            container.WriteToFile(levelDatEntry, lceLevelDat);
            Console.WriteLine("OK");

            // Step 5: Convert overworld chunks
            Console.WriteLine($"Converting overworld ({xzSize}x{xzSize} chunks)...");
            int owConverted = ConvertDimension(reader, container, "",
                halfSize, spawnChunkX, spawnChunkZ);
            Console.WriteLine($"  {owConverted} chunks converted");

            // Step 6: Convert nether chunks
            Console.WriteLine($"Converting nether ({xzSize / hellScale}x{xzSize / hellScale} chunks)...");
            // Nether in Java is at 1:1 chunk coords in DIM-1/
            // LCE nether is 1/hellScale of overworld
            int netherConverted = ConvertDimension(reader, container, "DIM-1",
                hellHalfSize, spawnChunkX / hellScale, spawnChunkZ / hellScale);
            Console.WriteLine($"  {netherConverted} chunks converted");

            // Step 7: Convert End chunks
            Console.WriteLine($"Converting end ({endHalfSize * 2}x{endHalfSize * 2} chunks)...");
            // End doesn't need recentring — it's always around (0,0)
            int endConverted = ConvertDimension(reader, container, "DIM1",
                endHalfSize, 0, 0);
            Console.WriteLine($"  {endConverted} chunks converted");

            // Step 8: Save output
            Console.Write("Writing saveData.ms... ");
            container.Save(outputPath);
            Console.WriteLine("OK");

            Console.WriteLine();
            Console.WriteLine($"Conversion complete!");
            Console.WriteLine($"  Overworld: {owConverted} chunks");
            Console.WriteLine($"  Nether:    {netherConverted} chunks");
            Console.WriteLine($"  End:       {endConverted} chunks");
            Console.WriteLine($"  Output:    {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nError during conversion: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
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

        // LCE region file prefix for nether/end inside saveData.ms
        // Source shows: "DIM-1/" for nether, "DIM1/" for end, "" for overworld
        string lcePrefix = string.IsNullOrEmpty(dimensionPrefix) ? "" : dimensionPrefix + "/";

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
                byte[] lceChunkData = ChunkConverter.ConvertChunk(chunkNbt, lcx, lcz);
                if (lceChunkData.Length == 0) continue;

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

        // Check first region file's chunk data header
        if (fileCount > 1)
        {
            // Find first .mcr entry
            pos = (int)headerOffset;
            for (int i = 0; i < (int)fileCount; i++)
            {
                string name = System.Text.Encoding.Unicode.GetString(raw, pos, 128).TrimEnd('\0');
                uint len = BitConverter.ToUInt32(raw, pos + 128);
                uint off = BitConverter.ToUInt32(raw, pos + 132);
                pos += 144;

                if (name.EndsWith(".mcr") && len > 8192)
                {
                    Console.WriteLine($"\n=== Inspecting region: {name} ===");
                    // Read first non-zero offset from the offset table
                    for (int ci = 0; ci < 1024; ci++)
                    {
                        uint chunkOff = BitConverter.ToUInt32(raw, (int)off + ci * 4);
                        if (chunkOff != 0)
                        {
                            uint sectorNum = chunkOff >> 8;
                            uint sectorCnt = chunkOff & 0xFF;
                            int chunkDataPos = (int)(off + sectorNum * 4096);
                            Console.WriteLine($"  First chunk at offset table index {ci}: sector={sectorNum} count={sectorCnt}");
                            Console.WriteLine($"  Chunk data at byte {chunkDataPos}:");
                            Console.WriteLine($"    First 16 bytes: {BitConverter.ToString(raw, chunkDataPos, 16)}");
                            uint val0 = BitConverter.ToUInt32(raw, chunkDataPos);
                            uint val1 = BitConverter.ToUInt32(raw, chunkDataPos + 4);
                            Console.WriteLine($"    Word 0: 0x{val0:X8} ({val0 & 0x7FFFFFFF} bytes, RLE={((val0 & 0x80000000) != 0)})");
                            Console.WriteLine($"    Word 1: 0x{val1:X8} ({val1} decompressed bytes)");
                            break;
                        }
                    }
                    break;
                }
            }
        }
    }
}
