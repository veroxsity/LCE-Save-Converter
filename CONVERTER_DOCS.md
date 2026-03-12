# LCE World Converter — Comprehensive Technical Documentation

## Overview

This document covers everything needed to build a Java Edition → Minecraft Legacy Console Edition (LCE) world converter.
It is based on direct analysis of the LCE source code located at:

```
C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\
```

The converter is a novel tool. No existing public converter properly supports converting **to** LCE format
because the save format was proprietary and undocumented until this source code became available.

---

## Part 1 — Game Version Mapping

### LCE Version (TU19)

The source code is **Minecraft Legacy Console Edition TU19**, equivalent to **Java Edition 1.6.4**.

Evidence from source:
- `FileHeader.h` defines `SAVE_FILE_VERSION_CHUNK_INHABITED_TIME` with comment `(1.6.4)`
- `SharedConstants.h` `NETWORK_PROTOCOL_VERSION = 78` — matches Java 1.6.4
- `McRegionChunkStorage.cpp` uses `.mcr` region files — the McRegion format used by Java up to 1.6.x
- Block IDs throughout are numeric, matching Java 1.6.4 pre-Flattening era

### Java Edition Chunk Format History

| Java Version | Chunk Format | Region Format | Notes |
|---|---|---|---|
| Alpha/Beta | Old Chunk (`.dat` per chunk) | None | `OldChunkStorage.cpp` reads this |
| 1.2 – 1.6.x | McRegion NBT | `.mcr` files | **Same format as LCE** |
| 1.7 – 1.12.x | Anvil NBT | `.mca` files | Block IDs still numeric |
| 1.13+ | Anvil with Block States | `.mca` files | String block IDs — requires full remapping |

**The ideal source format is Java 1.6.4** — it uses the same `.mcr` McRegion format that LCE uses internally,
making the conversion nearly 1:1 with only world boundary cropping and the `saveData.ms` container wrapping required.

**Java 1.7 – 1.12.2** is also feasible — same numeric block IDs, just a different region file format (`.mca` vs `.mcr`).
The chunk NBT structure changed slightly but block IDs are still compatible.

**Java 1.13+** requires a full block ID remapping pass (string → numeric) before conversion. Much more complex.

---

## Part 2 — LCE World Size Constraints

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\ChunkSource.h`

```cpp
#define LEVEL_LEGACY_WIDTH 54        // Default world = 54x54 chunks = 864x864 blocks
#define LEVEL_MAX_WIDTH 54           // (non _LARGE_WORLDS build)
#define LEVEL_MIN_WIDTH 54

#define HELL_LEVEL_LEGACY_SCALE 3    // Nether is 1/3 the scale of overworld
#define HELL_LEVEL_LEGACY_SCALE 3    // Nether = 18x18 chunks = 288x288 blocks
#define END_LEVEL_MAX_WIDTH 18       // End = 18x18 chunks = 288x288 blocks
```

With `_LARGE_WORLDS` defined (the Windows64 build uses this):

```cpp
#define LEVEL_MAX_WIDTH (5*64)       // = 320 chunks = 5120 blocks
#define LEVEL_WIDTH_CLASSIC 54
#define LEVEL_WIDTH_SMALL   64
#define LEVEL_WIDTH_MEDIUM  (3*64)   // = 192 chunks
#define LEVEL_WIDTH_LARGE   (5*64)   // = 320 chunks
```

The `_LARGE_WORLDS` define is active in the Windows64 build. The `server.properties` `level-type` controls
which size is used at world creation time.

### World Centre

LCE worlds are **centred at chunk (0,0)**. The chunk iteration in `ConsoleSaveFileConverter.cpp` confirms:

```cpp
int halfXZSize = xzSize / 2;
for(int x = -halfXZSize; x < halfXZSize; ++x)
    for(int z = -halfXZSize; z < halfXZSize; ++z)
