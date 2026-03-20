# Super Nintendo Entertainment System

The SNES installer uses the shared ROM-base workflow and targets RetroArch with the Snes9x core. It is intended for direct ROM deployment rather than complex multi-file install pipelines.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Super Nintendo Entertainment System |
| Plugin platform key | `snes` |
| Supported aliases | `snes`, `super nintendo`, `super nintendo entertainment system`, `supernintendo` |
| Default emulator target | RetroArch (`snes9x`) |
| Installation model | Portable ROM deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The installer profile defines RetroArch with the `snes9x` core and uses the ROM path as the launch argument.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.zip` | Yes | Preserved archive format |
| `.sfc` | Yes | Supported ROM format |
| `.smc` | Yes | Supported ROM format |

### Notes

- The shared ROM-base profile preserves archives for this platform.
- Games are installed into per-game subdirectories.

## Deployment Overview

1. The shared ROM installer selects a supported ROM artifact.
2. The chosen file is moved into the configured install location.
3. The installed ROM becomes the LaunchBox application path.
4. The plugin logs RetroArch (`snes9x`) as the configured emulator.

## RomM Asset Overview

No SNES-specific asset behavior is implemented in the installer code. Standard plugin metadata and RomM assets apply.

## Special Considerations

- This platform uses the generic ROM-base installer flow.
- BIOS handling, special chip compatibility, and emulator-side options are not defined by the installer itself.

