# Sony PlayStation 4

The PlayStation 4 installer supports direct PS4 package workflows and extracted folder layouts for ShadPS4-based deployments. It also detects update, DLC, and bonus content and can normalize package-based content into folder-based layouts.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation 4 |
| Plugin platform key | `ps4` |
| Supported aliases | `ps4`, `sony-playstation-4`, plus additional PS4 aliases defined in code |
| Default emulator target | ShadPS4 |
| Installation model | Portable folder-based deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported |
| Bonus content | Supported |

## Emulator Target

The PS4 installer is built around ShadPS4 workflows.

### Configuration

| Setting | Purpose |
| --- | --- |
| `ShadPs4ExecutablePath` | Optional ShadPS4 executable override |
| `Ps4GamesDirectory` | Optional install directory override for serial-based layout |
| `Ps4ExternalPkgExtractorPath` | Optional external extractor used for direct `.pkg` downloads |
| `Ps4FailIfDirectPkgExtractorMissing` | Fails direct PKG installs immediately if no extractor is available |
| `SupportedFileTypes` | Comma-separated PS4 discovery formats; default is `.pkg,.zip,.7z` |

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.pkg` | Yes | Supported package input |
| Extracted folder layout | Yes | Expected to contain a title-ID-style game folder such as `CUSA01607` |
| `.zip` / `.7z` archives | Yes | Used as discovery containers when they contain supported PS4 content |

### Optional content folders

- `UPDATE`
- `DLC`
- `Bonus` or `BONUS`

### Notes

- The installer can use an external PKG extractor when direct `.pkg` content must be normalized into a folder layout.
- The code detects and normalizes update, DLC, and bonus content into platform-specific output folders.

## Deployment Overview

1. The PS4 inspector resolves the base game artifact.
2. Package-based content may be extracted into normalized folders.
3. The base game is installed into the configured PS4 games directory.
4. Update, DLC, and bonus content are materialized into their target roots when detected.

### Typical layout examples

**Base package**

```text
/roms/ps4/Game.pkg
```

**Extracted folder layout**

```text
/roms/ps4/Game.rar
  /CUSA01607/
```

**Optional content folders**

```text
/roms/ps4/Game.rar
  /UPDATE/
  /DLC/
  /BONUS/
```

## RomM Asset Overview

No PS4-specific asset handling is defined in the installer code. Standard RomM metadata and asset behavior applies.

## Special Considerations

- Direct PKG workflows may depend on an external extractor.
- The installer supports bonus content in addition to base game, DLC, and updates.
- Structure and normalization behavior are driven by actual detected content rather than by the template alone.

