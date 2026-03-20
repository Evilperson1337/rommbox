# Sony PlayStation Vita

The PlayStation Vita installer is designed for Vita3K and uses a title-scoped launch token as the installed application path. Base game deployment can work from `.vpk` packages or extracted Vita layouts, and optional update and DLC import can be enabled through platform settings.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation Vita |
| Plugin platform key | `psvita` |
| Supported aliases | `psvita`, `ps vita`, `vita`, `playstation vita`, `sony playstation vita` |
| Default emulator target | Vita3K |
| Installation model | Token-based deployment for Vita3K |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported |

## Emulator Target

The Vita installer is built around Vita3K.

### Configuration

| Setting | Purpose |
| --- | --- |
| `Vita3kExecutablePath` | Optional Vita3K executable override |
| `VitaInstallUpdatesAutomatically` | Automatically imports detected update packages |
| `VitaInstallDlcAutomatically` | Automatically imports detected DLC packages |
| `VitaFailIfEmulatorNotReady` | Fails installation if Vita3K is not ready |
| `VitaConsolidateGameInstalls` | Consolidates content into a physical game-storage location and exposes it to Vita3K through a title-scoped link |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.vpk` | Yes | Supported Vita package input |
| Extracted layout with `PARAM.SFO` | Yes | Supported staged content source |
| `.vita3k.json` | Installed output | Used as the final LaunchBox application path |

### Notes

- Detection and verification expect the installed application path to be a Vita launch token ending in `.vita3k.json`.
- The inspector can classify base game, update, and DLC Vita package candidates.
- Update and DLC imports are optional and controlled by platform settings.

## Deployment Overview

1. The Vita inspector resolves a base game candidate from `.vpk` content or an extracted layout.
2. The installer prepares the Vita3K environment and title resolution metadata.
3. Base content is installed into the Vita game location.
4. Optional updates and DLC can be imported if those settings are enabled.
5. The plugin writes a title-scoped `.vita3k.json` token and uses that token as the application path.

## RomM Asset Overview

No Vita-specific RomM asset mapping overrides are defined in the installer code. Standard metadata and asset behavior applies.

## Special Considerations

- The installed path is not the raw `.vpk`; it is a generated Vita3K launch token.
- Consolidated installs store a physical install path and expose the content to Vita3K through a linked title path.
- If update or DLC packages are detected but automatic import is disabled, installation continues and logs a warning instead of failing.