```

For a 54-chunk world: chunks `-27` to `+26` in both X and Z.
For a 320-chunk large world: chunks `-160` to `+159` in both X and Z.

**Java worlds have no fixed centre** — spawn is wherever the world generated it.
When converting, you must **recentre** the Java world so Java's spawn point maps to LCE chunk (0,0).

### Nether and End Bounds

| Dimension | LCE Path | Chunk Range (legacy) |
|---|---|---|
| Overworld | `r.X.Z.mcr` (root) | -27 to +26 |
| Nether | `DIM-1/r.X.Z.mcr` | -9 to +8 |
| End | `DIM1/r.X.Z.mcr` | -9 to +8 |

The converter in `ConsoleSaveFileConverter.cpp` iterates all three dimensions separately.

---

## Part 3 — The `saveData.ms` Container Format

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\FileHeader.h`
Implementation: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\ConsoleSaveFileOriginal.h`

### File Structure

`saveData.ms` is a proprietary container — a flat binary file that holds all world files (region files,
level.dat, player data, game rules) in a single blob with a header/footer index.

```
[4 bytes]  Offset to header (footer location, written at end of file)
[4 bytes]  Size of header in bytes
[2 bytes]  Original save version (ESaveVersions enum)
[2 bytes]  Current save version (ESaveVersions enum)
... file data blocks ...
[header/footer] File table — array of FileEntrySaveData structs
```

### FileEntrySaveData struct

Source: `FileHeader.h`

```cpp
struct FileEntrySaveDataV2
{
    wchar_t  filename[64];     // 128 bytes — wide char filename e.g. L"r.0.0.mcr"
    uint32_t length;           // File size in bytes
    uint32_t startOffset;      // Byte offset from start of saveData.ms
    int64_t  lastModifiedTime; // Milliseconds since epoch
};
```

### Save Versions (ESaveVersions)

Source: `FileHeader.h`

| Enum | Value | Description |
|---|---|---|
| `SAVE_FILE_VERSION_PRE_LAUNCH` | 1 | Pre-release Xbox 360 |
| `SAVE_FILE_VERSION_LAUNCH` | 2 | Xbox 360 launch |
| `SAVE_FILE_VERSION_POST_LAUNCH` | 3 | First breaking changes |
| `SAVE_FILE_VERSION_NEW_END` | 4 | The End added |
| `SAVE_FILE_VERSION_MOVED_STRONGHOLD` | 5 | Stronghold gen changed |
| `SAVE_FILE_VERSION_CHANGE_MAP_DATA_MAPPING_SIZE` | 6 | PS3 UID format change |
| `SAVE_FILE_VERSION_DURANGO_CHANGE_MAP_DATA_MAPPING_SIZE` | 7 | Xbox One UID change |
| `SAVE_FILE_VERSION_COMPRESSED_CHUNK_STORAGE` | 8 | Chunks stored pre-compressed |
| `SAVE_FILE_VERSION_CHUNK_INHABITED_TIME` | 9 | InhabitedTime added (1.6.4) |

The Windows64 PC build uses version **9** (`SAVE_FILE_VERSION_CHUNK_INHABITED_TIME`).
The `originalSaveVersion` field tracks what version the save was first created at.

### Platform Tags (ESavePlatform)

Source: `FileHeader.h`

```cpp
SAVE_FILE_PLATFORM_X360  = MAKE_FOURCC('X','3','6','0')  // Big-endian
SAVE_FILE_PLATFORM_XBONE = MAKE_FOURCC('X','B','1','_')  // Little-endian
SAVE_FILE_PLATFORM_PS3   = MAKE_FOURCC('P','S','3','_')  // Big-endian
SAVE_FILE_PLATFORM_PS4   = MAKE_FOURCC('P','S','4','_')  // Little-endian
SAVE_FILE_PLATFORM_PSVITA= MAKE_FOURCC('P','S','V','_')  // Little-endian
SAVE_FILE_PLATFORM_WIN64 = MAKE_FOURCC('W','I','N','_')  // Little-endian
```

**Important:** Xbox 360 and PS3 saves are **big-endian**. All others are **little-endian**.
The converter must handle endian swapping when reading from or writing to a different platform.
For a converter targeting Windows64/PC, always write **little-endian**.

### Files Inside saveData.ms

The file table typically contains:

| Filename | Description |
|---|---|
| `level.dat` | World metadata (NBT compressed) |
| `r.X.Z.mcr` | Overworld region files |
| `DIM-1r.X.Z.mcr` | Nether region files (note: no slash in some versions) |
| `DIM1/r.X.Z.mcr` | End region files |
| `players/XUID.dat` | Player save data |
| `data/villages.dat` | Village data |
| `data/Fortress.dat` | Nether fortress data |
| `data/Stronghold.dat` | Stronghold location data |
| `GameRules.nbt` | Game rules |

The initial region files are created in a fixed order in `McRegionChunkStorage.cpp`:

```cpp
// These are created first so they appear first in the file table — important for load speed
DIM-1r.-1.-1.mcr, DIM-1r.0.-1.mcr, DIM-1r.0.0.mcr, DIM-1r.-1.0.mcr
DIM1/r.-1.-1.mcr, DIM1/r.0.-1.mcr, DIM1/r.0.0.mcr, DIM1/r.-1.0.mcr
r.-1.-1.mcr, r.0.-1.mcr, r.0.0.mcr, r.-1.0.mcr
```

---

## Part 4 — Region File Format

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\RegionFile.h`
Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\RegionFile.cpp`

LCE uses the **McRegion** format (`.mcr`) — identical to Java Edition 1.2–1.6.x.

### Region File Layout

```
[4096 bytes] Offset table  — 1024 x int32 (one per chunk slot, XZ encoded)
[4096 bytes] Timestamp table — 1024 x int32 (Unix timestamps)
[variable]   Chunk data sectors (each sector = 4096 bytes)
```

Chunk coordinates within a region: `x mod 32`, `z mod 32`
Region file for chunk (x,z): `r.(x>>5).(z>>5).mcr`

### Chunk Entry in Offset Table

```
Bits 31-8: Sector offset (3 bytes) — sector number where chunk starts
Bits  7-0: Sector count  (1 byte)  — number of 4096-byte sectors used
```

### Chunk Data

```
[4 bytes] Exact byte length of chunk data
[1 byte]  Compression type:
            1 = GZip (VERSION_GZIP)
            2 = Deflate/zlib (VERSION_DEFLATE)
            3 = Xbox custom (VERSION_XBOX)
