# Nintendo Wii

The Nintendo Wii installer supports direct Wii disc image deployment for Dolphin-based workflows. It can also detect title metadata during inspection and exposes Wii support for optional update and DLC handling at the capability level, although the installer remains focused on base artifact deployment.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo Wii |
| Plugin platform key | `wii` |
| Supported aliases | `wii`, `nintendo wii`, `nintendowii` |
| Default emulator target | Dolphin |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported at capability level |

## Emulator Target

The installer logs Dolphin as the configured emulator.

### Configuration

| Setting | Purpose |
| --- | --- |
| `DolphinExecutablePath` | Optional Dolphin executable override; otherwise LaunchBox mapping is used |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported disc image |
| `.wbfs` | Yes | Supported Wii backup format |
| `.gcz` | Yes | Supported Dolphin compressed format |
| `.ciso` | Yes | Supported compact disc image |
| `.wia` | Yes | Supported disc format |
| `.rvz` | Yes | Supported Dolphin RVZ format |

## Deployment Overview

1. The Wii inspector resolves the best launch artifact from staged content.
2. The plugin validates the install root and artifact extension.
3. The selected file is moved into a per-game install directory.
4. The installed file becomes the LaunchBox application path.

### Inspection behavior

When available, the installer logs detected Wii metadata such as title ID, region, and revision.

## RomM Asset Overview

No Wii-specific RomM asset handling is defined in the installer code. Standard plugin metadata and asset behavior applies.

## Special Considerations

- The implementation is built around Dolphin file deployment rather than package-style installation.
- Uninstall removes the installed artifact only and preserves platform folders.
- The installer exposes update and DLC capability flags, but the user-facing deployment flow remains centered on the main game artifact.

