namespace LceWorldConverter;

/// <summary>
/// Writes LCE saveData.ms container files.
/// Based on ConsoleSaveFileOriginal from the LCE TU19 source.
/// 
/// File layout:
///   [4 bytes] Offset to header/footer (end of file data)
///   [4 bytes] Number of file entries  
///   [2 bytes] Original save version
///   [2 bytes] Current save version
///   ... file data blocks (sequential, each at its startOffset) ...
///   [footer]  Array of FileEntrySaveDataV2 structs
///
/// Each FileEntrySaveDataV2 is 144 bytes:
///   [128 bytes] wchar_t filename[64] (UTF-16LE, null-padded)
///   [4 bytes]   length (file size in bytes)
///   [4 bytes]   startOffset (byte offset from start of saveData.ms)
///   [8 bytes]   lastModifiedTime (ms since epoch)
/// </summary>
public class SaveDataContainer
{
    public const int SAVE_FILE_HEADER_SIZE = 12;
    public const int FILE_ENTRY_SIZE = 144;

    private readonly short _originalSaveVersion;
    private readonly short _currentSaveVersion;

    private readonly List<SaveFileEntry> _entries = new();
    private byte[] _blob;
    private int _dataEnd; // points to the end of file data (start of footer)

    public SaveDataContainer(short originalSaveVersion, short currentSaveVersion)
    {
        _originalSaveVersion = originalSaveVersion;
        _currentSaveVersion = currentSaveVersion;

        // Start with a reasonably sized buffer
        _blob = new byte[2 * 1024 * 1024]; // 2MB initial
        _dataEnd = SAVE_FILE_HEADER_SIZE;   // first file starts after the 12-byte header
    }

