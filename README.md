# LCE World Converter

A C++ tool that converts Java Edition Minecraft worlds into the Minecraft Legacy Console Edition (LCE) `saveData.ms` format.

Prior to the LCE source code becoming publicly available, no tool could properly convert worlds **to** LCE format because the `saveData.ms` container and internal chunk storage were completely undocumented. This project changes that.

---

## How It Works

Java Edition 1.6.4 and LCE TU19 are built on the same codebase — both use the McRegion `.mcr` chunk format with identical numeric block IDs. Converting a Java 1.6.4 world to LCE is nearly a 1:1 operation. The main tasks are:

- Recentring the world around the Java spawn point so it maps to LCE's chunk (0,0)
- Cropping to the LCE world boundary (54x54 chunks legacy, up to 320x320 large world)
- Writing all region files, `level.dat` and player data into the `saveData.ms` container
- Adding LCE-specific `level.dat` fields (`xzSize`, `hellScale`, etc.)

---

## Supported Source Formats

| Java Version | Region Format | Block IDs | Notes |
|---|---|---|---|
| 1.6.4 | McRegion `.mcr` | Numeric | Near 1:1, no remapping needed |
| 1.7 – 1.12.2 | Anvil `.mca` | Numeric | Minor block remapping, section flattening |
| 1.13+ | Anvil `.mca` | String | Full block ID remapping required |

---

## Output

A valid `saveData.ms` file targeting the **Windows64 PC platform** (little-endian, save version 9).
Drop it into your LCE server's `GameHDD/<worldname>/` folder and set `level-name` in `server.properties`.

Compatible with the [smartcmd/MinecraftConsoles](https://github.com/smartcmd/MinecraftConsoles) dedicated server.

---

## Building

Requires Visual Studio 2022 and the LCE source code.

```
git clone <this repo>
cd LceWorldConverter
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

The converter links against files from `Minecraft.World` in the LCE source tree.
Set the `LCE_SOURCE_ROOT` CMake variable to point at your local clone of the LCE source:

```
cmake -S . -B build -DLCE_SOURCE_ROOT="/path/to/MinecraftConsoles" -G "Visual Studio 17 2022" -A x64
```

---

## Usage

```
LceWorldConverter.exe <java_world_folder> <output_saveData.ms> [options]

Options:
  --large-world     Use 320-chunk world size instead of legacy 54-chunk
  --no-nether       Skip nether conversion
  --no-end          Skip end conversion
```

Example:

```
LceWorldConverter.exe "path/to/JavaWorld" "path/to/output/saveData.ms"
```

---

## Technical Reference

See [CONVERTER_DOCS.md](CONVERTER_DOCS.md) for full technical documentation including:

- LCE save file format (`saveData.ms` binary layout, file table, save versions)
- Region file format and chunk NBT structure
- World size constants and coordinate remapping algorithm
- Block ID compatibility table per Java version
- `level.dat` field mapping including LCE-specific fields
- Full source file reference with paths into the LCE codebase
- Step-by-step conversion flow

---

## LCE Source Reference

Based on analysis of **LCE TU19 (v1.6.0560.0)** — Java Edition equivalent 1.6.4.

Key source files used:

| File | Purpose |
|---|---|
| `Minecraft.World/ConsoleSaveFileOriginal.cpp` | `saveData.ms` container read/write |
| `Minecraft.World/ConsoleSaveFileConverter.cpp` | Platform-to-platform converter (reference) |
| `Minecraft.World/RegionFile.cpp` | McRegion `.mcr` file handling |
| `Minecraft.World/McRegionChunkStorage.cpp` | Chunk load/save via region files |
| `Minecraft.World/OldChunkStorage.cpp` | Raw chunk NBT serialisation |
| `Minecraft.World/LevelData.cpp` | `level.dat` read/write |
| `Minecraft.World/NbtIo.cpp` | NBT read/write |
| `Minecraft.World/ChunkSource.h` | World size constants |
| `Minecraft.World/FileHeader.h` | Save version and platform enums |

---

## Limitations

- Player inventory positions are not remapped for world recentring
- Nether portal linkages will need re-establishing in-game after conversion
- Java 1.13+ conversion requires a complete block ID mapping table (work in progress)
- LCE world height is 128 blocks — blocks above Y=127 from 1.8+ worlds are dropped
