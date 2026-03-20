# Windows Games

The Windows platform installer handles game archives that resolve to either portable applications or installer-driven deployments. It is the most application-oriented platform in the project and includes special handling for executable discovery, installer execution, silent installation, and uninstall cleanup.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | Windows Games |
| Plugin platform key | `windows` |
| Default emulator target | Not applicable |
| Installation model | Portable or installer-based deployment |
| Direct files supported | No |
| Archive input supported | Yes |
| Silent installer support | Yes |
| DLC / updates | Supported |

## Installation Types

The installer supports two primary Windows deployment models.

### Portable games

Portable games are installed by extracting or staging the game files, resolving the correct executable, and using that executable as the application path.

### Installer-based games

Installer games are passed through the Windows install subsystem, which can execute supported installers and then detect the final executable inside the installed game root.

The current code also includes uninstall support for installer-driven Windows games, including an attempt to run an Inno Setup uninstaller silently before falling back to direct file removal.

## Supported File Formats and Layouts

The Windows installer does not validate a single extension list in the same way as the console-specific installers. Instead, it relies on staged archive content and a Windows-specific install subsystem to classify the install and resolve the final executable.

### Supported workflow characteristics

- Archive-based input
- Portable application folders
- Installer-driven deployment
- Silent installer support where available
- Update and DLC folders handled by the Windows install subsystem

## Deployment Overview

1. The plugin passes the staged content to the Windows install subsystem.
2. The subsystem determines whether the game is portable or installer-based.
3. The final executable is resolved from the installed output.
4. The executable path and install root are returned to LaunchBox.

### Uninstall behavior

- Portable installs are removed by deleting the install root.
- Installer installs first try to run an Inno uninstaller silently.
- If that fails or is unavailable, the plugin falls back to directory deletion.

## RomM Asset Overview

No Windows-specific RomM asset mapping overrides are defined in the platform installer itself. Standard plugin metadata and asset behavior applies.

## Special Considerations

- Windows installs are directory-oriented rather than simple file-oriented.
- Detection resolves the install root first and then searches for candidate executables.
- The plugin distinguishes between installer and portable uninstall behavior.
- Because classification is delegated to the Windows install subsystem, exact installer support depends on the logic implemented there rather than on a hard-coded extension table in the top-level platform installer.

