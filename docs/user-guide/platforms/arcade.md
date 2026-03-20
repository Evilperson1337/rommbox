# Arcade

The Arcade platform installer is designed for emulator workflows that launch a single ROM-set archive directly. In the current code, Arcade installs are built around `.zip` ROM sets and are intended for MAME- or FinalBurn-style setups.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Arcade |
| Plugin platform key | `arcade` |
| Supported aliases | `arcade`, `arcadegames`, `mame`, `finalburnneo`, `final burn neo`, `fbneo`, `fba` |
| Default emulator target | Determined by LaunchBox mapping or the configured ROM settings |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported |

## Emulator Target

The Arcade installer does not hard-code a specific emulator executable. It installs the selected ROM set and then uses the emulator configuration already associated with the platform in LaunchBox or in the platform's ROM settings.

The installer logs the resolved emulator mode during installation, but the emulator selection itself is external to the Arcade plugin implementation.

## Supported File Formats

The Arcade installer validates installed content as a single `.zip` archive.

| Format | Supported | Notes |
| --- | --- | --- |
| `.zip` | Yes | Required launch artifact and installed format |

### Notes

- The installer expects the resolved Arcade launch artifact to be a `.zip` ROM set archive.
- If inspection resolves anything other than `.zip`, installation is rejected.
- DLC, update, RAP/license, and installer-based workflows are not implemented for Arcade.

## Deployment Overview

Arcade installation is intentionally simple:

1. The plugin inspects the staged download content.
2. It resolves a single Arcade ROM set archive.
3. The `.zip` file is moved into the configured install root.
4. The installed `.zip` becomes the LaunchBox application path.

### Typical install result

- Installed content remains a single ROM-set archive.
- The game is treated as a portable install.
- Uninstall removes only the installed ROM archive and intentionally avoids deleting folders.

## RomM Asset Overview

No Arcade-specific asset handling is defined in the platform installer code. Arcade titles use the standard RomM metadata and asset flow provided by the plugin, with no special platform overrides documented in the Arcade installer itself.

## Special Considerations

- Only `.zip` ROM sets are accepted as final installed artifacts.
- The installer requires staging inspection before install so it can confirm the correct launch archive.
- Emulator behavior depends on the external emulator mapping you configure in LaunchBox.

