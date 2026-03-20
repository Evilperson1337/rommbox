# Nintendo 3DS

The Nintendo 3DS installer supports portable deployment of standard handheld image formats and is designed around Azahar-family emulator workflows.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo 3DS |
| Plugin platform key | `3ds` |
| Supported aliases | `nintendo3ds`, `nintendo 3ds`, `3ds` |
| Default emulator target | Azahar |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The installer profile defines Azahar as the emulator target.

### Configuration

| Setting | Purpose |
| --- | --- |
| `AzaharExecutablePath` | Optional Azahar executable override |
| `AzaharPlusExecutablePath` | Optional AzaharPlus executable override |

If these fields are not supplied, the plugin falls back to the LaunchBox emulator mapping.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.3ds` | Yes | Supported installed artifact |
| `.cci` | Yes | Supported installed artifact |
| `.cxi` | Yes | Supported installed artifact |

### Inspection notes

- The installer profile also allows staged detection of `.cia` content during inspection.
- Installed artifact validation is limited to `.3ds`, `.cci`, and `.cxi`.

## Deployment Overview

1. The plugin inspects the staged content and resolves a supported 3DS artifact.
2. It creates a per-game install directory.
3. The resolved file is moved into that folder.
4. The installed file becomes the LaunchBox application path.

## RomM Asset Overview

No Nintendo 3DS-specific asset logic is defined in the installer code. Standard plugin metadata and RomM asset behavior applies.

## Special Considerations

- Extraction from archives is allowed during inspection.
- The installer treats the final result as a portable file deployment.
- DLC, update, and separate package-install workflows are not implemented in this installer.

