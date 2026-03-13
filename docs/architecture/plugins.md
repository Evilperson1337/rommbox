# RomMbox Platform Plugins

## Overview

RomMbox loads platform installers dynamically from plugin assemblies in `system/platforms` at runtime. The loader reflects all types implementing `IPlatformInstaller`, instantiates them, and registers each by `PlatformKey`.

### Plugin registration

- `PlatformInstallerLoader` scans `*.dll` in plugin runtime folder and registers installers by key.
- `PlatformInstallerRegistry` resolves installers by key and exposes fallback behavior.
- If no exact installer key is found, registry attempts fallback to `general` (`GeneralPlatformInstaller`).

### Platform detection and installer resolution

Install pipeline resolves a platform key from RomM/LaunchBox metadata, then:

1. Resolves installer key
2. Attempts dedicated plugin
3. Falls back to `general` when available (and mapping allows fallback)

Pipeline logging explicitly reports plugin match, key remapping, and fallback selection.

### Installer pipeline

Install pipeline phases are:

Pending  
→ ResolvingMetadata  
→ ResolvingDestination  
→ Downloading  
→ Extracting  
→ Installing  
→ PostProcessing  
→ Completed / Failed / Cancelled

Operational flow:

Download  
→ Optional extraction to staging  
→ Platform-specific inspection  
→ Platform install implementation  
→ Application path + launch args assignment  
→ Install-state persistence

### Staging vs installation

- Staging paths (`.staging/...`) are used for extraction/inspection and (for some flows) atomic commit into final install location.
- Final install path is what is persisted as installed path/install root.
- Windows path includes dedicated staging commit/swap behavior.

### Install-state persistence

Install state is persisted through `InstallStateService` (SQLite-backed), including:

- installed path
- install root
- install phase/status
- launch path/args
- operation and validation timestamps

---

# Plugin List

## Windows
Plugin Key: `windows`  
Emulator: Native execution (no emulator required)  
Supported Platform Aliases: Unknown / Not Determined From Source (identity metadata not implemented in this installer)  
Supported Formats:
- Archive/content-driven Windows installs (format classification handled by Windows install subsystem)
- Direct file install: Not primary path (`SupportsDirectFiles = false`)

Install Strategy:
- Installer-oriented workflow through `WindowsInstallSubsystem`
- Supports silent/manual install modes
- Supports optional update/DLC root handling in Windows content structure

Launch Strategy:
- Executable discovery (`ExecutableResolver`) in install root
- Launch path resolved to detected executable

Update/DLC Support:
- Supported
- Windows subsystem installs update and DLC content after/with base content depending on mode

Notes:
- Uses install classifier and installer execution orchestration.

---

## Arcade
Plugin Key: `arcade`  
Emulator: MAME or RetroArch + FinalBurn Neo (mode inferred from emulator settings)  
Supported Platform Aliases:
- arcade
- arcadegames
- mame
- finalburnneo / final burn neo / fbneo / fba

Supported Formats:
- `.zip` ROM set archives

Install Strategy:
- Direct archive install into target directory
- Inspector resolves ROM set candidate and launch artifact
- Archive-preserve behavior in pipeline (pre-install extraction skipped for arcade)

Launch Strategy:
- Launch installed ROM set archive
- Default launch arguments differ by emulator mode (`{romset}` for MAME-style)

Update/DLC Support:
- Not supported

Notes:
- Plugin is archive-centric and expects zipped arcade set workflows.

---

## Nintendo 64
Plugin Key: `n64`  
Emulator: RetroArch (`mupen64plus-next` core)  
Supported Platform Aliases:
- n64
- nintendo 64
- nintendo64

Supported Formats:
- `.z64`
- `.n64`
- `.v64`
- `.zip`

Install Strategy:
- ROM-base installer (`RomPlatformInstallerBase`)
- Direct file move/copy into per-game folder
- Archive policy: Preserve

Launch Strategy:
- Launch ROM path with emulator launch template (`{rom}`)

Update/DLC Support:
- Not supported

Notes:
- Explicitly logged as RetroArch Mupen64Plus-Next target.

---

## Super Nintendo Entertainment System
Plugin Key: `snes`  
Emulator: RetroArch (`snes9x` core)  
Supported Platform Aliases:
- snes
- super nintendo
- super nintendo entertainment system
- supernintendo

Supported Formats:
- `.zip`
- `.sfc`
- `.smc`

Install Strategy:
- ROM-base installer (`RomPlatformInstallerBase`)
- Direct file install
- Archive policy: Preserve

Launch Strategy:
- Launch ROM path with emulator launch template (`{rom}`)

Update/DLC Support:
- Not supported

Notes:
- Explicitly logged as RetroArch snes9x target.

---

## PlayStation
Plugin Key: `ps1`  
Emulator: Unknown / Not Determined From Source  
Supported Platform Aliases:
- playstation
- sonyplaystation
- psx
- ps1

Supported Formats:
- `.chd`
- `.iso`
- `.pbp`
- `.cue`
- `.ccd`
- `.m3u`

