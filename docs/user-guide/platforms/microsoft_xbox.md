# Microsoft Xbox

The Microsoft Xbox installer supports portable deployment of original Xbox disc images for xemu-style workflows. The implementation is focused on direct game artifacts rather than installer-based deployment.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Microsoft Xbox |
| Plugin platform key | `xbox` |
| Supported aliases | Xbox platform aliases defined in code, including `xbox` |
| Default emulator target | xemu |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The installer logs `xemu` as the configured emulator after a successful install.

No platform-specific executable override field is exposed in the Xbox installer, so emulator selection is expected to come from LaunchBox emulator mapping or existing ROM settings.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported direct launch artifact |
| `.xiso` | Yes | Supported direct launch artifact |

### Notes

- Detection, verification, and installation all validate the final artifact by extension.
- DLC, title updates, RAP/license files, and installer workflows are not implemented for original Xbox.

## Deployment Overview

1. The plugin inspects the staged content.
2. It resolves a supported Xbox game artifact.
3. The selected file is moved into the configured install location.
4. The installed artifact becomes the LaunchBox application path.

### Typical install result

- Installed content remains portable.
- Launch arguments are built from the configured ROM settings.
- Uninstall removes the installed game file only and avoids deleting directories.

## RomM Asset Overview

No Xbox-specific RomM asset overrides are defined in the installer code. Standard plugin metadata and asset handling applies.

## Special Considerations

- The Xbox implementation is file-based rather than package-based.
- The installer assumes your emulator configuration is already handled elsewhere in LaunchBox.

