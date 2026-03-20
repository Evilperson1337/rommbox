# Nintendo Wii U

The Nintendo Wii U installer supports both direct Wii U image files and extracted `code/content/meta` layouts for Cemu-based workflows. It also detects update and DLC content during inspection, but does not automatically install that content yet.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo Wii U |
| Plugin platform key | `wiiu` |
| Supported aliases | Wii U aliases defined in code, including `wiiu` and `wii-u` |
| Default emulator target | Cemu |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Detected, but not installed automatically |

## Emulator Target

The installer logs `Cemu` as the configured emulator.

### Configuration

| Setting | Purpose |
| --- | --- |
| `CemuExecutablePath` | Optional Cemu executable override; otherwise LaunchBox mapping is used |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.wud` | Yes | Supported direct image |
| `.wux` | Yes | Supported compressed image |
| `.wua` | Yes | Supported packaged install format |
| `.rpx` | Yes | Supported executable when used as the launch artifact |
| `code/content/meta` layout | Yes | Extracted layout is resolved to a launch `.rpx` inside the `code` folder |

### Notes

- Archives such as `.zip`, `.7z`, and `.rar` are recognized during inspection and require extraction before final artifact selection.
- If only update or DLC content is detected, installation is rejected because a base game artifact is required.

## Deployment Overview

1. The plugin inspects staged content for direct images or extracted layouts.
2. It selects a base game candidate.
3. Extracted layouts are resolved to a launch `.rpx` file inside the `code` folder.
4. The selected artifact or layout is installed into a per-game directory.
5. The resolved launch path becomes the LaunchBox application path.

## RomM Asset Overview

No Wii U-specific asset mapping rules are defined in the installer. Standard plugin metadata and asset behavior applies.

## Special Considerations

- Update and DLC content is detected and logged, but automatic Cemu update/DLC installation is not implemented.
- Multiple base candidates may produce an ambiguity warning, with the first prioritized candidate chosen automatically.
- Extracted layout installs rely on a valid `code/content/meta` directory structure.

