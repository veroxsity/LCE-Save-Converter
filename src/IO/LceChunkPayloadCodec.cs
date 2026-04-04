using System.Buffers.Binary;
using fNbt;

namespace LceWorldConverter;

public static class LceChunkPayloadCodec
{
    private const short SaveFileVersionCompressedChunkStorage = 8;
    private const short SaveFileVersionChunkInhabitedTime = 9;

    private const int CompressedChunkSectionHeight = 128;
    private const int BlocksPerSection = CompressedChunkSectionHeight * 16 * 16;
    private const int NibblesPerSection = BlocksPerSection / 2;

    private const ushort IndexTypeMask = 0x0003;
    private const ushort IndexType1Bit = 0x0000;
    private const ushort IndexType2Bit = 0x0001;
    private const ushort IndexType4Bit = 0x0002;
    private const ushort IndexType0Or8Bit = 0x0003;
    private const ushort IndexType0BitFlag = 0x0004;

    private const byte SparseAllZeroIndex = 128;
    private const byte SparseAllFifteenIndex = 129;

    public static byte[] EncodeLegacyNbt(NbtCompound level)
    {
        var root = new NbtCompound(string.Empty)
        {
            (NbtCompound)level.Clone(),
        };

        using var ms = new MemoryStream();
        new NbtFile(root).SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }

    public static byte[] EncodeCompressedStorage(NbtCompound level)
    {
        byte[] blocks = level.Get<NbtByteArray>("Blocks")?.Value ?? new byte[BlocksPerSection];
        byte[] data = level.Get<NbtByteArray>("Data")?.Value ?? new byte[NibblesPerSection];
        byte[] skyLight = level.Get<NbtByteArray>("SkyLight")?.Value ?? CreateFilledArray(NibblesPerSection, 0xFF);
        byte[] blockLight = level.Get<NbtByteArray>("BlockLight")?.Value ?? new byte[NibblesPerSection];
        byte[] heightMap = level.Get<NbtByteArray>("HeightMap")?.Value ?? new byte[256];
        byte[] biomes = level.Get<NbtByteArray>("Biomes")?.Value ?? new byte[256];

        using var ms = new MemoryStream();

        WriteInt16BigEndian(ms, SaveFileVersionChunkInhabitedTime);
        WriteInt32BigEndian(ms, level.Get<NbtInt>("xPos")?.Value ?? 0);
        WriteInt32BigEndian(ms, level.Get<NbtInt>("zPos")?.Value ?? 0);
        WriteInt64BigEndian(ms, level.Get<NbtLong>("LastUpdate")?.Value ?? 0);
        WriteInt64BigEndian(ms, level.Get<NbtLong>("InhabitedTime")?.Value ?? 0);

        WriteCompressedTileStorage(ms, blocks);
        WriteEmptyCompressedTileStorage(ms);

        WriteSparseNibbleStorage(ms, data, supportsAllFifteenPlane: false);
        WriteEmptySparseNibbleStorage(ms, supportsAllFifteenPlane: false, fillWithFifteen: false);

        WriteSparseNibbleStorage(ms, skyLight, supportsAllFifteenPlane: true);
        WriteEmptySparseNibbleStorage(ms, supportsAllFifteenPlane: true, fillWithFifteen: true);

        WriteSparseNibbleStorage(ms, blockLight, supportsAllFifteenPlane: true);
        WriteEmptySparseNibbleStorage(ms, supportsAllFifteenPlane: true, fillWithFifteen: false);

        ms.Write(heightMap, 0, Math.Min(heightMap.Length, 256));
        if (heightMap.Length < 256)
            ms.Write(new byte[256 - heightMap.Length]);

        WriteInt16BigEndian(ms, level.Get<NbtShort>("TerrainPopulatedFlags")?.Value ?? (short)0);

        ms.Write(biomes, 0, Math.Min(biomes.Length, 256));
        if (biomes.Length < 256)
            ms.Write(new byte[256 - biomes.Length]);

        // Stability first: omit dynamic chunk NBT for compressed-storage output.
        // This isolates crashes caused by the trailing NBT section and lets the
        // runtime regenerate transient state safely.
        NbtCompound dynamicRoot = BuildEmptyDynamicRoot();
        using var nbtStream = new MemoryStream();
        new NbtFile(dynamicRoot).SaveToStream(nbtStream, NbtCompression.None);
        byte[] dynamicBytes = nbtStream.ToArray();
        ms.Write(dynamicBytes, 0, dynamicBytes.Length);

        return ms.ToArray();
    }

