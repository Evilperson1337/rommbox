# Nintendo Switch

The Nintendo Switch installer supports portable deployment of base game packages for Eden-based workflows. It also detects update and DLC packages during inspection, but automated optional-content installation is not implemented yet.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Nintendo Switch |
| Plugin platform key | `switch` |
| Supported aliases | `nintendoswitch`, `switch`, `nintendo switch` |
| Default emulator target | Eden |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Detected, but not installed automatically |

## Emulator Target

The installer logs `Eden` as the configured emulator.

### Configuration

| Setting | Purpose |
| --- | --- |
| `SwitchEdenExecutablePath` | Optional Eden executable override |

If this field is not supplied, the plugin uses the LaunchBox emulator mapping.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.nsp` | Yes | Supported installed artifact |
| `.xci` | Yes | Supported installed artifact |
| `.nsz` | Indirectly | Detected as compressed content and normalized to `.nsp` |
| `.xcz` | Indirectly | Detected as compressed content and normalized to `.xci` |

### Notes

- The final installed artifact must be `.nsp` or `.xci`.
- If the selected package is `.nsz` or `.xcz`, the installer performs a simulated decompression step and writes a normalized `.nsp` or `.xci` result.
- Update and DLC packages are detected and logged, but automatic installation is not currently implemented.

## Deployment Overview

1. The plugin inspects the staged content.
2. It selects a base game package as the canonical launch artifact.
3. If the package is compressed, it is normalized to `.nsp` or `.xci`.
4. The resulting package is moved into the game install directory.
5. The package becomes the LaunchBox application path.

## RomM Asset Overview

No Switch-specific RomM asset mapping is implemented in the installer. Standard plugin metadata and asset behavior applies.

## Special Considerations

- A base NSP/XCI package is required; update-only or DLC-only content is rejected as the primary install source.
- The installer logs detected update and DLC packages for future integration.
- Emulator-side key management and firmware requirements are not defined by the installer code.

