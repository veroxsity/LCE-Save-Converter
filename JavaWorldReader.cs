using System.IO.Compression;
using fNbt;

namespace LceWorldConverter;

/// <summary>
/// Reads Java Edition world files from the filesystem.
/// Supports McRegion (.mcr) for Java 1.2-1.6.x and Anvil (.mca) for Java 1.7+.
///
/// Java region file layout (identical header for both .mcr and .mca):
///   [4096 bytes] Offset table (1024 x int32, big-endian)
///   [4096 bytes] Timestamp table (1024 x int32, big-endian)
///   [variable]   Chunk data in 4096-byte sectors
///
/// Java chunk data format:
///   [4 bytes] Exact byte length (big-endian)
///   [1 byte]  Compression type: 1=GZip, 2=Deflate/zlib
///   [variable] Compressed NBT data
/// </summary>
public class JavaWorldReader
{
    private readonly string _worldPath;

    public JavaWorldReader(string worldPath)
    {
        _worldPath = worldPath;
    }

    /// <summary>
    /// Reads the level.dat from the Java world (GZip-compressed NBT).
    /// </summary>
    public NbtCompound ReadLevelDat()
    {
        string path = Path.Combine(_worldPath, "level.dat");
        var file = new NbtFile();
        file.LoadFromFile(path);
        return file.RootTag;
    }

    /// <summary>
    /// Gets the region directory for a dimension.
    /// Overworld: world/region/
    /// Nether:    world/DIM-1/region/
    /// End:       world/DIM1/region/
    /// </summary>
    public string GetRegionDir(string dimension = "")
    {
        if (string.IsNullOrEmpty(dimension))
            return Path.Combine(_worldPath, "region");
        return Path.Combine(_worldPath, dimension, "region");
    }

    /// <summary>
    /// Lists all region files for a dimension. Returns (regionX, regionZ) pairs.
    /// Tries .mcr first, then .mca.
    /// </summary>
    public List<(int rx, int rz, string path)> GetRegionFiles(string dimension = "")
    {
        var result = new List<(int, int, string)>();
        string dir = GetRegionDir(dimension);
        if (!Directory.Exists(dir)) return result;

        // Try .mcr first (Java 1.6.x and earlier), then .mca (Java 1.7+)
        var files = Directory.GetFiles(dir, "r.*.*.mcr")
            .Concat(Directory.GetFiles(dir, "r.*.*.mca"))
            .ToArray();

        foreach (var file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // Also strip the second extension for .mcr/.mca
            string[] parts = Path.GetFileName(file).Split('.');
            // Format: r.X.Z.mcr or r.X.Z.mca
            if (parts.Length == 4 && parts[0] == "r" &&
                int.TryParse(parts[1], out int rx) &&
                int.TryParse(parts[2], out int rz))
            {
                result.Add((rx, rz, file));
            }
        }
        return result;
    }

    /// <summary>
    /// Reads a single chunk's NBT from a Java region file.
    /// localX, localZ are 0-31 (position within the region).
    /// Returns null if chunk doesn't exist.
    /// </summary>
    public NbtCompound? ReadChunkNbt(string regionFilePath, int localX, int localZ)
    {
        using var fs = new FileStream(regionFilePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        // Read offset from table (big-endian)
        int index = (localX & 31) + (localZ & 31) * 32;
        fs.Seek(index * 4, SeekOrigin.Begin);
        uint offsetEntry = ReadBigEndianUInt32(reader);

        if (offsetEntry == 0) return null; // Chunk doesn't exist

        uint sectorOffset = offsetEntry >> 8;
        uint sectorCount = offsetEntry & 0xFF;

        // Seek to the chunk data
        fs.Seek(sectorOffset * 4096, SeekOrigin.Begin);

        // Read chunk header (big-endian)
        uint length = ReadBigEndianUInt32(reader);
        byte compressionType = reader.ReadByte();

        if (length <= 1) return null;

        // Read compressed data
        byte[] compressedData = reader.ReadBytes((int)(length - 1));

        // Decompress based on type
        byte[] decompressed;
        switch (compressionType)
        {
            case 1: // GZip
                decompressed = DecompressGZip(compressedData);
                break;
            case 2: // Deflate/zlib
                decompressed = DecompressZlib(compressedData);
                break;
            default:
                Console.Error.WriteLine($"Unknown compression type {compressionType} at chunk ({localX},{localZ})");
                return null;
        }

        // Parse NBT
        var nbtFile = new NbtFile();
        nbtFile.LoadFromBuffer(decompressed, 0, decompressed.Length, NbtCompression.None);
        return nbtFile.RootTag;
    }

    /// <summary>
    /// Checks if a region file has a chunk at the given local coords.
    /// </summary>
    public bool HasChunk(string regionFilePath, int localX, int localZ)
    {
        using var fs = new FileStream(regionFilePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        int index = (localX & 31) + (localZ & 31) * 32;
        fs.Seek(index * 4, SeekOrigin.Begin);
        uint offsetEntry = ReadBigEndianUInt32(reader);
        return offsetEntry != 0;
    }

    /// <summary>
    /// Detects whether this is an Anvil world (.mca) or McRegion (.mcr).
    /// Anvil uses section-based chunks that need flattening for LCE.
    /// </summary>
    public bool IsAnvilWorld()
    {
        string regionDir = GetRegionDir();
        if (!Directory.Exists(regionDir)) return false;
        return Directory.GetFiles(regionDir, "*.mca").Length > 0;
    }

    #region Helpers

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static byte[] DecompressGZip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    #endregion
}
