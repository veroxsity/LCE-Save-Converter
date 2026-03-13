namespace LceWorldConverter;

public static class SaveDataInspector
{
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
}