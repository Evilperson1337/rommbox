# Nintendo GameCube

The Nintendo GameCube installer supports direct GameCube disc image deployment for Dolphin-based workflows. It uses staging inspection so the plugin can resolve the correct launch artifact before install.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo GameCube |
| Plugin platform key | `gamecube` |
| Supported aliases | `gamecube`, `nintendo gamecube`, `nintendogamecube` |
| Default emulator target | Dolphin |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The installer logs Dolphin as the configured emulator.

### Configuration

| Setting | Purpose |
| --- | --- |
| `DolphinExecutablePath` | Optional Dolphin executable override; otherwise LaunchBox mapping is used |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported base artifact |
| `.gcz` | Yes | Supported compressed Dolphin disc format |
| `.ciso` | Yes | Supported compact disc image |
| `.wia` | Yes | Supported disc format |
| `.rvz` | Yes | Supported Dolphin RVZ format |

## Deployment Overview

1. The plugin inspects the staged content with the GameCube inspector.
2. It validates the resolved launch artifact.
3. The selected file is moved into a per-game install directory.
4. The installed file becomes the LaunchBox application path.

## RomM Asset Overview

No GameCube-specific asset overrides are defined in the installer code. Standard RomM metadata and asset handling applies.

## Special Considerations

- The installer is portable-file based and does not implement DLC or update workflows.
- Uninstall removes the installed artifact only and leaves directories in place.
- Emulator-specific launch arguments come from the configured ROM settings.