[variable] Compressed NBT chunk data
```

LCE uses **VERSION_XBOX (3)** for its own compression format. For conversion purposes,
reading Java `.mcr` files uses VERSION_DEFLATE (2), and writing LCE files also uses deflate
or the Xbox format. The `RegionFile` class handles both transparently.

### Compression versions

```cpp
// RegionFile.h
static const int VERSION_GZIP    = 1;  // Java default — rarely used
static const int VERSION_DEFLATE = 2;  // Java standard (zlib deflate)
static const int VERSION_XBOX    = 3;  // 4J custom compression
```

---

## Part 5 — Chunk NBT Format

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\OldChunkStorage.cpp`
Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\OldChunkStorage.h`

### Pre-version-8 chunk format (NBT-based)

Before `SAVE_FILE_VERSION_COMPRESSED_CHUNK_STORAGE` (version 8), chunks are stored as standard NBT:

```
CompoundTag root {
    CompoundTag "Level" {
        int   "xPos"             -- chunk X coordinate
        int   "zPos"             -- chunk Z coordinate
        byte[] "Blocks"          -- 32768 bytes (16x128x16), YZX order
        byte[] "Data"            -- 16384 bytes (nibble array, metadata)
        byte[] "SkyLight"        -- 16384 bytes (nibble array)
        byte[] "BlockLight"      -- 16384 bytes (nibble array)
        byte[] "HeightMap"       -- 256 bytes (16x16)
        byte[] "Biomes"          -- 256 bytes (16x16) — 4J added
        List   "Entities"        -- entity list
        List   "TileEntities"    -- tile entity list
        long   "LastUpdate"      -- game tick
        long   "InhabitedTime"   -- time players spent in chunk (added v9/1.6.4)
        short  "TerrainPopulated"-- 4J: bitfield, not just bool
    }
}
```

This is **identical to Java Edition 1.6.4 McRegion chunk NBT format** with two additions:
- `Biomes` byte array (4J added — Java stored biomes differently)
- `TerrainPopulated` is a short bitfield not a boolean (4J change)

### Post-version-8 chunk format (compressed binary)

After version 8, `OldChunkStorage::save(LevelChunk*, Level*, DataOutputStream*)` writes a compressed
binary stream directly — not standard NBT. This is what the Windows64 PC build uses. The format:

```
[compressed block data]     writeCompressedBlockData()
[compressed metadata]       writeCompressedDataData()
[compressed sky light]      writeCompressedSkyLightData()
[compressed block light]    writeCompressedBlockLightData()
[byte[256] heightmap]
[byte[256] biomes]
[short terrainPopulated]
[long lastUpdate]
[long inhabitedTime]
[int tileEntityCount]
    for each TileEntity: NbtIo::write(tag)
[int entityCount]
    for each Entity: NbtIo::write(tag)
