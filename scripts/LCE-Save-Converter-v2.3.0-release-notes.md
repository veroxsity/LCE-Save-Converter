# LCE Save Converter v2.3.0

### Highlights

* Architecture Cleanup: Split the project into a shared core library, a dedicated CLI app, and the WPF GUI app while preserving the existing conversion workflow and release artifact names.
* Shared Conversion Requests: Replaced duplicated CLI and GUI option-building with one request model, one defaults layer, and one validation pipeline.
* GUI Refactor: Reworked the wizard to use a proper state/view-model layer with declarative XAML instead of assembling large parts of the UI in code-behind.

### Converter Improvements

* Conversion Orchestration: Split the old monolithic conversion flow into dedicated Java -> LCE and LCE -> Java orchestrators with focused services for world preparation, cleanup, player transfer, spawn estimation, and unknown-block reporting.
* Chunk Conversion Cleanup: Reduced global mutable state by introducing a per-run conversion context and moved mapping/resource loading into a reusable provider.
* CLI And Inspector Routing: Separated debug and inspection command handling from the normal conversion path so the shipping CLI is easier to maintain.
* Build And Release: Added a solution file, shared analyzer configuration, GitHub Actions CI, and fixed packaging so version metadata stays valid while the CLI still ships as `LceWorldConverter.exe`.

### Assets

* `LCE-Save-Converter-v2.3.0-win-x64.zip`
* `LCE-Save-Converter-v2.3.0-linux-x64.zip`
* `LCE-Save-Converter-v2.3.0-osx-x64.zip`
* `LCE-Save-Converter-v2.3.0-osx-arm64.zip`
* `LCE-Save-Converter-GUI-v2.3.0-win-x64.zip`
* `LCE-Save-Converter-v2.3.0-setup.exe`
* `LCE-Save-Converter-v2.3.0-sha256.txt`

### Full Changelog

* `v2.2.1...v2.3.0`
