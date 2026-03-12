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
public class JavaWorldReader : IDisposable
{
    private readonly string _worldPath;
    private readonly Dictionary<string, RegionReader> _regionReaders = new(StringComparer.OrdinalIgnoreCase);

    public JavaWorldReader(string worldPath)
    {
        _worldPath = worldPath;
    }

    public void Dispose()
    {
        foreach (var (_, reader) in _regionReaders)
            reader.Dispose();
        _regionReaders.Clear();
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
        return GetOrCreateRegionReader(regionFilePath).ReadChunkNbt(localX, localZ);
    }

    /// <summary>
    /// Checks if a region file has a chunk at the given local coords.
    /// </summary>
    public bool HasChunk(string regionFilePath, int localX, int localZ)
    {
        return GetOrCreateRegionReader(regionFilePath).HasChunk(localX, localZ);
    }

    private RegionReader GetOrCreateRegionReader(string regionFilePath)
    {
        if (!_regionReaders.TryGetValue(regionFilePath, out var reader))
        {
            reader = new RegionReader(regionFilePath);
            _regionReaders[regionFilePath] = reader;
        }

        return reader;
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

    private sealed class RegionReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly uint[] _offsetTable = new uint[1024];

        public RegionReader(string regionFilePath)
        {
            _stream = new FileStream(regionFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);

            // Cache the 1024 offset entries from the region header once.
            _stream.Seek(0, SeekOrigin.Begin);
            for (int i = 0; i < 1024; i++)
                _offsetTable[i] = ReadBigEndianUInt32(_reader);
        }

        public bool HasChunk(int localX, int localZ)
        {
            int index = (localX & 31) + (localZ & 31) * 32;
            return _offsetTable[index] != 0;
        }

        public NbtCompound? ReadChunkNbt(int localX, int localZ)
        {
            int index = (localX & 31) + (localZ & 31) * 32;
            uint offsetEntry = _offsetTable[index];
            if (offsetEntry == 0)
                return null;

            uint sectorOffset = offsetEntry >> 8;
            long chunkPos = sectorOffset * 4096L;
            if (chunkPos + 5 > _stream.Length)
                return null;

            _stream.Seek(chunkPos, SeekOrigin.Begin);
            uint length = ReadBigEndianUInt32(_reader);
            byte compressionType = _reader.ReadByte();
            if (length <= 1)
                return null;

            int compressedLength = (int)length - 1;
            if (compressedLength < 0 || chunkPos + 5 + compressedLength > _stream.Length)
                return null;

            byte[] compressedData = _reader.ReadBytes(compressedLength);
            if (compressedData.Length != compressedLength)
                return null;

            byte[] decompressed = compressionType switch
            {
                1 => DecompressGZip(compressedData),
                2 => DecompressZlib(compressedData),
                _ => Array.Empty<byte>(),
            };

            if (decompressed.Length == 0)
                return null;

            var nbtFile = new NbtFile();
            nbtFile.LoadFromBuffer(decompressed, 0, decompressed.Length, NbtCompression.None);
            return nbtFile.RootTag;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
