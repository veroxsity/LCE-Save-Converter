using fNbt;

namespace LceWorldConverter;

public static class SaveDataInspector
{
    public static void ScanJavaWorld(string worldPath, int top = 10)
    {
        using var reader = new JavaWorldReader(worldPath);
        var results = new List<(int chunkX, int chunkZ, int nonAir, int maxY)>();

        foreach (var (_, _, regionPath) in reader.GetRegionFiles())
        {
            for (int localZ = 0; localZ < 32; localZ++)
            {
                for (int localX = 0; localX < 32; localX++)
                {
                    NbtCompound? root;
                    try
                    {
                        root = reader.ReadChunkNbt(regionPath, localX, localZ);
                    }
                    catch
                    {
                        continue;
                    }

                    if (root == null)
                        continue;

                    var level = root.Get<NbtCompound>("Level") ?? root;
                    var blocks = level.Get<NbtByteArray>("Blocks")?.Value;
                    if (blocks == null || blocks.Length < 32768)
                        continue;

                    int nonAir = 0;
                    int maxY = 0;
                    for (int y = 0; y < 128; y++)
                    {
                        bool layerHasSolid = false;
                        for (int z = 0; z < 16; z++)
                        {
                            for (int x = 0; x < 16; x++)
                            {
                                int idx = y * 256 + z * 16 + x;
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

                    int chunkX = level.Get<NbtInt>("xPos")?.Value ?? 0;
                    int chunkZ = level.Get<NbtInt>("zPos")?.Value ?? 0;
                    results.Add((chunkX, chunkZ, nonAir, maxY));
                }
            }
        }

        if (results.Count == 0)
        {
            Console.WriteLine("No readable chunks found in Java world.");
            return;
        }

        Console.WriteLine($"Top {Math.Min(top, results.Count)} densest chunks in {worldPath}:");
        foreach (var chunk in results
            .OrderByDescending(r => r.nonAir)
            .ThenByDescending(r => r.maxY)
            .Take(top))
        {
            int blockX = chunk.chunkX * 16 + 8;
            int blockZ = chunk.chunkZ * 16 + 8;
            Console.WriteLine($"  chunk=({chunk.chunkX},{chunk.chunkZ}) block~({blockX}, {chunk.maxY + 1}, {blockZ}) nonAir={chunk.nonAir} maxY={chunk.maxY}");
        }
    }

    public static void InspectJavaRegion(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Console.WriteLine($"Region file: {path}");
        Console.WriteLine($"Size: {bytes.Length} bytes");

        for (int index = 0; index < 1024; index++)
        {
            int entryOffset = index * 4;
            uint offsetEntry = ReadUInt32BigEndian(bytes, entryOffset);
            if (offsetEntry == 0)
                continue;

            int sectorOffset = (int)(offsetEntry >> 8);
            int chunkPos = sectorOffset * 4096;
            if (chunkPos + 5 > bytes.Length)
                continue;

            int length = (int)ReadUInt32BigEndian(bytes, chunkPos);
            byte compressionType = bytes[chunkPos + 4];
            if (length <= 1 || chunkPos + 5 + (length - 1) > bytes.Length)
                continue;

            byte[] compressed = new byte[length - 1];
            Buffer.BlockCopy(bytes, chunkPos + 5, compressed, 0, length - 1);

            byte[] decompressed;
            try
            {
                decompressed = compressionType switch
                {
                    1 => DecompressGZip(compressed),
                    2 => DecompressZlib(compressed),
                    _ => Array.Empty<byte>(),
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chunk {index}: decompress failed: {ex.Message}");
                continue;
            }

            if (decompressed.Length == 0)
                continue;

            try
            {
                var nbtFile = new fNbt.NbtFile();
                nbtFile.LoadFromBuffer(decompressed, 0, decompressed.Length, fNbt.NbtCompression.None);
                var root = nbtFile.RootTag;
                var level = root.Get<fNbt.NbtCompound>("Level") ?? root;
                var blocks = level.Get<fNbt.NbtByteArray>("Blocks")?.Value;

                int nonAir = 0;
                int maxSolidY = 0;
                if (blocks != null && blocks.Length >= 32768)
                {
                    for (int y = 0; y < 128; y++)
                    {
                        bool layerHasSolid = false;
                        for (int z = 0; z < 16; z++)
                        {
                            for (int x = 0; x < 16; x++)
                            {
                                int idx = y * 256 + z * 16 + x;
                                if (blocks[idx] != 0)
                                {
                                    nonAir++;
                                    layerHasSolid = true;
                                }
                            }
                        }

                        if (layerHasSolid)
                            maxSolidY = y;
                    }
                }

                int xPos = level.Get<fNbt.NbtInt>("xPos")?.Value ?? 0;
                int zPos = level.Get<fNbt.NbtInt>("zPos")?.Value ?? 0;
                Console.WriteLine($"Chunk index {index}: x={xPos}, z={zPos}, nonAir={nonAir}, maxSolidY={maxSolidY}, decomp={decompressed.Length}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chunk {index}: NBT parse failed: {ex.Message}");
            }
        }

        Console.WriteLine("No readable chunk found.");
    }

    public static void Inspect(string path)
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
            uint length = BitConverter.ToUInt32(raw, pos);
            pos += 4;
            uint startOff = BitConverter.ToUInt32(raw, pos);
            pos += 4;
            pos += 8;
            Console.WriteLine($"  [{i}] \"{name}\" offset={startOff} len={length}");
        }

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

                    try
                    {
                        byte[] levelBytes = new byte[len];
                        Buffer.BlockCopy(raw, (int)off, levelBytes, 0, (int)len);
                        var levelFile = new fNbt.NbtFile();
                        levelFile.LoadFromBuffer(levelBytes, 0, levelBytes.Length, fNbt.NbtCompression.None);

                        var data = levelFile.RootTag.Get<fNbt.NbtCompound>("Data") ?? levelFile.RootTag;
                        int spawnX = data.Get<fNbt.NbtInt>("SpawnX")?.Value ?? 0;
                        int spawnY = data.Get<fNbt.NbtInt>("SpawnY")?.Value ?? 64;
                        int spawnZ = data.Get<fNbt.NbtInt>("SpawnZ")?.Value ?? 0;
                        string generator = data.Get<fNbt.NbtString>("generatorName")?.Value ?? "<missing>";
                        string levelName = data.Get<fNbt.NbtString>("LevelName")?.Value ?? "<missing>";
                        int xzSize = data.Get<fNbt.NbtInt>("XZSize")?.Value
                            ?? data.Get<fNbt.NbtInt>("xzSize")?.Value
                            ?? 0;

                        Console.WriteLine($"  LevelName: {levelName}");
                        Console.WriteLine($"  Generator: {generator}");
                        Console.WriteLine($"  Spawn: ({spawnX}, {spawnY}, {spawnZ})");
                        Console.WriteLine($"  XZSize: {xzSize}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Could not parse level.dat NBT: {ex.Message}");
                    }
                }
            }
        }