```

**For the converter: read Java NBT chunks, write using the pre-version-8 NBT path for maximum compatibility,
then let the LCE server upgrade the format on first load.**

---

## Part 6 — level.dat Format

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\LevelData.h`
Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\LevelData.cpp`

LCE `level.dat` is **GZip-compressed NBT**, same as Java Edition.

### Required fields (LCE additions marked with ★)

```
CompoundTag "Data" {
    long   "RandomSeed"
    string "LevelName"
    string "generatorName"       -- "default" or "flat"
    string "generatorOptions"    -- flat world config string
    int    "version"             -- always 19133 (LevelData version)
    int    "SpawnX"
    int    "SpawnY"
    int    "SpawnZ"
    long   "Time"                -- game tick
    long   "DayTime"             -- time within current day
    long   "LastPlayed"          -- Unix timestamp ms
    long   "SizeOnDisk"          -- approximate save size
    byte   "raining"
    int    "rainTime"
    byte   "thundering"
    int    "thunderTime"
    int    "GameType"            -- 0=survival, 1=creative
    byte   "MapFeatures"         -- generate structures
    byte   "hardcore"
    byte   "allowCommands"
    byte   "initialized"
    byte   "newSeaLevel"         ★ LCE only
    byte   "hasBeenInCreative"   ★ LCE only
    byte   "spawnBonusChest"     ★ LCE only
    int    "xzSize"              ★ LCE only — world size in chunks (54 legacy)
    int    "hellScale"           ★ LCE only — nether scale factor (3 legacy)
    int    "xStronghold"         ★ LCE only — cached stronghold location
    int    "yStronghold"         ★ LCE only
    int    "zStronghold"         ★ LCE only
    byte   "hasStronghold"       ★ LCE only
    int    "xStrongholdEP"       ★ LCE only — End Portal location
    int    "zStrongholdEP"       ★ LCE only
    byte   "hasStrongholdEP"     ★ LCE only
    CompoundTag "GameRules" { ... }
}
```

When converting from Java, copy all standard fields and **add the LCE-specific fields** with sensible defaults:
- `xzSize` = 54 (legacy) or 320 (large world)
- `hellScale` = 3
- `newSeaLevel` = 1
- `hasBeenInCreative` = 0
- `spawnBonusChest` = 0
- Stronghold fields = 0 / false (LCE will locate it on first load)

---

## Part 7 — Block ID Compatibility

Because LCE TU19 = Java 1.6.4, block IDs are **numerically identical** for all blocks that existed
in that era. No block ID remapping is needed for Java 1.6.4 → LCE.

### Block IDs that exist in Java 1.6.4 and LCE TU19 (identical)

All standard blocks 0–163 are compatible. Key ones:

| ID | Block |
|---|---|
| 0 | Air |
| 1 | Stone |
| 2 | Grass |
| 3 | Dirt |
| 4 | Cobblestone |
| 5 | Wood Planks |
| 12 | Sand |
| 13 | Gravel |
| 17 | Wood Log |
| 18 | Leaves |
| 35 | Wool |
| 54 | Chest |
| 116 | Enchantment Table |
| 130 | Ender Chest |
| 145 | Anvil |
| 152 | Redstone Block |
| 158 | Dropper |
| 163–166 | Stained glass (added TU14/Java 1.7, see below) |

### Blocks added AFTER Java 1.6.4 / TU19

If converting from Java 1.7+, these blocks did not exist in TU19 and must be handled:

| Java Version | Blocks Added | Strategy |
|---|---|---|
| 1.7 | Stained glass (95, 160), Podzol (243), Red Sand (159) | Replace with nearest equivalent or air |
| 1.8 | Slime block, Red Sandstone, Banners, Armor stands | Replace with air/nearest |
| 1.9+ | End rods, Chorus, Purpur, Shulker boxes | Replace with air |
| 1.13+ | All blocks renamed + new palette system | Full remapping required |

**For Java 1.6.4 source**: no block remapping needed at all — perfect 1:1.
**For Java 1.7–1.8 source**: small remapping table needed (~20 blocks).
**For Java 1.9–1.12 source**: moderate remapping table needed (~80 blocks).
**For Java 1.13+ source**: full string→numeric remapping needed — complex but feasible.

### Chunk height

Java 1.6.4 and LCE both use **128 blocks** height (Y = 0–127).
Java 1.8+ increased this to 256. If converting from 1.8+, blocks above Y=127 must be dropped.
The `maxBuildHeight` in `LevelData` for LCE is capped at 256 in `server.properties` but the actual
chunk storage uses 128 layers — set `maxBuildHeight = 128` in the output level.dat.

---

## Part 8 — Existing Converter Code in the Source

The LCE source already has **platform-to-platform converter** logic you can build on directly.

### ConsoleSaveFileConverter

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\ConsoleSaveFileConverter.cpp`