    /// <summary>
    /// Creates or retrieves a file entry in the container.
    /// If it already exists, returns the existing entry.
    /// </summary>
    public SaveFileEntry CreateFile(string name)
    {
        // Check if already exists
        var existing = _entries.FirstOrDefault(e => e.Name == name);
        if (existing != null) return existing;

        var entry = new SaveFileEntry(name, _dataEnd);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Writes data to a file entry at its current position.
    /// If the entry was empty (pre-created placeholder), relocates it to the current data end.
    /// </summary>
    public void WriteToFile(SaveFileEntry entry, byte[] data)
    {
        // If this is a previously-empty entry, relocate it to the current data end
        if (entry.Length == 0 && data.Length > 0)
        {
            entry.StartOffset = (uint)_dataEnd;
            entry.CurrentPointer = (uint)_dataEnd;
        }

        int writePos = (int)entry.CurrentPointer;
        int endPos = writePos + data.Length;

        // Ensure capacity
        EnsureCapacity(endPos);

        Buffer.BlockCopy(data, 0, _blob, writePos, data.Length);
        entry.CurrentPointer += (uint)data.Length;

        // Update entry length if we wrote past the end
        uint written = entry.CurrentPointer - entry.StartOffset;
        if (written > entry.Length)
            entry.Length = written;

        // Update data end marker
        int entryEnd = (int)(entry.StartOffset + entry.Length);
        if (entryEnd > _dataEnd)
            _dataEnd = entryEnd;

        entry.LastModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Writes zeroed data to a file entry (for region file sector allocation).
    /// </summary>
    public void ZeroFile(SaveFileEntry entry, int count)
    {
        int writePos = (int)entry.CurrentPointer;
        int endPos = writePos + count;

        EnsureCapacity(endPos);
        Array.Clear(_blob, writePos, count);
        entry.CurrentPointer += (uint)count;

        uint written = entry.CurrentPointer - entry.StartOffset;
        if (written > entry.Length)
            entry.Length = written;

        int entryEnd = (int)(entry.StartOffset + entry.Length);
        if (entryEnd > _dataEnd)
            _dataEnd = entryEnd;

        entry.LastModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Reads data from a file entry at its current position.
    /// </summary>
    public int ReadFromFile(SaveFileEntry entry, byte[] buffer, int count)
    {
        int readPos = (int)entry.CurrentPointer;
        int available = (int)(entry.StartOffset + entry.Length) - readPos;
        int toRead = Math.Min(count, Math.Max(0, available));

        if (toRead > 0)
            Buffer.BlockCopy(_blob, readPos, buffer, 0, toRead);

        entry.CurrentPointer += (uint)toRead;
        return toRead;
    }

    /// <summary>
    /// Sets the file pointer for an entry (FILE_BEGIN mode).
    /// </summary>
    public void SeekFile(SaveFileEntry entry, uint offset)
    {
        entry.CurrentPointer = entry.StartOffset + offset;
    }

    /// <summary>
    /// Sets file pointer to end (FILE_END mode).
    /// </summary>
    public void SeekFileEnd(SaveFileEntry entry)
    {
        entry.CurrentPointer = entry.StartOffset + entry.Length;
    }

    /// <summary>
    /// Writes the complete saveData.ms file to disk.
    /// The on-disk format wraps the in-memory blob in a compression envelope:
    ///   [4 bytes] int = 0 (signals compressed data)
    ///   [4 bytes] decompressed size (little-endian)
    ///   [variable] zlib-compressed blob
    /// Based on ConsoleSaveFileOriginal::Flush() from the LCE source.
    /// </summary>
    public void Save(string outputPath)
    {
        // Write header at the start of the in-memory blob
        WriteHeader();

        // Calculate total in-memory size = data + footer
        int totalSize = _dataEnd + (_entries.Count * FILE_ENTRY_SIZE);
        EnsureCapacity(totalSize);

        // Write footer (file table) at _dataEnd
        WriteFooter();

        // Compress the entire blob with zlib (WIN64 format)
        byte[] rawBlob = new byte[totalSize];
        Buffer.BlockCopy(_blob, 0, rawBlob, 0, totalSize);

        using var compressedStream = new MemoryStream();
        using (var zlibStream = new System.IO.Compression.ZLibStream(
            compressedStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlibStream.Write(rawBlob, 0, totalSize);
        }
        byte[] compressedData = compressedStream.ToArray();

        // Write the on-disk format: [0 int][decompSize int][compressed data]
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        using var fs = new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write);
        fs.Write(BitConverter.GetBytes((int)0));            // compressed flag
        fs.Write(BitConverter.GetBytes((int)totalSize));    // decompressed size
        fs.Write(compressedData);                           // zlib compressed blob
    }

    private void WriteHeader()
    {
        // Offset to footer
        BitConverter.TryWriteBytes(_blob.AsSpan(0), (uint)_dataEnd);
        // Number of file entries
        BitConverter.TryWriteBytes(_blob.AsSpan(4), (uint)_entries.Count);
        // Original save version
        BitConverter.TryWriteBytes(_blob.AsSpan(8), _originalSaveVersion);
        // Current save version
        BitConverter.TryWriteBytes(_blob.AsSpan(10), _currentSaveVersion);
    }

    private void WriteFooter()
    {
        int pos = _dataEnd;
        // Runtime save growth logic assumes the file table order matches physical file layout.
        // Sort by start offset so the game can shift subsequent files correctly when a region grows.
        foreach (var entry in _entries.OrderBy(e => e.StartOffset))
        {
            // Filename: wchar_t[64] = 128 bytes, UTF-16LE, null-padded
            byte[] nameBytes = new byte[128];
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(entry.Name);
            int copyLen = Math.Min(encoded.Length, 126); // leave room for null terminator
            Buffer.BlockCopy(encoded, 0, nameBytes, 0, copyLen);
            Buffer.BlockCopy(nameBytes, 0, _blob, pos, 128);
            pos += 128;

            // Length
            BitConverter.TryWriteBytes(_blob.AsSpan(pos), entry.Length);
            pos += 4;

            // Start offset
            BitConverter.TryWriteBytes(_blob.AsSpan(pos), entry.StartOffset);
            pos += 4;

            // Last modified time
            BitConverter.TryWriteBytes(_blob.AsSpan(pos), entry.LastModifiedTime);
            pos += 8;
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _blob.Length) return;

        int newSize = _blob.Length;
        while (newSize < required)
            newSize *= 2;

        byte[] newBlob = new byte[newSize];
        Buffer.BlockCopy(_blob, 0, newBlob, 0, _dataEnd);
        _blob = newBlob;
    }

    /// <summary>
    /// Gets a writable span at the file entry's current pointer position.
    /// Used by RegionFile for direct writes.
    /// </summary>
    public Span<byte> GetWriteSpan(SaveFileEntry entry, int length)
    {
        int writePos = (int)entry.CurrentPointer;
        EnsureCapacity(writePos + length);
        return _blob.AsSpan(writePos, length);
    }
}

/// <summary>
/// Represents a single file inside the saveData.ms container.
/// </summary>
public class SaveFileEntry
{
    public string Name { get; }
    public uint Length { get; set; }
    public uint StartOffset { get; set; }
    public long LastModifiedTime { get; set; }
    public uint CurrentPointer { get; set; }

    public SaveFileEntry(string name, int startOffset)
    {
        Name = name;
        Length = 0;
        StartOffset = (uint)startOffset;
        CurrentPointer = (uint)startOffset;
        LastModifiedTime = 0;
    }
}
