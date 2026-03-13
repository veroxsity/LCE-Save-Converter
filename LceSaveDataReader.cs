using System.IO.Compression;
using System.Text;
using fNbt;

namespace LceWorldConverter;

public sealed class LceSaveDataReader
{
    private const int FileEntrySize = 144;

    private readonly byte[] _rawBlob;
    private readonly Dictionary<string, SaveEntry> _entries;

    public LceSaveDataReader(string saveDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveDataPath);
        byte[] containerBytes = File.ReadAllBytes(saveDataPath);
        _rawBlob = ReadRawBlob(containerBytes);

        _entries = ParseEntries(_rawBlob)
            .ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetFileBytes(string entryName, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (!_entries.TryGetValue(entryName, out SaveEntry? entry))
            return false;

        data = new byte[entry.Length];
        Buffer.BlockCopy(_rawBlob, entry.StartOffset, data, 0, entry.Length);
        return true;
    }

    public IEnumerable<SaveEntry> EnumerateEntries()
    {
        return _entries.Values;
    }

    public bool TryReadLevelDat(out NbtCompound root)
    {
        root = new NbtCompound(string.Empty);
        if (!TryGetFileBytes("level.dat", out byte[] levelBytes))
            return false;

        var nbtFile = new NbtFile();
        nbtFile.LoadFromBuffer(levelBytes, 0, levelBytes.Length, NbtCompression.None);
        root = nbtFile.RootTag;
        return true;
    }

    public sealed class SaveEntry
    {
        public required string Name { get; init; }
        public required int Length { get; init; }
        public required int StartOffset { get; init; }
        public required long LastModifiedTime { get; init; }
    }

    private static byte[] ReadRawBlob(byte[] containerBytes)
    {
        if (containerBytes.Length < 8)
            throw new InvalidDataException("Invalid saveData.ms container: too small.");

        int compressedFlag = BitConverter.ToInt32(containerBytes, 0);
        if (compressedFlag != 0)
            return containerBytes;

        using var compressed = new MemoryStream(containerBytes, 8, containerBytes.Length - 8);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static IEnumerable<SaveEntry> ParseEntries(byte[] rawBlob)
    {
        if (rawBlob.Length < 12)
            yield break;

        int tableOffset = (int)BitConverter.ToUInt32(rawBlob, 0);
        int fileCount = (int)BitConverter.ToUInt32(rawBlob, 4);

        if (tableOffset < 0 || tableOffset >= rawBlob.Length || fileCount < 0)
            yield break;

        int pos = tableOffset;
        for (int i = 0; i < fileCount; i++)
        {
            if (pos + FileEntrySize > rawBlob.Length)
                yield break;

            string name = Encoding.Unicode.GetString(rawBlob, pos, 128).TrimEnd('\0');
            pos += 128;

            int length = (int)BitConverter.ToUInt32(rawBlob, pos);
            pos += 4;

            int startOffset = (int)BitConverter.ToUInt32(rawBlob, pos);
            pos += 4;

            long modified = BitConverter.ToInt64(rawBlob, pos);
            pos += 8;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (length <= 0)
                continue;

            if (startOffset < 0 || startOffset + length > rawBlob.Length)
                continue;

            yield return new SaveEntry
            {
                Name = name,
                Length = length,
                StartOffset = startOffset,
                LastModifiedTime = modified,
            };
        }
    }
}