The `ConvertSave(sourceSave, targetSave, progress)` method:

1. Copies `level.dat` verbatim
2. Copies game rules
3. Copies player dat files (handling platform-specific XUID mapping)
4. Iterates overworld chunks from `-halfXZSize` to `+halfXZSize`
5. Iterates nether chunks at 1/3 scale
6. Iterates end chunks

This is your starting point. For a Java → LCE converter, replace step 4-6 with
logic that reads Java `.mcr`/`.mca` files and writes into an LCE `ConsoleSaveFileOriginal`.

### ProcessStandardRegionFile

```cpp
void ConsoleSaveFileConverter::ProcessStandardRegionFile(
    ConsoleSaveFile *sourceSave, File sourceFile,
    ConsoleSaveFile *targetSave, File targetFile)
```

Opens a `RegionFile` on both source and target, iterates all 32×32 chunk slots,
and copies raw chunk data streams. For a Java → LCE converter, the source side
would read from a standard filesystem `.mcr` file instead of a `ConsoleSaveFile`.

### RegionFile class

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\RegionFile.h`

Key methods:
- `getChunkDataInputStream(x, z)` → `DataInputStream*` — reads and decompresses a chunk
- `getChunkDataOutputStream(x, z)` → `DataOutputStream*` — writes and compresses a chunk
- `hasChunk(x, z)` → `bool`

The `RegionFile` operates through the `ConsoleSaveFile` interface.
For reading Java files, you'd need a thin `ConsoleSaveFile` wrapper around a standard filesystem file,
or extend `RegionFile` to take a file path directly.

### NbtIo

Source: `C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\Minecraft.World\NbtIo.h`

```cpp
static CompoundTag* NbtIo::readCompressed(DataInput*)   // reads GZip NBT
static CompoundTag* NbtIo::read(DataInput*)             // reads uncompressed NBT
static void NbtIo::write(CompoundTag*, DataOutput*)
static void NbtIo::writeCompressed(CompoundTag*, DataOutput*)
```

Full NBT read/write is already implemented. Use these to read Java `level.dat` and chunk NBT.

---

## Part 9 — Coordinate Remapping

This is the most critical transformation in the converter.

### The Problem

Java worlds are infinite and centred wherever the seed places spawn.
LCE worlds are finite and centred at chunk (0,0) in the region file system.

### The Solution

1. Read the Java `level.dat` to get `SpawnX` and `SpawnZ`
2. Compute the Java spawn chunk: `spawnChunkX = SpawnX >> 4`, `spawnChunkZ = SpawnZ >> 4`
3. For every chunk you read from Java at `(jx, jz)`, write it to LCE at `(jx - spawnChunkX, jz - spawnChunkZ)`
4. Clamp to LCE bounds: only write chunks where `-halfXZSize <= lcx < halfXZSize`
5. Update `level.dat` spawn point: `SpawnX = SpawnX - (spawnChunkX * 16)`, same for Z

### Nether Coordinate Remapping

Nether coordinates are at 1/8 scale in Java but 1/3 scale in LCE (`HELL_LEVEL_LEGACY_SCALE = 3`).
The nether spawn/portal locations in player data and `level.dat` will need adjusting accordingly.

### Chunk coordinate in region file

Java region filename `r.X.Z.mcr` contains chunks `(X*32)` to `(X*32 + 31)` in each axis.
After remapping, recalculate region file names based on the new LCE chunk coordinates.

---

## Part 10 — Key Source Files Reference

All paths relative to:
`C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\`

### Save Container

| File | Purpose |
|---|---|
| `Minecraft.World\ConsoleSaveFile.h` | Abstract interface for the save container |
| `Minecraft.World\ConsoleSaveFileOriginal.h` | Concrete `saveData.ms` implementation |
| `Minecraft.World\ConsoleSaveFileOriginal.cpp` | Read/write logic for the container |
| `Minecraft.World\FileHeader.h` | File table structs, `ESaveVersions`, `ESavePlatform` |
| `Minecraft.World\ConsoleSavePath.h` | Path wrapper used by save file API |
| `Minecraft.World\ConsoleSaveFileConverter.h` | Platform-to-platform converter interface |
| `Minecraft.World\ConsoleSaveFileConverter.cpp` | Converter implementation — start here |
| `Minecraft.World\ConsoleSaveFileInputStream.h` | Read stream into save file |
| `Minecraft.World\ConsoleSaveFileOutputStream.h` | Write stream into save file |

### Region Files

| File | Purpose |
|---|---|
| `Minecraft.World\RegionFile.h` | McRegion `.mcr` file implementation |
| `Minecraft.World\RegionFile.cpp` | Read/write chunks with compression |
| `Minecraft.World\RegionFileCache.h` | Cache for open region files |
| `Minecraft.World\RegionFileCache.cpp` | `getChunkDataInputStream/OutputStream` helpers |

### Chunk Storage

| File | Purpose |
|---|---|
| `Minecraft.World\McRegionChunkStorage.h` | McRegion chunk storage (used by PC build) |
| `Minecraft.World\McRegionChunkStorage.cpp` | Load/save chunks via region files |
| `Minecraft.World\OldChunkStorage.h` | Raw chunk serialisation |
| `Minecraft.World\OldChunkStorage.cpp` | NBT and binary chunk read/write |
| `Minecraft.World\LevelChunk.h` | Chunk data structure |
| `Minecraft.World\ChunkSource.h` | World size constants (`LEVEL_LEGACY_WIDTH` etc.) |
| `Minecraft.World\ZoneFile.h` | Alternative storage (older format) |
| `Minecraft.World\ZoneIo.h` | Zone I/O helpers |

### World Metadata

| File | Purpose |
|---|---|
| `Minecraft.World\LevelData.h` | `level.dat` NBT fields |
| `Minecraft.World\LevelData.cpp` | Read/write `level.dat` |
| `Minecraft.World\LevelSettings.h` | World creation settings |
| `Minecraft.World\LevelStorage.h` | Storage interface |
| `Minecraft.World\DirectoryLevelStorage.h` | Filesystem-based storage |
| `Minecraft.World\DirectoryLevelStorage.cpp` | Player data path helpers |

### NBT I/O

| File | Purpose |
|---|---|
| `Minecraft.World\NbtIo.h` | NBT read/write (GZip + raw) |
| `Minecraft.World\NbtIo.cpp` | Implementation |
| `Minecraft.World\CompoundTag.h` | NBT CompoundTag |
| `Minecraft.World\Tag.h` | NBT base tag |
| `Minecraft.World\com.mojang.nbt.h` | NBT type includes |

### Compression

| File | Purpose |
|---|---|
| `Minecraft.World\compression.h` | zlib wrappers |
| `Minecraft.World\compression.cpp` | Deflate/inflate |
| `Minecraft.World\DataInputStream.h` | Read stream |
| `Minecraft.World\DataOutputStream.h` | Write stream |
| `Minecraft.World\ByteArrayInputStream.h` | In-memory read |
| `Minecraft.World\ByteArrayOutputStream.h` | In-memory write |

---

## Part 11 — Recommended Converter Architecture

The converter should be a standalone C++ tool that links against the relevant
parts of `Minecraft.World` only (no client code needed).

### Project Structure

```
LceWorldConverter/
  CMakeLists.txt
  src/
    main.cpp               -- CLI entry point
    JavaWorldReader.h/cpp  -- reads Java .mcr/.mca region files from filesystem
    LceWorldWriter.h/cpp   -- wraps ConsoleSaveFileOriginal to write saveData.ms
    ChunkRemapper.h/cpp    -- coordinate remapping + world boundary clipping
    BlockRemapper.h/cpp    -- block ID remapping table (for Java 1.7+ source)
    LevelDatConverter.h/cpp-- reads Java level.dat, outputs LCE level.dat
  lib/
    (symlink or copy of relevant Minecraft.World source files)