Install Strategy:
- Staging inspection resolves canonical launch artifact
- Handles multi-disc structures and companion file requirements (CUE/CCD/M3U handling)
- Installs file/folder layout based on detected content format

Launch Strategy:
- Launch canonical artifact selected by inspector
- For playlist/disc layouts, launch path resolves to selected playlist/disc descriptor

Update/DLC Support:
- Not supported

Notes:
- Multi-disc validation is built into detection/install logic.

---

## PlayStation 2
Plugin Key: `ps2`  
Emulator: PCSX2  
Supported Platform Aliases:
- ps2
- playstation2 / playstation 2
- sonyplaystation2 / sony playstation 2

Supported Formats:
- `.iso`
- `.chd`
- `.cso`
- `.zso`
- `.bin`

Install Strategy:
- Inspector-led direct artifact selection
- File-based install into resolved PS2 game directory

Launch Strategy:
- Launch installed artifact path
- Default/template launch args support (`{rom}` replacement path behavior)

Update/DLC Support:
- Not supported

Notes:
- Emulator path can be overridden with `Pcsx2ExecutablePath`.

---

## PlayStation 3
Plugin Key: `ps3`  
Emulator: RPCS3  
Supported Platform Aliases: Unknown / Not Determined From Source (identity metadata not implemented in this installer)  
Supported Formats:
- JB folder layout (`PS3_GAME`, `USRDIR/EBOOT.BIN`)
- Decrypted `.iso`
- Optional package content: `.pkg` (update/DLC)
- Optional license content: `.rap`

Install Strategy:
- Inspector detects JB folder vs decrypted ISO
- JB folder installed as extracted layout; ISO installed as file artifact
- Optional content pass handles update/DLC packages and RAP licenses through RPCS3 integration

Launch Strategy:
- JB: launch resolved `EBOOT.BIN`
- ISO: launch ISO path

Update/DLC Support:
- Supported
- Detects update packages, DLC packages, and RAP files
- Update packages are version-ordered during installation flow
- Installer supports region/package matching controls via settings

Notes:
- Includes PARAM.SFO parsing (`TitleId`, title, version, region inference)
- Includes RPCS3 executable/license directory resolution and RAP install behavior.

---

## PlayStation 4
Plugin Key: `ps4`  
Emulator: ShadPS4  
Supported Platform Aliases:
- ps4
- playstation4 / playstation 4
- sony playstation 4 / sonyplaystation4

Supported Formats:
- PS4 package/content formats (inspector-driven; includes base/update/DLC roles)
- Direct `.pkg` path support with optional external extractor

Install Strategy:
- Inspector classifies base/update/DLC items
- Serial/title-based folder install layout
- Optional direct PKG extraction path via configured external extractor

Launch Strategy:
- Launch path resolves to installed content directory/base path

Update/DLC Support:
- Supported
- Installs update and DLC content after base content (ordered loops in install implementation)

Notes:
- Config includes `Ps4ExternalPkgExtractorPath` and fail-fast toggle for direct-PKG extractor requirement.

---

## PlayStation Portable
Plugin Key: `psp`  
Emulator: PPSSPP (standalone) or RetroArch PPSSPP mode  
Supported Platform Aliases:
- psp
- playstation portable
- sony playstation portable
- sony psp
- playstationportable

Supported Formats:
- `.iso`
- `.cso`
- `.chd`

Install Strategy:
- Inspector-based direct file selection
- Installs selected artifact into PSP game directory
- Emulator readiness validation can be enforced by settings

Launch Strategy:
- Launch installed artifact path
- Launch args depend on emulator mode (standalone vs RetroArch)

Update/DLC Support:
- Not supported

Notes:
- Configurable emulator mode and RetroArch asset validation.

---

## PlayStation Vita
Plugin Key: `psvita`  
Emulator: Vita3K  
Supported Platform Aliases:
- psvita
- ps vita
- vita
- playstation vita
- sony playstation vita

Supported Formats:
- Inspector-classified Vita content packages (base/update/DLC)
- Launch token format persisted as `*.vita3k.json`

Install Strategy:
- Emulator-managed import workflow
- Base package import is primary path
- Optional auto-import of update/DLC controlled by settings
- Writes Vita launch token containing title metadata/import history

Launch Strategy:
- Launch via title ID through emulator arguments
- Stored application path is Vita3K token file, not raw package

Update/DLC Support:
- Supported (conditional)
- Installed only when `VitaInstallUpdatesAutomatically` / `VitaInstallDlcAutomatically` enabled

Notes:
- Includes emulator readiness gate (`VitaFailIfEmulatorNotReady`).

---

## Nintendo Switch
Plugin Key: `switch`  
Emulator: Eden  
Supported Platform Aliases:
- nintendoswitch
- switch
- nintendo switch

Supported Formats:
- `.nsp`
- `.xci`

Install Strategy:
- Inspector identifies base/update/DLC package roles
- Base game artifact is installed for launch path
- Update/DLC currently detected and logged for future installer integration

Launch Strategy:
- Launch installed base package artifact

Update/DLC Support:
- Detection: supported
- Automatic install: Not implemented (logged only)

Notes:
- Explicit warning is emitted when updates/DLC are present.

