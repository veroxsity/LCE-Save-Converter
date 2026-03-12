namespace LceWorldConverter;

/// <summary>
/// Builds an LCE-format region file (.mcr) in memory, then writes
/// the completed file to a SaveDataContainer in one shot.
///
/// LCE region file layout:
///   [4096 bytes] Offset table (1024 x int32, little-endian for WIN64)
///   [4096 bytes] Timestamp table (1024 x int32)
///   [variable]   Chunk data in 4096-byte sectors
///
/// Chunk data format (differs from Java):
///   [4 bytes] Compressed length (high bit = RLE flag)
///   [4 bytes] Decompressed length
///   [variable] RLE+zlib compressed data
/// </summary>
public class LceRegionFile
{
    private const int SECTOR_BYTES = 4096;
    private const int SECTOR_INTS = SECTOR_BYTES / 4;
    private const int CHUNK_HEADER_SIZE = 8;

    private readonly string _filename;
    private readonly MemoryStream _data;
    private readonly int[] _offsets = new int[SECTOR_INTS];
    private readonly int[] _timestamps = new int[SECTOR_INTS];
    private int _sectorCount;

    public LceRegionFile(string filename)
    {
        _filename = filename;
        _data = new MemoryStream();

        // Write two empty sectors (offset table + timestamp table)
        _data.Write(new byte[SECTOR_BYTES * 2]);
        _sectorCount = 2;
    }

    /// <summary>
    /// Write a chunk's uncompressed NBT data at local coords (x, z).
    /// x and z should be 0-31.
    /// </summary>
    public void WriteChunk(int x, int z, byte[] uncompressedData)
    {
        if (x < 0 || x >= 32 || z < 0 || z >= 32) return;
        if (uncompressedData.Length == 0) return;

        // Use plain zlib payloads (RLE flag clear) for maximum compatibility.
        byte[] compressed = LceCompression.CompressZlibOnly(uncompressedData);

        // Calculate sectors needed
        int totalSize = CHUNK_HEADER_SIZE + compressed.Length;
        int sectorsNeeded = (totalSize + SECTOR_BYTES - 1) / SECTOR_BYTES;
        if (sectorsNeeded >= 256) return;

        // Allocate at end
        int sectorNumber = _sectorCount;
        _sectorCount += sectorsNeeded;

        // Seek to sector position and write chunk data
        _data.Seek(sectorNumber * SECTOR_BYTES, SeekOrigin.Begin);

        // Compressed length with RLE flag clear.
        uint compLengthWithFlag = (uint)compressed.Length;
        _data.Write(BitConverter.GetBytes(compLengthWithFlag));
        _data.Write(BitConverter.GetBytes((uint)uncompressedData.Length));
        _data.Write(compressed);

        // Pad to sector boundary
        int written = CHUNK_HEADER_SIZE + compressed.Length;
        int padding = (sectorsNeeded * SECTOR_BYTES) - written;
        if (padding > 0)
            _data.Write(new byte[padding]);

        // Record offset and timestamp
        _offsets[x + z * 32] = (sectorNumber << 8) | sectorsNeeded;
        _timestamps[x + z * 32] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Finalises the region file and writes it into the SaveDataContainer.
    /// Call this after all chunks have been written.
    /// </summary>
    public void WriteTo(SaveDataContainer container)
    {
        // Write offset table at sector 0
        _data.Seek(0, SeekOrigin.Begin);
        for (int i = 0; i < SECTOR_INTS; i++)
            _data.Write(BitConverter.GetBytes(_offsets[i]));

        // Write timestamp table at sector 1
        _data.Seek(SECTOR_BYTES, SeekOrigin.Begin);
        for (int i = 0; i < SECTOR_INTS; i++)
            _data.Write(BitConverter.GetBytes(_timestamps[i]));

        // Write the entire region file as one blob to the container
        byte[] regionBytes = _data.ToArray();
        var entry = container.CreateFile(_filename);
        container.WriteToFile(entry, regionBytes);
    }
}
