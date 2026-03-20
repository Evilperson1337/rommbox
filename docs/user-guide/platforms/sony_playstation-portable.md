# Sony PlayStation Portable

The PlayStation Portable installer supports direct portable image deployment and can validate two emulator modes: standalone PPSSPP and RetroArch with the PPSSPP core.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation Portable |
| Plugin platform key | `psp` |
| Supported aliases | `psp`, `playstation portable`, `sony playstation portable`, `sony psp`, `playstationportable` |
| Default emulator target | Standalone PPSSPP |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported at capability level |

## Emulator Target

The PSP installer can operate in two modes.

### Configuration

| Setting | Purpose |
| --- | --- |
| `PspEmulatorMode` | `StandalonePPSSPP` or `RetroArchPPSSPP` |
| `PpssppExecutablePath` | Optional standalone PPSSPP executable override |
| `RetroArchExecutablePath` | Optional RetroArch executable override |
| `RetroArchPpssppCorePath` | Optional PPSSPP libretro core path or identifier |
| `ValidateRetroArchPpssppAssets` | Validates RetroArch system assets when RetroArch mode is selected |
| `FailInstallIfEmulatorNotReady` | Fails installation if the selected emulator mode is not ready |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported installed artifact |
| `.cso` | Yes | Supported compressed image |
| `.chd` | Yes | Supported compressed image |

## Deployment Overview

1. The PSP inspector resolves a canonical launch artifact.
2. The installer validates the configured emulator mode.
3. The selected game file is moved into a per-game install directory.
4. The installed file becomes the LaunchBox application path.

## RomM Asset Overview

No PSP-specific RomM asset mapping overrides are defined in the installer code. Standard metadata and asset behavior applies.

## Special Considerations

- Installation can optionally fail if the selected emulator mode is not ready.
- The plugin supports both standalone and RetroArch-based PPSSPP workflows.
- Update and DLC capability flags are enabled, but the visible installer flow remains focused on the primary game artifact.

