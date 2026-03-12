using System.Buffers.Binary;

namespace LceWorldConverter;

/// <summary>
/// Writes the LCE CompressedTileStorage format for chunk block/data/light arrays.
/// Based on CompressedTileStorage.cpp from the LCE TU19 source.
///
/// Each CompressedTileStorage covers a 16x64x16 half-chunk (lower or upper).
/// The format written to the DataOutputStream is:
///   [4 bytes int] allocatedSize
///   [allocatedSize bytes] indices (1024 bytes) + data
///
/// We use the simplest encoding: 8 bits per tile for all 512 blocks.
/// The game will re-compress via compress() on load.
///
/// Block/tile mapping (from source):
///   block = ((x & 0x0c) << 5) | ((z & 0x0c) << 3) | (y >> 2)
///   tile  = ((x & 0x03) << 4) | ((z & 0x03) << 2) | (y & 0x03)
///   getIndex(block, tile) = ((tile & 0x30) << 7) | ((tile & 0x0c) << 5) | (tile & 0x03)
/// </summary>
public static class CompressedTileStorageWriter
{
    private const int INDEX_TYPE_0_OR_8_BIT = 0x0003;
    private const int NUM_BLOCKS = 512;
    private const int TILES_PER_BLOCK = 64;
    private const int INDEX_SIZE = 1024; // 512 x 2-byte shorts

    /// <summary>
    /// Writes a CompressedTileStorage for a 16x64x16 region of full-byte block data.
    /// flatData is the full 32768-byte (16x128x16) array in YZX order.
    /// yOffset is 0 for lower half, 64 for upper half.
    /// Output is written big-endian (DataOutputStream format).
    /// </summary>
    public static byte[] WriteBlockStorage(byte[] flatData, int yOffset)
    {
        // Build the indices+data blob
        // All blocks use 8-bit-per-tile (uncompressed): index type = 0x0003, bit2=0
        int dataSize = NUM_BLOCKS * TILES_PER_BLOCK; // 32768
        int allocatedSize = INDEX_SIZE + dataSize;    // 33792
        byte[] blob = new byte[allocatedSize];

        ushort[] indices = new ushort[NUM_BLOCKS];
        byte[] data = new byte[dataSize];

        int dataOffset = 0;
        for (int b = 0; b < NUM_BLOCKS; b++)
        {
            // 8-bit per tile: type bits = 011, offset stored shifted
            indices[b] = (ushort)(INDEX_TYPE_0_OR_8_BIT | (dataOffset << 1));
            dataOffset += TILES_PER_BLOCK;
        }

        // Fill data: for each position in the 16x16x64 half-chunk,
        // compute block+tile, then place the value at data[block*64 + tile]
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int ly = 0; ly < 64; ly++)
                {
                    int globalY = ly + yOffset;
                    int flatIdx = globalY * 256 + z * 16 + x;
                    byte value = flatData[flatIdx];

                    int block = ((x & 0x0c) << 5) | ((z & 0x0c) << 3) | (ly >> 2);
                    int tile = ((x & 0x03) << 4) | ((z & 0x03) << 2) | (ly & 0x03);

                    data[block * TILES_PER_BLOCK + tile] = value;
                }
            }
        }

        // Write indices as little-endian shorts (WIN64 = little-endian)
        for (int i = 0; i < NUM_BLOCKS; i++)
        {
            blob[i * 2] = (byte)(indices[i] & 0xFF);
            blob[i * 2 + 1] = (byte)(indices[i] >> 8);
        }

        // Copy data after indices
        Buffer.BlockCopy(data, 0, blob, INDEX_SIZE, dataSize);

        // Build output: [4-byte allocatedSize big-endian] + [blob]
        byte[] result = new byte[4 + allocatedSize];
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(0), allocatedSize);
        Buffer.BlockCopy(blob, 0, result, 4, allocatedSize);
        return result;
    }

    /// <summary>
    /// Writes a CompressedTileStorage for nibble data (Data, SkyLight, BlockLight).
    /// nibbleData is the full 16384-byte nibble array (16x128x16, 2 values per byte).
    /// yOffset is 0 for lower half, 64 for upper half.
    /// </summary>
    public static byte[] WriteNibbleStorage(byte[] nibbleData, int yOffset)
    {
        // Expand nibbles to full bytes for the half-chunk, then write as block storage
        byte[] expanded = new byte[32768]; // will only use 16x64x16 = 16384 entries but need full array shape

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int ly = 0; ly < 64; ly++)
                {
                    int globalY = ly + yOffset;
                    int flatIdx = globalY * 256 + z * 16 + x;
                    int nibbleIdx = flatIdx / 2;
                    int nibbleVal;
                    if ((flatIdx & 1) == 0)
                        nibbleVal = nibbleData[nibbleIdx] & 0x0F;
                    else
                        nibbleVal = (nibbleData[nibbleIdx] >> 4) & 0x0F;

                    // Store in the expanded array at the same flat position
                    expanded[globalY * 256 + z * 16 + x] = (byte)nibbleVal;
                }
            }
        }

        return WriteBlockStorage(expanded, yOffset);
    }
}
