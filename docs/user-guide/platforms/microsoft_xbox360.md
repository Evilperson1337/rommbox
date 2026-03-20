# Microsoft Xbox 360

The Xbox 360 platform installer supports portable deployment of base game artifacts and also detects optional update and DLC package archives for Xenia content folders. Base game installation and optional content handling are separate parts of the workflow.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Microsoft Xbox 360 |
| Plugin platform key | `xbox360` |
| Supported aliases | Xbox 360 aliases defined in code, including `xbox360` and `x360` |
| Default emulator target | xenia |
| Installation model | Portable base-game deployment with optional package extraction |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Supported |

## Emulator Target

The installer logs `xenia` as the configured emulator after installation.

The plugin also tries to resolve the Xenia root when optional content needs to be installed or removed, because title updates and DLC are copied into Xenia content directories.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.iso` | Yes | Supported base game artifact |
| `.xex` | Yes | Supported base game artifact, including extracted layouts that expose `default.xex` |

### Optional content handling

- The inspector can also detect package archives stored as compressed files such as `.zip` when they represent Xbox 360 update or DLC content.
- These package archives are not used as the main launch artifact.
- Instead, they are extracted into Xenia content folders under the resolved title ID.

## Deployment Overview

1. The plugin inspects staged content and resolves the canonical base-game artifact.
2. It installs the base game as a portable file.
3. It records the platform content ID from the inspection result.
4. If update or DLC package archives were detected, it tries to install them into the Xenia content structure.

### Optional content layout

When optional content is installed, the plugin uses Xenia content subdirectories:

- `000B0000` for title updates
- `00000002` for DLC

### Typical install result

- The base game becomes the LaunchBox application path.
- Optional content is extracted separately into emulator-managed content folders.
- Uninstall removes the installed base artifact and also attempts to clean up matching Xenia update and DLC directories.

## RomM Asset Overview

No Xbox 360-specific asset mapping overrides are defined in the installer code. Standard RomM metadata and asset handling applies.

## Special Considerations

- Update and DLC installation depends on successfully resolving the Xenia root.
- Package installation is additive and does not block base game deployment unless the base artifact itself cannot be resolved.
- Extracted layouts that expose `default.xex` are recognized during inspection.

