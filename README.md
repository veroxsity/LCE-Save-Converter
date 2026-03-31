# LCE Save Converter

<p align="center">
  <img src="https://img.shields.io/github/license/veroxsity/LCE-Save-Converter?style=for-the-badge" alt="License" />
  <img src="https://img.shields.io/github/last-commit/veroxsity/LCE-Save-Converter?style=for-the-badge" alt="Last Commit" />
  <img src="https://img.shields.io/github/repo-size/veroxsity/LCE-Save-Converter?style=for-the-badge" alt="Repo Size" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/github/v/release/veroxsity/LCE-Save-Converter?style=flat-square&label=Release" alt="Release" />
  <img src="https://img.shields.io/github/downloads/veroxsity/LCE-Save-Converter/total?style=flat-square&label=Downloads" alt="Downloads" />
</p>

Convert Java Edition worlds into Minecraft Legacy Console Edition (LCE) `saveData.ms`, and convert LCE saves back to Java world folders reversably.

## Workspace Role

- Use `saveconverter` to move Java Edition worlds onto an `LCEServer` instance or into a client save slot
- Use it in reverse to recover LCE saves back to Java Edition world folders
- Pair it with `LCEServer` and `LCEClient`/`LCEDebug` for end-to-end world testing

## Quick Start (Prebuilt)

1. Download the latest release from [GitHub Releases](https://github.com/veroxsity/LCE-Save-Converter/releases).
2. Extract the zip.
3. Use the GUI app for the easiest workflow, or use the CLI executable.

### GUI (Recommended)

1. Zip your Java world folder.
2. Open `LceWorldConverter.Gui.exe`.
3. Click `Explore...` and select your world zip.
4. Select an output folder.
5. Click `Convert`.

The converter extracts your zip to a temp folder and writes `saveData.ms` to the output folder you chose.

### CLI (Prebuilt EXE)

```powershell
.\LceWorldConverter.exe --from java <java_world_folder_or_zip> <output_dir> [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions] [--copy-players] [--preserve-entities]
.\LceWorldConverter.exe --from lce <saveData.ms_path> <java_world_output_dir> [--all-dimensions] [--copy-players]
```

Common examples:

```powershell
# Folder input
.\LceWorldConverter.exe --from java "C:\Users\You\AppData\Roaming\.minecraft\saves\MyWorld" "D:\GameHDD\MySlot" --world-type large --all-dimensions

# Zip input
.\LceWorldConverter.exe --from java "C:\Users\You\Desktop\MyWorld.zip" "D:\GameHDD\MySlot"

# LCE to Java
.\LceWorldConverter.exe --from lce "D:\GameHDD\MySlot\saveData.ms" "C:\Users\You\Desktop\RecoveredJavaWorld" --all-dimensions --copy-players
```

## What You Get

- `saveData.ms` for Windows64 LCE/TU19-compatible targets.
- Optional `unknown-modern-blocks.txt` in output when modern blocks are mapped to air.

Drop `saveData.ms` into your server world folder (for example `GameHDD/<worldname>/`) and set `level-name` in `server.properties`.

## Supported Inputs

| Java Version | Region Format | Notes |
|---|---|---|
| 1.6.4 | McRegion `.mcr` | Near 1:1 conversion |
| 1.7 - 1.12.2 | Anvil `.mca` | Minor remapping / flattening |
| 1.13 - 1.17 | Anvil `.mca` | Palette chunk remapping path |
| 1.18+ | Anvil `.mca` | Extended-height remapping path |
| Upgraded / mixed worlds | `.mca` | Chunk format is detected per chunk, not assumed per world |

## Flags

| Argument | Description |
|---|---|
| `--from java|lce` | Conversion direction |
| `java_world_folder_or_zip` | Java->LCE input path |
| `saveData.ms_path` | LCE->Java input path |
| `output_dir` | Java->LCE output directory for `saveData.ms` |
| `java_world_output_dir` | LCE->Java output world directory |
| `--world-type <...>` | Java->LCE only: `classic`, `small`, `medium`, `large`, `flat`, `flat-small`, `flat-medium`, `flat-large` |
| `--all-dimensions` | Convert Nether and End too |
| `--copy-players` | Java->LCE imports numeric player data; LCE->Java exports `players/*.dat` |
| `--preserve-entities` | Java->LCE only: keep entities/tile data (less compatibility-safe) |

Legacy Java->LCE positional mode still exists: `.\LceWorldConverter.exe <java_world_folder_or_zip> [output_dir] [flags...]`.

## Inspect Existing saveData.ms

```powershell
.\LceWorldConverter.exe --inspect <path_to_saveData.ms>
```

## Notes and Limitations

- LCE height is 128; source blocks above Y=127 are dropped/remapped.
- Some modern blocks have no exact TU19 equivalent.
- Nether portal linkage should be re-established in-game after conversion.

## Technical Docs

Full format notes, source references, and conversion internals are in [CONVERTER_DOCS.md](CONVERTER_DOCS.md).

## Related Repositories

- Hub repo: https://github.com/veroxsity/MinecraftLCE
- Bridge repo: https://github.com/veroxsity/LCEBridge
- Client repo: https://github.com/veroxsity/LCEClient
- Debug client repo: https://github.com/veroxsity/LCEDebug
- Launcher repo: https://github.com/veroxsity/LCELauncher
- Server repo: https://github.com/veroxsity/LCEServer

## Build From Source (Optional)

If you only want to use releases, you can ignore this section.

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/veroxsity/LCE-Save-Converter.git
cd LCE-Save-Converter
dotnet build
```

Build GUI project:

```bash
dotnet build ./LceWorldConverter.Gui/LceWorldConverter.Gui.csproj
```

Run GUI from source:

```powershell
dotnet run --project .\LceWorldConverter.Gui\LceWorldConverter.Gui.csproj
```

Run CLI from source:

```powershell
dotnet run --project .\LceWorldConverter.csproj -- --from java <java_world_folder_or_zip> <output_dir>
```
