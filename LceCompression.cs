using System.IO.Compression;

namespace LceWorldConverter;

/// <summary>
/// Implements LCE WIN64 compression: RLE pass then zlib compress.
/// Based on Compression::CompressLZXRLE and Compression::Compress from the LCE source.
/// 
/// RLE encoding scheme:
///   Byte 0-254: literal byte
///   Byte 255 followed by 0,1,2: encodes 1, 2, or 3 literal 255s
///   Byte 255 followed by 3-255, followed by data byte: run of (count+1) copies of data byte
/// </summary>
public static class LceCompression
{
    /// <summary>
    /// Compress using zlib only (no RLE pre-pass). Safer interoperability path.
    /// </summary>
    public static byte[] CompressZlibOnly(byte[] data)
    {
        return ZlibCompress(data);
    }

    /// <summary>
    /// Compress data using RLE then zlib (WIN64 format).
    /// </summary>
    public static byte[] Compress(byte[] data)
    {
        // Step 1: RLE encode
        byte[] rleData = RleEncode(data);

        // Step 2: zlib compress
        return ZlibCompress(rleData);
    }

    /// <summary>
    /// Decompress data using zlib then RLE decode (WIN64 format).
    /// </summary>
    public static byte[] Decompress(byte[] compressed, int decompressedSize)
    {
        // Step 1: zlib decompress
        byte[] rleData = ZlibDecompress(compressed);

        // Step 2: RLE decode
        return RleDecode(rleData, decompressedSize);
    }

    /// <summary>
    /// RLE encode matching the LCE source CompressLZXRLE algorithm exactly.
    /// </summary>
    public static byte[] RleEncode(byte[] data)
    {
        if (data.Length == 0) return Array.Empty<byte>();

        using var output = new MemoryStream(data.Length);
        int i = 0;

        while (i < data.Length)
        {
            byte current = data[i++];
            int count = 1;

            // Count consecutive identical bytes (max 256)
            while (i < data.Length && data[i] == current && count < 256)
            {
                i++;
                count++;
            }

            if (count <= 3)
            {
                if (current == 255)
                {
                    // Special encoding for literal 255s
                    output.WriteByte(255);
                    output.WriteByte((byte)(count - 1));
                }
                else
                {
                    // Write literal bytes
                    for (int j = 0; j < count; j++)
                        output.WriteByte(current);
                }
            }
            else
            {
                // Run-length encode: 255, (count-1), data
                output.WriteByte(255);
                output.WriteByte((byte)(count - 1));
                output.WriteByte(current);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// RLE decode matching the LCE source DecompressLZXRLE algorithm.
    /// </summary>
    public static byte[] RleDecode(byte[] data, int expectedSize)
    {
        byte[] output = new byte[expectedSize];
        int inPos = 0;
        int outPos = 0;

        while (inPos < data.Length && outPos < expectedSize)
        {
            byte current = data[inPos++];
            if (current == 255)
            {
                if (inPos >= data.Length) break;
                int count = data[inPos++];
                if (count < 3)
                {
                    // 1, 2, or 3 literal 255s
                    count++;
                    for (int j = 0; j < count && outPos < expectedSize; j++)
                        output[outPos++] = 255;
                }
                else
                {
                    // Run of (count+1) copies of next byte
                    count++;
                    if (inPos >= data.Length) break;
                    byte value = data[inPos++];
                    for (int j = 0; j < count && outPos < expectedSize; j++)
                        output[outPos++] = value;
                }
            }
            else
            {
                output[outPos++] = current;
            }
        }

        return output;
    }

    /// <summary>
    /// zlib compress using .NET's built-in DeflateStream wrapped in zlib format.
    /// Matches zlib::compress() used by the LCE WIN64 build.
    /// </summary>
    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using var compressor = new ZLibStream(output, CompressionLevel.Optimal);
        compressor.Write(data, 0, data.Length);
        compressor.Flush();
        compressor.Close();
        return output.ToArray();
    }

    /// <summary>
    /// zlib decompress using .NET's built-in ZLibStream.
    /// Matches zlib::uncompress() used by the LCE WIN64 build.
    /// </summary>
    private static byte[] ZlibDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }
}
