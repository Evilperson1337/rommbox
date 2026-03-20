# Flash Player

The Flash Player platform installer is built for standalone `.swf` content launched through Ruffle. Unlike most console platforms, this integration requires an emulator path to be resolved because the plugin explicitly validates that a working Ruffle executable is available.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Flash Player |
| Plugin platform key | `flashplayer` |
| Supported aliases | `flash`, `flash player`, `flashplayer`, `adobe flash`, `adobe flash player`, `browser (flash/html5)`, `browser flash html5`, `flash html5`, `macromedia flash`, `swf` |
| Default emulator target | Ruffle |
| Installation model | Portable file deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| RomM web play | Supported through the `ruffle` path suffix |

## Emulator Target

Flash content is launched with Ruffle.

### Configuration

| Setting | Purpose |
| --- | --- |
| `RuffleExecutablePath` | Optional explicit path to the Ruffle executable used to launch installed `.swf` files |

If this path is not supplied, the plugin tries to resolve Ruffle from the LaunchBox emulator mapping. Installation fails if a usable Ruffle executable cannot be found on disk.

## Supported File Formats

| Format | Supported | Notes |
| --- | --- | --- |
| `.swf` | Yes | Required installed and launch format |

### Notes

- Flash installation accepts direct files or archive-based downloads, but the resolved launch artifact must end as `.swf`.
- Unsupported extensions are rejected during detection and install.
- DLC, updates, RAP files, and installer-based workflows are not implemented.

## Deployment Overview

1. The plugin resolves a single `.swf` launch artifact from the staged content.
2. It validates the configured install root.
3. It resolves and validates the Ruffle executable.
4. The `.swf` file is moved into the configured game directory.
5. The installed `.swf` becomes the application path, and launch arguments are generated for Ruffle.

### Typical install result

- Installed content remains a portable `.swf` file.
- The platform logs the resolved Ruffle executable and generated launch command.
- Uninstall removes only the installed Flash artifact and preserves parent folders.

## RomM Asset Overview

No Flash-specific metadata or asset mapping rules are implemented in the platform installer. Flash titles use the standard RomM asset and metadata behavior supplied by the wider plugin.

## Special Considerations

- A valid Ruffle executable is required for a successful install.
- This platform advertises RomM web play support using the `ruffle` suffix.
- Because the plugin validates the emulator path directly, Flash behaves more like an application launcher than a generic ROM platform.