```

### Dependencies from Minecraft.World to include

Minimum set needed:

```
ConsoleSaveFileOriginal.cpp/.h
ConsoleSaveFile.h
FileHeader.h
ConsoleSavePath.h
RegionFile.cpp/.h
RegionFileCache.cpp/.h
McRegionChunkStorage.cpp/.h   (optional — can use RegionFile directly)
OldChunkStorage.cpp/.h
LevelData.cpp/.h
NbtIo.cpp/.h
compression.cpp/.h
DataInputStream.cpp/.h
DataOutputStream.cpp/.h
ByteArrayInputStream.cpp/.h
ByteArrayOutputStream.cpp/.h
BufferedOutputStream.cpp/.h
Tag.cpp/.h + all tag types
CompoundTag.h, ListTag.h etc.
```

### Step-by-step conversion flow

```
1. Parse CLI args: java_world_path, output_savedata_ms_path, [--large-world]

2. Read Java level.dat
   - NbtIo::readCompressed(FileInputStream(java_world/level.dat))
   - Extract SpawnX, SpawnZ, seed, worldName, gameType, etc.

3. Compute recentring offset
   - spawnChunkX = SpawnX >> 4
   - spawnChunkZ = SpawnZ >> 4

4. Determine world size
   - legacy: halfSize = 27 (54-chunk world)
   - large:  halfSize = 160 (320-chunk world)

