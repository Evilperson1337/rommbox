# Sony PlayStation

The original PlayStation installer supports both single-file and multi-file disc layouts. It is designed around portable deployment and can generate an `.m3u` playlist automatically when multi-disc content is installed.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation |
| Plugin platform key | `ps1` |
| Supported aliases | `playstation`, `sonyplaystation`, `psx`, `ps1` |
| Default emulator target | Determined by LaunchBox mapping or existing ROM settings |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The PS1 installer does not expose a platform-specific emulator override field. It installs the launch artifact and relies on the emulator configuration already associated with the platform in LaunchBox or the ROM settings.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.chd` | Yes | Supported single-file format |
| `.iso` | Yes | Supported single-file format |
| `.pbp` | Yes | Supported single-file format |
| `.cue` | Yes | Supported multi-file disc layout |
| `.ccd` | Yes | Supported CloneCD layout; requires companion `.img` |
| `.m3u` | Yes | Supported installed launch playlist for multi-disc content |

### Notes

- `.cue` installs require referenced disc companion files to exist.
- `.ccd` installs require a matching `.img` file.
- If multi-disc content is installed from discrete disc files, the installer can generate an `.m3u` playlist and use that playlist as the final launch target.
- Raw archives such as `.zip`, `.7z`, and `.rar` are not accepted as the final source unless they are extracted first.

## Deployment Overview

1. The plugin resolves the source content from extracted files or direct input.
2. The PS1 inspector identifies the canonical launch artifact.
3. Single-file formats are moved directly into the game folder.
4. Multi-file layouts are copied into a per-game directory.
5. If the content is multi-disc, the plugin generates an `.m3u` playlist and uses it as the application path.

## RomM Asset Overview

No PS1-specific asset mapping overrides are implemented in the installer code. Standard RomM metadata and asset behavior applies.

## Special Considerations

- Multi-disc installs can change the final launch artifact from a disc image to a generated playlist.
- Detection will fail if `.cue`, `.ccd`, or `.m3u` companion files are missing.
- The installer is intentionally file-layout aware, even though it remains a portable deployment workflow.