    public static byte[] ForceChunkCoordinates(byte[] payload, int expectedChunkX, int expectedChunkZ)
    {
        if (TryReadLegacyLevel(payload, out NbtCompound? legacyLevel, out _))
        {
            UpsertTag(legacyLevel, new NbtInt("xPos", expectedChunkX));
            UpsertTag(legacyLevel, new NbtInt("zPos", expectedChunkZ));
            return EncodeLegacyNbt(legacyLevel);
        }

        if (IsCompressedChunkStorage(payload))
        {
            byte[] patched = (byte[])payload.Clone();
            BinaryPrimitives.WriteInt32BigEndian(patched.AsSpan(2, 4), expectedChunkX);
            BinaryPrimitives.WriteInt32BigEndian(patched.AsSpan(6, 4), expectedChunkZ);
            return patched;
        }

        return Array.Empty<byte>();
    }

    public static bool TryReadChunkCoordinates(byte[] payload, out int chunkX, out int chunkZ, out bool hasLevelWrapper)
    {
        chunkX = int.MinValue;
        chunkZ = int.MinValue;
        hasLevelWrapper = false;

        if (TryReadLegacyLevel(payload, out NbtCompound? level, out bool wrapped))
        {
            chunkX = level.Get<NbtInt>("xPos")?.Value ?? int.MinValue;
            chunkZ = level.Get<NbtInt>("zPos")?.Value ?? int.MinValue;
            hasLevelWrapper = wrapped;
            return true;
        }

        if (IsCompressedChunkStorage(payload))
        {
            chunkX = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(2, 4));
            chunkZ = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(6, 4));
            hasLevelWrapper = false;
            return true;
        }

        return false;
    }

    public static bool TryDecodeToLegacyNbt(byte[] payload, out byte[] legacyNbt)
    {
        legacyNbt = Array.Empty<byte>();

        if (TryReadLegacyLevel(payload, out NbtCompound? legacyLevel, out _))
        {
            legacyNbt = EncodeLegacyNbt(legacyLevel);
            return true;
        }

        if (!IsCompressedChunkStorage(payload))
            return false;

        try
        {
            NbtCompound root = DecodeCompressedChunkToLegacyRoot(payload);
            using var ms = new MemoryStream();
            new NbtFile(root).SaveToStream(ms, NbtCompression.None);
            legacyNbt = ms.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetCompressedChunkNbtOffset(byte[] payload, out int nbtOffset)
    {
        nbtOffset = -1;

        if (!IsCompressedChunkStorage(payload))
            return false;

        try
        {
            int offset = 0;
            short version = ReadInt16BigEndian(payload, ref offset);
            if (version != SaveFileVersionCompressedChunkStorage && version != SaveFileVersionChunkInhabitedTime)
                return false;

            offset += 4; // x
            offset += 4; // z
            offset += 8; // last update
            if (version >= SaveFileVersionChunkInhabitedTime)
                offset += 8; // inhabited time

            SkipCompressedTileStorage(payload, ref offset);
            SkipCompressedTileStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);
            SkipSparseNibbleStorage(payload, ref offset);

            offset += 256; // heightmap
            offset += 2;   // terrain populated flags
            offset += 256; // biomes

            if (offset > payload.Length)
                return false;

            nbtOffset = offset;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadLegacyLevel(byte[] payload, out NbtCompound level, out bool hasLevelWrapper)
    {
        level = new NbtCompound("Level");
        hasLevelWrapper = false;

        if (payload.Length == 0 || payload[0] != (byte)NbtTagType.Compound)
            return false;

        try
        {
            var file = new NbtFile();
            file.LoadFromBuffer(payload, 0, payload.Length, NbtCompression.None);

            NbtCompound root = file.RootTag;
            NbtCompound? readLevel = root.Get<NbtCompound>("Level");
            hasLevelWrapper = readLevel != null;
            level = (NbtCompound)(readLevel ?? root).Clone();
            level.Name = "Level";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCompressedChunkStorage(byte[] payload)
    {
        if (payload.Length < 2 + 4 + 4 + 8)
            return false;

        short version = BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(0, 2));
        return version == SaveFileVersionCompressedChunkStorage || version == SaveFileVersionChunkInhabitedTime;
    }

    private static NbtCompound DecodeCompressedChunkToLegacyRoot(byte[] payload)
    {
        int offset = 0;
        short version = ReadInt16BigEndian(payload, ref offset);
        if (version != SaveFileVersionCompressedChunkStorage && version != SaveFileVersionChunkInhabitedTime)
            throw new InvalidDataException($"Unsupported compressed chunk version: {version}");

        int chunkX = ReadInt32BigEndian(payload, ref offset);
        int chunkZ = ReadInt32BigEndian(payload, ref offset);
        long lastUpdate = ReadInt64BigEndian(payload, ref offset);
        long inhabitedTime = version >= SaveFileVersionChunkInhabitedTime
            ? ReadInt64BigEndian(payload, ref offset)
            : 0L;

        byte[] lowerBlocks = ReadCompressedTileStorage(payload, ref offset);
        _ = ReadCompressedTileStorage(payload, ref offset); // upper blocks, currently ignored

        byte[] lowerData = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: false);
        _ = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: false); // upper data

        byte[] lowerSkyLight = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: true);
        _ = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: true); // upper skylight

        byte[] lowerBlockLight = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: true);
        _ = ReadSparseNibbleStorage(payload, ref offset, supportsAllFifteenPlane: true); // upper blocklight

        byte[] heightMap = ReadSizedBytes(payload, ref offset, 256);
        short terrainPopulatedFlags = ReadInt16BigEndian(payload, ref offset);
        byte[] biomes = ReadSizedBytes(payload, ref offset, 256);

        NbtCompound dynamicRoot;
        if (offset < payload.Length)
        {
            var dynamicFile = new NbtFile();
            dynamicFile.LoadFromBuffer(payload, offset, payload.Length - offset, NbtCompression.None);
            dynamicRoot = dynamicFile.RootTag;
        }
        else
        {
            dynamicRoot = new NbtCompound(string.Empty);
        }

        var level = new NbtCompound("Level")
        {
            new NbtInt("xPos", chunkX),
            new NbtInt("zPos", chunkZ),
            new NbtLong("LastUpdate", lastUpdate),
            new NbtLong("InhabitedTime", inhabitedTime),
            new NbtByteArray("Blocks", lowerBlocks),
            new NbtByteArray("Data", lowerData),
            new NbtByteArray("SkyLight", lowerSkyLight),
            new NbtByteArray("BlockLight", lowerBlockLight),
            new NbtByteArray("HeightMap", heightMap),
            new NbtShort("TerrainPopulatedFlags", terrainPopulatedFlags),
            new NbtByteArray("Biomes", biomes),
            CloneListOrEmpty(dynamicRoot, "Entities"),
            CloneListOrEmpty(dynamicRoot, "TileEntities"),
        };

        if (dynamicRoot.Contains("TileTicks") && dynamicRoot["TileTicks"] is NbtList tileTicks)
            level.Add((NbtTag)tileTicks.Clone());

        return new NbtCompound(string.Empty) { level };
    }

    private static NbtCompound BuildEmptyDynamicRoot()
    {
        return new NbtCompound(string.Empty);
    }

    private static NbtList CloneListOrEmpty(NbtCompound source, string name)
    {
        if (source.Contains(name) && source[name] is NbtList list)
            return (NbtList)list.Clone();

        return new NbtList(name, NbtTagType.Compound);
    }

    private static void UpsertTag(NbtCompound compound, NbtTag tag)
    {
        if (string.IsNullOrEmpty(tag.Name))
            return;

        if (compound.Contains(tag.Name))
            compound.Remove(tag.Name);

        compound.Add(tag);
    }

    private static void WriteCompressedTileStorage(Stream stream, byte[] blocks)
    {
        byte[] normalizedBlocks = EnsureLength(blocks, BlocksPerSection);
        byte[] blob = new byte[1024 + BlocksPerSection];
        int dataOffset = 0;

        for (int block = 0; block < 512; block++)
        {
            ushort index = (ushort)(IndexType0Or8Bit | (dataOffset << 1));
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(block * 2, 2), index);

            for (int tile = 0; tile < 64; tile++)
                blob[1024 + dataOffset + tile] = normalizedBlocks[GetCompressedTileIndex(block, tile)];

            dataOffset += 64;
        }

        WriteInt32BigEndian(stream, blob.Length);
        stream.Write(blob, 0, blob.Length);
    }

    private static void WriteEmptyCompressedTileStorage(Stream stream)
    {
        byte[] blob = new byte[1024];
        for (int block = 0; block < 512; block++)
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(block * 2, 2), (ushort)(IndexType0Or8Bit | IndexType0BitFlag));

        WriteInt32BigEndian(stream, blob.Length);
        stream.Write(blob, 0, blob.Length);
    }

    private static byte[] ReadCompressedTileStorage(byte[] payload, ref int offset)
    {
        int allocatedSize = ReadInt32BigEndian(payload, ref offset);
        if (allocatedSize < 1024 || offset + allocatedSize > payload.Length)
            throw new InvalidDataException("Invalid CompressedTileStorage payload.");

        ReadOnlySpan<byte> blob = payload.AsSpan(offset, allocatedSize);
        offset += allocatedSize;
        ReadOnlySpan<byte> dataRegion = blob.Slice(1024);

        byte[] blocks = new byte[BlocksPerSection];
        for (int block = 0; block < 512; block++)
        {
            ushort blockIndex = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(block * 2, 2));
            int indexType = blockIndex & IndexTypeMask;

            if (indexType == IndexType0Or8Bit)
            {
                if ((blockIndex & IndexType0BitFlag) != 0)
                {
                    byte value = (byte)((blockIndex >> 8) & 0xFF);
                    for (int tile = 0; tile < 64; tile++)
                        blocks[GetCompressedTileIndex(block, tile)] = value;
                }
                else
                {
                    int dataOffset = (blockIndex >> 1) & 0x7FFE;
                    if (dataOffset + 64 > dataRegion.Length)
                        throw new InvalidDataException("Invalid 8-bit CompressedTileStorage offset.");

                    for (int tile = 0; tile < 64; tile++)
                        blocks[GetCompressedTileIndex(block, tile)] = dataRegion[dataOffset + tile];
                }

                continue;
            }

            int bitsPerTile = indexType switch
            {
                IndexType1Bit => 1,
                IndexType2Bit => 2,
                IndexType4Bit => 4,
                _ => throw new InvalidDataException("Unsupported CompressedTileStorage index type."),
            };

            int tileTypeCount = 1 << bitsPerTile;
            int tileTypeMask = tileTypeCount - 1;
            int indexShift = 3 - indexType;
            int indexMaskBits = 7 >> indexType;
            int indexMaskBytes = 62 >> indexShift;
            int packedDataSize = 8 << indexType;

            int dataOffsetPacked = (blockIndex >> 1) & 0x7FFE;
            if (dataOffsetPacked + tileTypeCount + packedDataSize > dataRegion.Length)
                throw new InvalidDataException("Invalid packed CompressedTileStorage offset.");

            ReadOnlySpan<byte> tileTypes = dataRegion.Slice(dataOffsetPacked, tileTypeCount);
            ReadOnlySpan<byte> packed = dataRegion.Slice(dataOffsetPacked + tileTypeCount, packedDataSize);

            for (int tile = 0; tile < 64; tile++)
            {
                int idx = (tile >> indexShift) & indexMaskBytes;
                int bit = (tile & indexMaskBits) * bitsPerTile;
                int paletteIndex = (packed[idx] >> bit) & tileTypeMask;
                blocks[GetCompressedTileIndex(block, tile)] = tileTypes[paletteIndex];
            }
        }

        return blocks;
    }

    private static void SkipCompressedTileStorage(byte[] payload, ref int offset)
    {
        int allocatedSize = ReadInt32BigEndian(payload, ref offset);
        if (allocatedSize < 0 || offset + allocatedSize > payload.Length)
            throw new InvalidDataException("Invalid CompressedTileStorage payload.");

        offset += allocatedSize;
    }

    private static void WriteSparseNibbleStorage(Stream stream, byte[] nibbleData, bool supportsAllFifteenPlane)
    {
        byte[] normalizedNibbles = EnsureLength(nibbleData, NibblesPerSection);
        byte[] planeIndices = new byte[128];
        var planes = new List<byte[]>(128);

        for (int y = 0; y < 128; y++)
        {
            bool allZero = true;
            bool allFifteen = supportsAllFifteenPlane;
            byte[] plane = new byte[128];
            int planeCursor = 0;

            for (int xz = 0; xz < 128; xz++)
            {
                byte first = (byte)GetNibbleValue(normalizedNibbles, (xz << 1), y);
                byte second = (byte)GetNibbleValue(normalizedNibbles, (xz << 1) + 1, y);

                if (first != 0 || second != 0)
                    allZero = false;
                if (supportsAllFifteenPlane && (first != 15 || second != 15))
                    allFifteen = false;

                plane[planeCursor++] = (byte)(first | (second << 4));
            }

            if (allZero)
            {
                planeIndices[y] = SparseAllZeroIndex;
                continue;
            }

            if (supportsAllFifteenPlane && allFifteen)
            {
                planeIndices[y] = SparseAllFifteenIndex;
                continue;
            }

            planeIndices[y] = (byte)planes.Count;
            planes.Add(plane);
        }

        WriteInt32BigEndian(stream, planes.Count);
        stream.Write(planeIndices, 0, planeIndices.Length);
        foreach (byte[] plane in planes)
            stream.Write(plane, 0, plane.Length);
    }

    private static void WriteEmptySparseNibbleStorage(Stream stream, bool supportsAllFifteenPlane, bool fillWithFifteen)
    {
        WriteInt32BigEndian(stream, 0);
        byte fill = supportsAllFifteenPlane && fillWithFifteen ? SparseAllFifteenIndex : SparseAllZeroIndex;
        byte[] planeIndices = CreateFilledArray(128, fill);
        stream.Write(planeIndices, 0, planeIndices.Length);
    }

    private static byte[] ReadSparseNibbleStorage(byte[] payload, ref int offset, bool supportsAllFifteenPlane)
    {
        int count = ReadInt32BigEndian(payload, ref offset);
        int storageBytes = 128 + (count * 128);
        if (count < 0 || offset + storageBytes > payload.Length)
            throw new InvalidDataException("Invalid SparseStorage payload.");

        ReadOnlySpan<byte> blob = payload.AsSpan(offset, storageBytes);
        offset += storageBytes;

        ReadOnlySpan<byte> planeIndices = blob.Slice(0, 128);
        ReadOnlySpan<byte> planeData = blob.Slice(128);
        byte[] nibbleData = new byte[NibblesPerSection];

        for (int y = 0; y < 128; y++)
        {
            byte planeIndex = planeIndices[y];
            if (planeIndex == SparseAllZeroIndex)
                continue;

            if (supportsAllFifteenPlane && planeIndex == SparseAllFifteenIndex)
            {
                for (int xz = 0; xz < 256; xz++)
                    SetNibbleValue(nibbleData, xz, y, 15);
                continue;
            }

            int planeOffset = planeIndex * 128;
            if (planeOffset + 128 > planeData.Length)
                throw new InvalidDataException("Invalid sparse plane index.");

            ReadOnlySpan<byte> plane = planeData.Slice(planeOffset, 128);
            for (int xz = 0; xz < 128; xz++)
            {
                byte packed = plane[xz];
                SetNibbleValue(nibbleData, xz << 1, y, packed & 0x0F);
                SetNibbleValue(nibbleData, (xz << 1) + 1, y, (packed >> 4) & 0x0F);
            }
        }

        return nibbleData;
    }

    private static void SkipSparseNibbleStorage(byte[] payload, ref int offset)
    {
        int count = ReadInt32BigEndian(payload, ref offset);
        int storageBytes = 128 + (count * 128);
        if (count < 0 || offset + storageBytes > payload.Length)
            throw new InvalidDataException("Invalid SparseStorage payload.");

        offset += storageBytes;
    }

    private static int GetCompressedTileIndex(int block, int tile)
    {
        int index = ((block & 0x180) << 6) | ((block & 0x060) << 4) | ((block & 0x01F) << 2);
        index |= ((tile & 0x30) << 7) | ((tile & 0x0C) << 5) | (tile & 0x03);
        return index;
    }

    private static int GetNibbleValue(byte[] nibbleData, int xz, int y)
    {
        int pos = (xz << 7) | y;
        int slot = pos >> 1;
        int part = pos & 1;
        byte value = nibbleData[slot];
        return part == 0 ? value & 0x0F : (value >> 4) & 0x0F;
    }

    private static void SetNibbleValue(byte[] nibbleData, int xz, int y, int value)
    {
        int pos = (xz << 7) | y;
        int slot = pos >> 1;
        int part = pos & 1;
        value &= 0x0F;

        if (part == 0)
            nibbleData[slot] = (byte)((nibbleData[slot] & 0xF0) | value);
        else
            nibbleData[slot] = (byte)((nibbleData[slot] & 0x0F) | (value << 4));
    }

    private static byte[] EnsureLength(byte[] input, int expectedLength)
    {
        if (input.Length == expectedLength)
            return input;

        byte[] resized = new byte[expectedLength];
        Buffer.BlockCopy(input, 0, resized, 0, Math.Min(input.Length, expectedLength));
        return resized;
    }

    private static byte[] CreateFilledArray(int length, byte value)
    {
        byte[] buffer = new byte[length];
        Array.Fill(buffer, value);
        return buffer;
    }

    private static short ReadInt16BigEndian(byte[] payload, ref int offset)
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static int ReadInt32BigEndian(byte[] payload, ref int offset)
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static long ReadInt64BigEndian(byte[] payload, ref int offset)
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    private static byte[] ReadSizedBytes(byte[] payload, ref int offset, int length)
    {
        byte[] result = payload.AsSpan(offset, length).ToArray();
        offset += length;
        return result;
    }

    private static void WriteInt16BigEndian(Stream stream, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64BigEndian(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