5. Create output saveData.ms
   - ConsoleSaveFileOriginal writer(outputPath)
   - writer.setLocalPlatform()  // WIN64
   - writer.setSaveVersion(SAVE_FILE_VERSION_CHUNK_INHABITED_TIME)

6. Write LCE level.dat
   - Copy standard fields from Java
   - Add LCE-specific fields (xzSize, hellScale, etc.)
   - Update spawn to be relative to new centre
   - NbtIo::writeCompressed(tag, ConsoleSaveFileOutputStream(writer, "level.dat"))

7. Convert overworld chunks
   for lcx = -halfSize to halfSize-1:
     for lcz = -halfSize to halfSize-1:
       jx = lcx + spawnChunkX
       jz = lcz + spawnChunkZ
       regionFile = openJavaRegionFile(jx >> 5, jz >> 5)
       dis = regionFile.getChunkDataInputStream(jx & 31, jz & 31)
       if dis != null:
         chunkNbt = NbtIo::read(dis)
         remapChunkCoordinates(chunkNbt, lcx, lcz)
         remapBlocks(chunkNbt)  // only needed for Java 1.7+ source
         dos = lceRegionFileCache.getChunkDataOutputStream(writer, "", lcx, lcz)
         NbtIo::write(chunkNbt, dos)

8. Convert nether chunks (scale 1/3)
   - hellHalfSize = halfSize / 3
   - Same process, source prefix "DIM-1/", target prefix "DIM-1/"
   - Nether coordinate scaling: lce_nether_chunk = java_nether_chunk * (3.0/8.0)

9. Convert End chunks
   - endHalfSize = 9 (18-chunk end)
   - Source prefix "DIM1/", target prefix "DIM1/"

