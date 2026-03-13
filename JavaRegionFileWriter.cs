using System.IO.Compression;

namespace LceWorldConverter;

public sealed class JavaRegionFileWriter
{
    private const int SectorBytes = 4096;
    private const int HeaderSectors = 2;

    private readonly string _path;
    private readonly MemoryStream _buffer;
    private readonly int[] _offsets = new int[1024];
    private readonly int[] _timestamps = new int[1024];
    private int _nextSector;

    public JavaRegionFileWriter(string path)
    {
        _path = path;
        _buffer = new MemoryStream();
        _buffer.Write(new byte[SectorBytes * HeaderSectors]);
        _nextSector = HeaderSectors;
    }

    public void WriteChunk(int localX, int localZ, byte[] uncompressedChunkNbt)
    {
        if (localX is < 0 or > 31 || localZ is < 0 or > 31)
            return;

        byte[] compressed = CompressZlib(uncompressedChunkNbt);

        int payloadLength = 1 + compressed.Length;
        int totalLength = 4 + payloadLength;
        int sectorsNeeded = (totalLength + SectorBytes - 1) / SectorBytes;
        if (sectorsNeeded >= 256)
            return;

        int sectorStart = _nextSector;
        _nextSector += sectorsNeeded;

        _buffer.Seek(sectorStart * SectorBytes, SeekOrigin.Begin);
        WriteInt32BigEndian(_buffer, payloadLength);
        _buffer.WriteByte(2);
        _buffer.Write(compressed, 0, compressed.Length);

        int written = totalLength;
        int padding = sectorsNeeded * SectorBytes - written;
        if (padding > 0)
            _buffer.Write(new byte[padding], 0, padding);

        int offsetIndex = (localX & 31) + ((localZ & 31) * 32);
        _offsets[offsetIndex] = (sectorStart << 8) | sectorsNeeded;
        _timestamps[offsetIndex] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void Save()
    {
        _buffer.Seek(0, SeekOrigin.Begin);
        for (int i = 0; i < _offsets.Length; i++)
            WriteInt32BigEndian(_buffer, _offsets[i]);

        _buffer.Seek(SectorBytes, SeekOrigin.Begin);
        for (int i = 0; i < _timestamps.Length; i++)
            WriteInt32BigEndian(_buffer, _timestamps[i]);

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(_path, _buffer.ToArray());
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)((value >> 24) & 0xFF);
        bytes[1] = (byte)((value >> 16) & 0xFF);
        bytes[2] = (byte)((value >> 8) & 0xFF);
        bytes[3] = (byte)(value & 0xFF);
        stream.Write(bytes);
    }
}
