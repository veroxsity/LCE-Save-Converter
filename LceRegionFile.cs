namespace LceWorldConverter;

/// <summary>
/// Writes LCE-format region files (.mcr) inside a SaveDataContainer.
/// Based on RegionFile.cpp from the LCE TU19 source.
///
/// LCE region file layout (same sector structure as Java McRegion):
///   [4096 bytes] Offset table (1024 x int32)
///   [4096 bytes] Timestamp table (1024 x int32) 
///   [variable]   Chunk data in 4096-byte sectors
///
/// But chunk data format differs from Java:
///   [4 bytes] Compressed length (high bit = RLE flag)
///   [4 bytes] Decompressed length
///   [variable] RLE+zlib compressed data (WIN64)
/// </summary>
public class LceRegionFile
{
    private const int SECTOR_BYTES = 4096;
    private const int SECTOR_INTS = SECTOR_BYTES / 4;
    private const int CHUNK_HEADER_SIZE = 8;

    private readonly SaveDataContainer _container;
    private readonly SaveFileEntry _fileEntry;
    private readonly int[] _offsets = new int[SECTOR_INTS];
    private readonly int[] _timestamps = new int[SECTOR_INTS];
    private int _sectorCount;

    public LceRegionFile(SaveDataContainer container, string filename)
    {
        _container = container;
        _fileEntry = container.CreateFile(filename);

        // Initialize with two empty sectors (offset table + timestamp table)
        _sectorCount = 2;
        byte[] emptyData = new byte[SECTOR_BYTES * 2];
        _container.WriteToFile(_fileEntry, emptyData);
        _container.SeekFile(_fileEntry, 0); // Reset pointer
    }

    /// <summary>
    /// Write a chunk's uncompressed NBT data at local coords (x, z) within this region.
    /// x and z should be 0-31.
    /// </summary>
    public void WriteChunk(int x, int z, byte[] uncompressedData)
    {
        if (x < 0 || x >= 32 || z < 0 || z >= 32) return;
        if (uncompressedData.Length == 0) return;

        // Compress using LCE RLE+zlib
        byte[] compressed = LceCompression.Compress(uncompressedData);
        int decompLength = uncompressedData.Length;

        // Calculate sectors needed
        int totalSize = CHUNK_HEADER_SIZE + compressed.Length;
        int sectorsNeeded = (totalSize + SECTOR_BYTES - 1) / SECTOR_BYTES;
        if (sectorsNeeded >= 256) return; // Max chunk size check

        // Allocate at end of file
        int sectorNumber = _sectorCount;
        _sectorCount += sectorsNeeded;

        // Write empty sectors to extend the file
        _container.SeekFileEnd(_fileEntry);
        byte[] emptySectors = new byte[sectorsNeeded * SECTOR_BYTES];
        _container.WriteToFile(_fileEntry, emptySectors);

        // Write chunk data at the sector position
        _container.SeekFile(_fileEntry, (uint)(sectorNumber * SECTOR_BYTES));

        // Compressed length with RLE flag (high bit set)
        uint compLengthWithFlag = (uint)compressed.Length | 0x80000000;
        byte[] header = new byte[CHUNK_HEADER_SIZE];
        BitConverter.TryWriteBytes(header.AsSpan(0), compLengthWithFlag);
        BitConverter.TryWriteBytes(header.AsSpan(4), (uint)decompLength);
        _container.WriteToFile(_fileEntry, header);
        _container.WriteToFile(_fileEntry, compressed);

        // Update offset and timestamp tables
        int offset = (sectorNumber << 8) | sectorsNeeded;
        _offsets[x + z * 32] = offset;
        _timestamps[x + z * 32] = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Write offset entry back to sector 0
        _container.SeekFile(_fileEntry, (uint)((x + z * 32) * 4));
        _container.WriteToFile(_fileEntry, BitConverter.GetBytes(offset));

        // Write timestamp entry to sector 1
        _container.SeekFile(_fileEntry, (uint)(SECTOR_BYTES + (x + z * 32) * 4));
        _container.WriteToFile(_fileEntry, BitConverter.GetBytes(_timestamps[x + z * 32]));
    }
}
