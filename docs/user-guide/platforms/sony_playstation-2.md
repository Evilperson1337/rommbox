# Sony PlayStation 2

The PlayStation 2 installer supports direct PS2 game image deployment for PCSX2-based workflows. It uses staging inspection to choose a canonical launch artifact before installing the game into a per-game directory.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation 2 |
| Plugin platform key | `ps2` |
| Supported aliases | `ps2`, `playstation2`, `playstation 2`, `sonyplaystation2`, `sony playstation 2` |
| Default emulator target | PCSX2 |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported at capability level |

## Emulator Target

The installer logs `PCSX2` as the configured emulator.

### Configuration

| Setting | Purpose |
| --- | --- |
| `Pcsx2ExecutablePath` | Optional PCSX2 executable override; otherwise LaunchBox mapping is used |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported disc image |
| `.chd` | Yes | Supported compressed disc image |
| `.cso` | Yes | Supported compressed image |
| `.zso` | Yes | Supported compressed image |
| `.bin` | Yes | Supported artifact if selected by inspection |

## Deployment Overview

1. The plugin inspects staged content with the PS2 inspector.
2. It validates the selected launch artifact.
3. The resolved file is moved into a per-game directory.
4. The installed file becomes the LaunchBox application path.

## RomM Asset Overview

No PS2-specific asset mapping rules are implemented in the installer code. Standard RomM metadata and asset behavior applies.

## Special Considerations

- The installer exposes DLC and update capability flags, but the visible install flow is still centered on the primary PS2 game artifact.
- Uninstall removes the installed file only and intentionally avoids deleting directories.

