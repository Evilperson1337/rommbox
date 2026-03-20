# Nintendo 64

The Nintendo 64 installer uses the shared ROM-base workflow for direct ROM deployment. It is built around RetroArch with the Mupen64Plus-Next core.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo 64 |
| Plugin platform key | `n64` |
| Supported aliases | `n64`, `nintendo 64`, `nintendo64` |
| Default emulator target | RetroArch (`mupen64plus-next`) |
| Installation model | Portable ROM deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The installer profile defines RetroArch with the `mupen64plus-next` core and uses `{rom}` as the launch argument template.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.z64` | Yes | Supported ROM format |
| `.n64` | Yes | Supported ROM format |
| `.v64` | Yes | Supported ROM format |
| `.zip` | Yes | Preserved archive format |

### Notes

- The ROM-base profile preserves archives rather than forcing extraction.
- Games are installed into per-game subdirectories.

## Deployment Overview

1. The shared ROM installer finds the best matching ROM artifact.
2. It stages and moves the selected ROM into the install directory.
3. The ROM file becomes the LaunchBox application path.
4. Launch arguments are built from the ROM path.

## RomM Asset Overview

No Nintendo 64-specific RomM asset handling is implemented in this installer. Standard metadata and asset behavior applies.

## Special Considerations

- This platform uses the generic ROM-base installer flow rather than a custom package installer.
- Archive handling is preservation-based, so compressed ROMs can remain compressed if selected as the launch artifact.

