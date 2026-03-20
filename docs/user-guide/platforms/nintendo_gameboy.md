# Nintendo Game Boy

This platform currently uses the plugin's general fallback installer rather than a dedicated platform-specific installer. Installation behavior is configuration-driven and depends on the platform mapping's supported file types, launch priority, and install layout settings.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo Game Boy |
| Plugin platform key | `general` fallback |
| Installer type | General fallback installer |
| Installation model | Portable ROM/artifact deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported by the general installer |
| Emulator target | Determined by LaunchBox emulator mapping for the platform |

## Emulator Target

The general fallback installer does not define a platform-specific emulator. It installs the selected artifact and relies on the emulator already mapped to this platform in LaunchBox.

## Supported File Formats

This platform uses the configurable general installer defaults unless your platform mapping overrides them.

| Format | Default support | Notes |
| --- | --- | --- |
| `.zip` | Yes | Supported by default |
| `.rom` | Yes | Supported by default |
| `.bin` | Yes | Supported by default |
| `.iso` | Yes | Supported by default |
| `.cue` | Yes | Supported by default |
| `.chd` | Yes | Supported by default |
| `.sfc` | Yes | Supported by default |
| `.smc` | Yes | Supported by default |
| `.z64` | Yes | Supported by default |
| `.n64` | Yes | Supported by default |
| `.v64` | Yes | Supported by default |

## Deployment Overview

1. The general installer resolves the install root.
2. It loads the configured fallback-installer options.
3. It resolves a source artifact from the download or extracted staging content.
4. It validates the artifact against `SupportedFileTypes`.
5. It installs the selected file into the configured layout.
6. The installed file becomes the LaunchBox application path.

## RomM Asset Overview

No Nintendo Game Boy-specific RomM asset overrides are implemented by the fallback installer. Standard plugin metadata and asset behavior applies.

## Special Considerations

- This document reflects the generic fallback workflow from the general installer.
- Supported extensions and layout behavior can differ if this platform's mapping overrides the defaults.
- If a dedicated Nintendo Game Boy installer is added later, this document should be replaced with platform-specific documentation.