---

## Nintendo 3DS
Plugin Key: `3ds`  
Emulator: Azahar / AzaharPlus  
Supported Platform Aliases:
- nintendo3ds
- nintendo 3ds
- 3ds

Supported Formats:
- Direct install/launch:
  - `.3ds`
  - `.cci`
  - `.cxi`
- Inspector import-aware format:
  - `.cia` (detected as emulator-import style candidate)

Install Strategy:
- Inspector resolves candidate artifact and format
- Installer accepts direct-launch extensions for installed artifacts
- File-based install into Nintendo 3DS game directory

Launch Strategy:
- Launch installed artifact path
- Emulator name resolution favors AzaharPlus when configured

Update/DLC Support:
- Not supported

Notes:
- Source indicates import-aware inspection for `.cia`, but installer output path validation is direct-file focused.

---

## Nintendo Wii
Plugin Key: `wii`  
Emulator: Dolphin  
Supported Platform Aliases:
- wii
- nintendo wii
- nintendowii

Supported Formats:
- `.iso`
- `.wbfs`
- `.gcz`
- `.ciso`
- `.wia`
- `.rvz`

Install Strategy:
- Dolphin shared artifact inspector + Wii policy
- Installs selected launch artifact directly

Launch Strategy:
- Launch installed disc artifact via Dolphin args template

Update/DLC Support:
- Not supported

Notes:
- Uses shared Dolphin internal helpers/policy.

---

## Nintendo GameCube
Plugin Key: `gamecube`  
Emulator: Dolphin  
Supported Platform Aliases:
- gamecube
- nintendo gamecube
- nintendogamecube

Supported Formats:
- `.iso`
- `.gcz`
- `.ciso`
- `.wia`
- `.rvz`

Install Strategy:
- Dolphin shared artifact inspector + GameCube policy
- Installs selected launch artifact directly

Launch Strategy:
- Launch installed disc artifact via Dolphin args template

Update/DLC Support:
- Not supported

Notes:
- Uses shared Dolphin internal helpers/policy.

---

## Nintendo Wii U
Plugin Key: `wiiu`  
Emulator: Cemu  
Supported Platform Aliases:
- wiiu
- wii u
- nintendo wii u
- nintendowiiu

Supported Formats:
- `.wud`
- `.wux`
- `.wua`
- `.rpx`
- Extracted code/content/meta layouts (inspector-classified)

Install Strategy:
- Inspector classifies base/update/DLC candidates and launch RPX
- Base game installed to platform path
- Handles direct file or extracted layout install depending on candidate

Launch Strategy:
- Launch resolved `.rpx` or direct supported artifact

Update/DLC Support:
- Detection: supported
- Automatic install: Not implemented (detected and logged)

Notes:
- Inspector reports update/DLC candidate counts and warnings for unimplemented automation.

---

## Microsoft Xbox
Plugin Key: `xbox`  
Emulator: Xemu  
Supported Platform Aliases:
- xbox
- microsoftxbox / microsoft xbox
- original xbox
- xbox original

Supported Formats:
- `.iso`
- `.xiso`

Install Strategy:
- Inspector resolves launch artifact
- Direct artifact install into Xbox target folder

Launch Strategy:
- Launch installed image artifact with emulator arguments template

Update/DLC Support:
- Not supported

Notes:
- Installer logs configured emulator as xemu.

---

## Microsoft Xbox 360
Plugin Key: `xbox360`  
Emulator: Xenia  
Supported Platform Aliases:
- xbox 360
- xbox360
- microsoft xbox 360
- x360

Supported Formats:
- `.iso`
- `.xex`
- Extracted layout signal support (e.g., default.xex discovery in inspector)

Install Strategy:
- Inspector resolves canonical launch artifact from staged content
- Installs direct file artifact into Xbox 360 target folder

Launch Strategy:
- Launch installed artifact (`.iso` or `.xex`) with emulator args template

Update/DLC Support:
- Not supported

Notes:
- Installer logs configured emulator as xenia.

---

## General Fallback Plugin
Plugin Key: `general`  
Supported Formats:
- Configurable via `SupportedFileTypes`
- Default includes `.zip,.rom,.bin,.iso,.cue,.chd,.sfc,.smc,.z64,.n64,.v64,.m3u,.ccd,.pbp`

Install Strategy:
- Generic artifact selection + install based on configurable options
- Optional archive extraction behavior via:
  - `ArchiveHandlingMode` (`NeverExtract`, `ExtractForInspection`, `ExtractAlways`)
  - legacy `ExtractArchives`
- Supports direct archive install when allowed (`InstallFromArchiveDirectly`)
- Layout strategy configurable (`InstallLayoutMode`, `UseGameSubdirectory`)
- Candidate selection configurable (`ArtifactSelectionMode`, `PreferredLaunchExtensions`)

Launch Strategy:
- Application path is installed selected artifact
- Launch args from mapping/emulator template

Notes:
- This is the default fallback when no dedicated installer is matched.
- Per-platform mapping controls whether general fallback usage is allowed (`UseGeneralFallbackInstaller`).