10. Copy player data
    - For each players/*.dat file in the Java world:
      read player NBT, update position coordinates, write to saveData.ms

11. Finalise
    - writer.Flush(false)
```

---

## Part 12 — Java Region File Reading (Filesystem, Not saveData.ms)

Java worlds store region files as loose files on the filesystem, not inside a container.
You need a thin wrapper to read them directly since `RegionFile` in the source expects a `ConsoleSaveFile`.

### Option A — Extend RegionFile for filesystem

Create a `FilesystemConsoleSaveFile` class that implements the `ConsoleSaveFile` interface
but reads/writes from a standard Windows file handle instead of the in-memory saveData.ms blob.

Key methods to implement:
```cpp
virtual BOOL readFile(FileEntry*, LPVOID, DWORD, LPDWORD)   // -> ReadFile()
virtual BOOL writeFile(FileEntry*, LPCVOID, DWORD, LPDWORD) // -> WriteFile()
virtual void setFilePointer(FileEntry*, LONG, PLONG, DWORD) // -> SetFilePointer()
virtual bool doesFileExist(ConsoleSavePath)                  // -> GetFileAttributes()
```

### Option B — Read Java .mcr files directly

Since McRegion format is well-documented, implement a simpler standalone reader:

```cpp
class JavaRegionFile {
    // Read 8KB header (offsets + timestamps)
    // For chunk (lx, lz): offset = header[(lx & 31) + (lz & 31) * 32]
    // Seek to offset * 4096, read 5-byte chunk header (length + compression)
    // Decompress chunk data (zlib deflate)
    // Parse NBT
};
```

### Java .mca vs .mcr differences

`.mca` (Anvil, Java 1.7+) uses the same header format as `.mcr` (McRegion).
The only difference is compression type byte:
- `.mcr`: type 2 (deflate) is standard
- `.mca`: type 2 (deflate) is standard, type 1 (gzip) also valid

The chunk NBT inside differs:
- `.mcr`: `"Blocks"` is a flat `byte[]` array, Y first (YZX ordering)
- `.mca`: uses `"Sections"` list with 16-block-high sub-chunks

For `.mca` source you must flatten the sections back into a single 128-byte array,
dropping any sections above Y=7 (Y=112).

---

## Part 13 — Biome Handling

LCE stores biomes as a `byte[256]` array in each chunk NBT under key `"Biomes"`.
Java 1.6.4 also stores biomes per-chunk as `byte[256]` — direct copy is fine.

Java 1.7+ still uses `byte[256]` per chunk for biomes — still a direct copy.
Java 1.18+ changed biomes to 3D per-section — requires collapsing to 2D for LCE.

Biome IDs are compatible between Java 1.6.4 and LCE for all biomes that existed then.

---

## Part 14 — Entity and TileEntity Handling

### Entities

Entity NBT is largely compatible between Java 1.6.4 and LCE since LCE is based on that code.
Notable differences:
- LCE drops entities that don't exist in TU19 (e.g. bats were TU14, horses TU12 — both exist)
- Position coordinates need adjusting for the world recentring
- Entity UUIDs use a different format on some platforms — safest to regenerate

### TileEntities

Chest, furnace, sign, etc. NBT is identical to Java 1.6.4.
Contents (item stacks) use numeric item IDs — same as Java 1.6.4.

### Items

Item IDs are numerically identical to Java 1.6.4. Damage values (metadata) are the same.
For Java 1.13+ source, item IDs need remapping from string to numeric.

---

## Part 15 — Platform Conversion (LCE to LCE)

The existing `ConsoleSaveFileConverter::ConvertSave()` handles converting an existing
`saveData.ms` from one LCE platform to another. This is useful for:

- Converting an Xbox 360 save (big-endian) to Windows64 (little-endian)
- Converting a PS3 save to Windows64

The endian swap is handled transparently by `ConsoleSaveFile::isSaveEndianDifferent()`
and `ConvertRegionFile()`.

Usage flow:
```cpp
ConsoleSaveFileOriginal source("input_saveData.ms", sourceData, sourceSize);
source.setPlatform(SAVE_FILE_PLATFORM_X360);  // or wherever it came from

ConsoleSaveFileOriginal target("output_saveData.ms");
target.setLocalPlatform();  // WIN64

ConsoleSaveFileConverter::ConvertSave(&source, &target, nullptr);
target.Flush(false);
```

---

## Part 16 — Build Notes

### Files NOT needed from Minecraft.Client

The converter is purely a world format tool. You do **not** need:
- Any rendering code
- D3D / graphics
- Input handling
- Network code
- The 4JLibs

Link only against `Minecraft.World` source files listed in Part 10.

### Preprocessor defines needed

```cpp
#define _WINDOWS64
#define _LARGE_WORLDS   // to get the full world size constants
#define SPLIT_SAVES     // the PC build uses split entity saves
```

### stdafx.h dependency

`Minecraft.World` files all `#include "stdafx.h"`. Create a minimal `stdafx.h` for the
converter project that just includes Windows headers and standard library:

```cpp
#define WIN32_LEAN_AND_MEAN
#define _HAS_STD_BYTE 0
#include <windows.h>
#include <string>
#include <vector>
#include <unordered_map>
#include <memory>
#include <algorithm>
```

---

## Part 17 — Quick Reference: Conversion Checklist

- [x] Read Java `level.dat` → compute spawn chunk offset
- [x] Create `ConsoleSaveFileOriginal` output (WIN64 platform)
    Current implementation writes save version 7 (pre-v8 path) for stability.
- [x] Write LCE `level.dat` with extra fields (`xzSize`, `hellScale`, etc.)
- [x] Iterate overworld chunks in LCE coordinate space, remap from Java
- [x] Iterate nether chunks (scale: Java 1/8 source offset applied)
- [x] Iterate End chunks
- [x] Copy/remap player dat files (legacy `players/` only)
- [x] For Java 1.7+: remap Anvil `.mca` sections to flat `byte[128*16*16]` blocks
- [x] For Java 1.7–1.12: remap unknown block IDs to air or equivalents
- [ ] For Java 1.13+: full string → numeric block ID remapping
- [x] For Java 1.8+: drop/remap blocks above Y=127
- [x] Update entity positions for world recentring
- [x] Write final save container blob (equivalent output-finalization step)

---

*Document generated from direct source code analysis of:*
*`C:\Users\Dan\Documents\Programming\LCE World Converter\Minecraft-LegacyConsoleEdition\`*
*LCE TU19 (v1.6.0560.0) — Java Edition equivalent: 1.6.4*
