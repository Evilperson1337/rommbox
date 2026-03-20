# Sony PlayStation 3

The PlayStation 3 installer supports both decrypted ISO images and JB folder layouts, with optional PKG and RAP handling for updates, DLC, and license files through RPCS3.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Sony PlayStation 3 |
| Plugin platform key | `ps3` |
| Default emulator target | RPCS3 |
| Installation model | Portable base-game deployment with optional package and license install |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported |
| RAP licenses | Supported |

## Emulator Target

The PS3 installer is built around RPCS3 workflows.

### Configuration

| Setting | Purpose |
| --- | --- |
| `Rpcs3ExecutablePath` | Optional RPCS3 executable override |
| `Rpcs3LicenseDirectory` | Optional exdata override used for RAP installation |
| `SkipRegionMismatchedDlc` | Skips DLC packages when their region does not match the base title |
| `SkipUnmatchedRapFiles` | Skips RAP files that cannot be matched to a title ID |
| `PreferMetadataBasedPackageMatching` | Requires package metadata to match more strictly before install |

If the RPCS3 executable cannot be resolved, base game deployment can still succeed, but PKG and RAP installation is skipped.

## Supported File Formats

### Base game formats

| Format | Supported | Notes |
| --- | --- | --- |
| Decrypted `.iso` | Yes | Installed directly as the main launch artifact |
| JB folder layout | Yes | Requires `PS3_GAME` content; the folder layout is copied into the install directory |

### Optional content formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.pkg` | Yes | Used for updates and DLC |
| `.rap` | Yes | Used for license installation |

### Expected optional content folders

- `DLC`
- `UPDATE`

## Deployment Overview

1. The PS3 inspector resolves either an ISO or JB folder base game.
2. The base game is installed into the configured PS3 location.
3. The installer scans staged content for PKG updates, PKG DLC, and RAP files.
4. If RPCS3 is available, packages are staged and installed in batch form.
5. RAP licenses are copied into the RPCS3 license location when possible.

### Base game examples

**Decrypted ISO**

```text
/roms/ps3/Game.iso
```

**JB folder layout**

```text
/roms/ps3/Game.zip
  /PS3_GAME/
  /PS3_DISC.SFB
```

### Optional content behavior

- Update packages are detected and ordered before install.
- DLC packages can be filtered by title ID and region.
- RAP files can be skipped when unmatched, depending on platform settings.

## RomM Asset Overview

No PS3-specific asset mapping overrides are defined in the installer code. Standard RomM metadata and asset behavior applies.

## Special Considerations

- If both a JB folder and ISO are detected, inspection prefers the JB folder and warns about the ambiguity.
- Base game deployment does not require package installation to succeed.
- Package matching behavior can change depending on the metadata preference and region-mismatch settings.

