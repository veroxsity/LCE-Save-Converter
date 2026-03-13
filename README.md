# LCE World Converter

[![Latest Release](https://img.shields.io/github/v/release/veroxsity/LCE-Save-Converter?display_name=tag)](https://github.com/veroxsity/LCE-Save-Converter/releases)
[![Downloads](https://img.shields.io/github/downloads/veroxsity/LCE-Save-Converter/total)](https://github.com/veroxsity/LCE-Save-Converter/releases)
[![GitHub Stars](https://img.shields.io/github/stars/veroxsity/LCE-Save-Converter?style=social)](https://github.com/veroxsity/LCE-Save-Converter/stargazers)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)

A C# .NET 8 converter for turning Java Edition Minecraft worlds into the Minecraft Legacy Console Edition (LCE) `saveData.ms` format, with both a CLI and a Windows GUI.

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

## Current 1.13+ Compatibility

Recent updates improved modern-world conversion fidelity (especially for large adventure/build worlds):

- Directional blockstate handling for doors, stairs, and trapdoors
- Flattened color block mapping (`<color>_wool`, glass, panes, carpet, terracotta, concrete)
- Deepslate-era fallback mappings to closest TU19 blocks
- Fluid `level` mapping for source vs flowing water/lava
- Slab `type` handling (`bottom`, `top`, `double`) for wood and stone slab families
- Sandstone and red-sandstone slab variants mapped to legacy sandstone slab families
- Redstone lamp state mapping (`lit=true/false`) to lit/unlit legacy lamps

For best results, always reconvert from the original Java world after updating this tool.

---

## Known Mismatches (Expected Fallbacks)

| Modern Java Block Family | LCE TU19 Output | Why |
|---|---|---|
| `red_sandstone*` (block/stairs/slabs) | Sandstone family (`24`/`128`/`44`/`43`) | Red sandstone does not exist in TU19 |
| Deepslate blocks (`deepslate*`) | Stone/cobblestone/stone-brick families | Deepslate does not exist in TU19 |
| Prismarine variants | Stone/Quartz-like fallbacks | Prismarine content is newer than TU19 |
| Purpur variants | Quartz-like fallbacks | Purpur does not exist in TU19 |
| Modern coral/kelp/observer/end-rod/etc. | Air or nearest legacy replacement | No compatible TU19 tile/entity behavior |

If a world depends on exact modern block identity, visual parity may differ in those areas.

---

## Output

A valid `saveData.ms` file targeting the **Windows64 PC platform** (little-endian, save version 7).
Drop it into your LCE server's `GameHDD/<worldname>/` folder and set `level-name` in `server.properties`.

If modern blocks are encountered that still have no mapping, the converter also writes:

- `unknown-modern-blocks.txt` (in the output directory): sorted list of block IDs that were mapped to air

Compatible with the [smartcmd/MinecraftConsoles](https://github.com/smartcmd/MinecraftConsoles) dedicated server.

---

## Building

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/veroxsity/LCE-Save-Converter.git
cd LCE-Save-Converter
dotnet build
```

To build the Windows GUI:

```bash
dotnet build ./LceWorldConverter.Gui/LceWorldConverter.Gui.csproj
```

To publish a Windows executable manually:

```bash
dotnet publish ./LceWorldConverter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Prebuilt binaries, when available, can be downloaded from the GitHub Releases page.

---

## Windows GUI

The repository now includes a Windows desktop app in `LceWorldConverter.Gui/` for the zip-based workflow:

1. Zip your Java world folder.
2. Launch the GUI build.
3. Click `Explore...` and pick the world zip.
4. Choose the output folder.
5. Click `Convert`.

The GUI extracts the zip to a temporary folder, converts the world, and writes `saveData.ms` into the output folder you selected.

## CLI Usage

```
dotnet run -- <java_world_folder_or_zip> [output_dir] [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions]
```

Published executable usage:

```powershell
.\LceWorldConverter.exe <java_world_folder_or_zip> [output_dir] [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions] [--copy-players] [--preserve-entities]
```

| Argument | Description |
|---|---|
| `java_world_folder_or_zip` | Path to the Java Edition world folder or a `.zip` archive containing it |
| `output_dir` | Optional. Directory to write `saveData.ms` into. Defaults to a folder named after the world in the current directory. |
| `--world-type <...>` | Unified world profile selector (recommended): `classic`, `small`, `medium`, `large`, `flat`, `flat-small`, `flat-medium`, `flat-large`. |
| `--small-world` | Use 64-chunk (1024 block) world size. |
| `--medium-world` | Use 192-chunk (3072 block) world size. |
| `--large-world` | Use 320-chunk (5120 block) world size. |
| `--flat-world` | Force output world type to flat (`generatorName=flat` in `level.dat`). |
| `--all-dimensions` | Also convert Nether and End. By default only Overworld is converted. |
| `--copy-players` | Import Java `players/*.dat` files when they use numeric filenames. |
| `--preserve-entities` | Keep chunk entities and tile data instead of stripping them for compatibility. |

Note: `--small-world`, `--medium-world`, `--large-world`, and `--flat-world` are kept as legacy aliases. Do not mix them with `--world-type`.

Inspect an existing `saveData.ms` container:

```
dotnet run -- --inspect <path_to_saveData.ms>
```

Inspect with the published executable:

```powershell
.\LceWorldConverter.exe --inspect <path_to_saveData.ms>
```

**Examples:**

Convert Overworld only (default) — output goes to `./MyWorld/saveData.ms`:
```
dotnet run -- "C:/Users/You/AppData/Roaming/.minecraft/saves/MyWorld"
```

Convert into a specific folder (e.g. an LCE save slot):
```
dotnet run -- "C:/Users/You/AppData/Roaming/.minecraft/saves/MyWorld" "D:/GameHDD/MySlot"
```

Convert from a zipped world export:
```
dotnet run -- "C:/Users/You/Desktop/MyWorld.zip" "D:/GameHDD/MySlot"
```

Convert Overworld + Nether + End:
```
dotnet run -- "C:/Users/You/AppData/Roaming/.minecraft/saves/MyWorld" --all-dimensions
```

Convert as a large world and include all dimensions:
```
dotnet run -- "C:/Users/You/AppData/Roaming/.minecraft/saves/MyWorld" "D:/GameHDD/MySlot" --world-type large --all-dimensions
```

Convert as a medium flat world:
```
dotnet run -- "C:/Users/You/AppData/Roaming/.minecraft/saves/MyWorld" "D:/GameHDD/MySlot" --world-type flat-medium
```

Convert with the published Windows executable:
```powershell
.\LceWorldConverter.exe "C:\Users\You\AppData\Roaming\.minecraft\saves\MyWorld" "D:\GameHDD\MySlot" --world-type large --all-dimensions --copy-players
```

Example PowerShell script:
```powershell
$converter = "C:\Tools\LCE-Save-Converter\LceWorldConverter.exe"
$javaWorld = "C:\Users\You\AppData\Roaming\.minecraft\saves\MyWorld"
$outputDir = "D:\GameHDD\MySlot"

& $converter $javaWorld $outputDir --world-type large --all-dimensions --copy-players

if ($LASTEXITCODE -ne 0) {
	throw "Conversion failed."
}
```

Then copy (or drop) the output `saveData.ms` into your LCE server's `GameHDD/<worldname>/` folder.

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
- Not every post-1.6.4 Java block has an exact TU19 equivalent; some modern blocks are downgraded to the closest visual/behavioral fallback
- Some complex modern blockstates can still require additional mapping passes for perfect parity in edge-case builds
- LCE world height is 128 blocks; source blocks above Y=127 are dropped or remapped during conversion