        pos = (int)headerOffset;
        for (int i = 0; i < (int)fileCount; i++)
        {
            string name = System.Text.Encoding.Unicode.GetString(raw, pos, 128).TrimEnd('\0');
            uint len = BitConverter.ToUInt32(raw, pos + 128);
            uint off = BitConverter.ToUInt32(raw, pos + 132);
            pos += 144;

            if (!name.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase) || len <= 8192)
                continue;

            Console.WriteLine($"\n=== Deep inspect: {name} ===");
            for (int chunkIndex = 0; chunkIndex < 1024; chunkIndex++)
            {
                uint chunkOff = BitConverter.ToUInt32(raw, (int)off + chunkIndex * 4);
                if (chunkOff == 0)
                    continue;

                uint sectorNum = chunkOff >> 8;
                int chunkDataPos = (int)(off + sectorNum * 4096);

                uint compLenRaw = BitConverter.ToUInt32(raw, chunkDataPos);
                bool rleFlag = (compLenRaw & 0x80000000) != 0;
                uint compLen = compLenRaw & 0x7FFFFFFF;
                uint decompLen = BitConverter.ToUInt32(raw, chunkDataPos + 4);

                Console.WriteLine($"  Chunk index {chunkIndex}: compLen={compLen} decompLen={decompLen} rle={rleFlag}");

                byte[] compData = new byte[compLen];
                Buffer.BlockCopy(raw, chunkDataPos + 8, compData, 0, (int)compLen);

                try
                {
                    byte[] decompData = LceCompression.Decompress(compData, (int)decompLen);
                    Console.WriteLine($"  Decompressed OK: {decompData.Length} bytes");
                    Console.WriteLine($"  First 32 bytes: {BitConverter.ToString(decompData, 0, Math.Min(32, decompData.Length))}");

                    try
                    {
                        var nbtFile = new fNbt.NbtFile();
                        nbtFile.LoadFromBuffer(decompData, 0, decompData.Length, fNbt.NbtCompression.None);
                        var root = nbtFile.RootTag;
                        Console.WriteLine($"  NBT root: {root.Name} ({root.TagType})");
                        foreach (var tag in root)
                            Console.WriteLine($"    {tag.Name}: {tag.TagType}");

                        var level = root.Get<fNbt.NbtCompound>("Level");
                        if (level != null)
                        {
                            Console.WriteLine("  Level compound children:");
                            foreach (var tag in level)
                                Console.WriteLine($"    {tag.Name}: {tag.TagType} {(tag is fNbt.NbtByteArray ba ? $"[{ba.Value.Length}]" : tag is fNbt.NbtInt ni ? $"={ni.Value}" : string.Empty)}");

                            var blocks = level.Get<fNbt.NbtByteArray>("Blocks")?.Value;
                            if (blocks != null && blocks.Length >= 32768)
                            {
                                int nonAir = 0;
                                int maxY = 0;
                                for (int y = 0; y < 128; y++)
                                {
                                    bool layerHasSolid = false;
                                    for (int z = 0; z < 16; z++)
                                    {
                                        for (int x = 0; x < 16; x++)
                                        {
                                            int idx = y * 256 + z * 16 + x;
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

                                Console.WriteLine($"  Chunk stats: nonAir={nonAir}, maxSolidY={maxY}");
                            }
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

                break;
            }

            break;
        }
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24)
            | ((uint)bytes[offset + 1] << 16)
            | ((uint)bytes[offset + 2] << 8)
            | bytes[offset + 3];
    }

    private static byte[] DecompressGZip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}