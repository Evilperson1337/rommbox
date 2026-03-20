# Generic Platform Example

Use this document as the baseline for any platform that does not have a dedicated installer and instead relies on the general fallback installer.

This example is based on the behavior implemented in the general installer, not on the old template. It reflects how the plugin behaves when `UseGeneralFallbackInstaller` is enabled for a platform mapping.

## Summary

| Attribute | Value |
| --- | --- |
| Platform | `<Platform Name>` |
| Plugin platform key | `general` fallback |
| Installer type | General fallback installer |
| Installation model | Portable ROM/artifact deployment |
| Direct files supported | Yes |
| Archive input supported | Yes |
| DLC / updates | Not supported by the general installer |
| Emulator target | Determined by LaunchBox emulator mapping for the platform |

## When to Use This Document

Use this version when all of the following are true:

- the platform does not have its own dedicated installer class
- the platform mapping enables the general fallback installer
- the platform behaves like a standard ROM or content-file deployment workflow

This behavior comes from the general installer implementation in [`GeneralPlatformInstaller`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:21).

## Emulator Target

The general fallback installer does not hard-code a platform-specific emulator.

Instead, it installs the selected artifact and relies on the emulator configuration already associated with the platform in LaunchBox. Any launch arguments are built from the configured ROM settings and the final installed artifact path.

## Supported File Formats

The general installer is configurable per platform mapping. By default, it allows the following file types:

| Format | Default support | Notes |
| --- | --- | --- |
| `.zip` | Yes | Supported by default |
| `.rom` | Yes | Supported by default |
| `.bin` | Yes | Supported by default |
| `.iso` | Yes | Supported by default |
| `.cue` | Yes | Supported by default |
| `.chd` | Yes | Supported by default |
| `.sfc` | Yes | Supported by default |
| `.smc` | Yes | Supported by default |
| `.z64` | Yes | Supported by default |
| `.n64` | Yes | Supported by default |
| `.v64` | Yes | Supported by default |
| `.m3u` | Available by configuration | Included in preferred launch ordering |
| `.ccd` | Available by configuration | Included in preferred launch ordering |
| `.pbp` | Available by configuration | Included in preferred launch ordering |

The default supported file list is defined in [`DefaultSupportedExtensions`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:23), and the default launch-priority order is defined in [`DefaultPreferredLaunchExtensions`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:28).

### Important note

For a generic platform document, you should replace this section with the actual `SupportedFileTypes` configured for that platform mapping if those settings differ from the defaults.

## Deployment Overview

The general fallback installer follows this workflow:

1. Resolve the install root from the platform install directory or ROM root settings.
2. Load installer options from the platform mapping.
3. Resolve the source artifact from the downloaded file or extracted staging content.
4. Confirm that the selected artifact extension is allowed by `SupportedFileTypes`.
5. Resolve the final install folder using the configured layout mode.
6. Move the selected artifact into the final install location.
7. Save the installed artifact path as the LaunchBox application path.

The install flow is implemented in [`InstallAsync()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:205).

## Default Configuration Options

The general fallback installer exposes the following platform configuration fields:

| Setting | Purpose | Default |
| --- | --- | --- |
| `ExtractArchives` | Legacy archive-handling toggle | `false` |
| `ArchiveHandlingMode` | `NeverExtract`, `ExtractForInspection`, or `ExtractAlways` | `NeverExtract` |
| `SupportedFileTypes` | Allowed candidate extensions | `.zip,.rom,.bin,.iso,.cue,.chd,.sfc,.smc,.z64,.n64,.v64` |
| `PreferredLaunchExtensions` | Launch artifact priority order | `.m3u,.cue,.chd,.iso,.zip` |
| `InstallFromArchiveDirectly` | Allows supported archives to be installed directly | `false` in the schema, `true` in the default fallback values |
| `InstallLayoutMode` | `UsePlatformRoot`, `CreatePerGameSubfolder`, or `PreserveArchiveStructure` | `CreatePerGameSubfolder` |
| `ArtifactSelectionMode` | `FirstSupportedFile`, `LargestSupportedFile`, or `ExtensionPriority` | `ExtensionPriority` |
| `UseGameSubdirectory` | Legacy compatibility toggle | `false` |
| `InstallAllMatchingFiles` | Install all candidates instead of only the launch artifact set | `true` in the schema |
| `UseGeneralFallbackInstaller` | Enables this installer for non-dedicated platforms | `false` in the schema, `true` in fallback defaults |

These fields are defined in [`GetConfigDescriptor()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:54) and the fallback values come from [`GetDefaultValues()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:275).

## Detection and Verification

- Detection checks whether the installed path exists on disk and, if so, returns that file as the recommended executable path.
- Verification succeeds when the installed path exists as a file.
- Unlike dedicated installers, the general fallback installer does not apply platform-specific validation rules beyond configured file-type selection.

Detection is implemented in [`DetectAsync()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:164), and verification is implemented in [`VerifyAsync()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:194).

## Uninstall Behavior

The general fallback installer:

1. deletes the installed artifact if it exists
2. optionally deletes the install root if it looks like a game-owned directory
3. preserves notes when cleanup fails

This behavior is implemented in [`UninstallAsync()`](src/RomM.Platforms.General/GeneralPlatformInstaller.cs:318).

## RomM Asset Overview

No platform-specific RomM asset overrides are implemented by the general fallback installer. Platforms that use this installer inherit the standard metadata and asset behavior provided elsewhere in the plugin.

## Special Considerations

- This installer is intentionally generic and is best suited for simple file-based platforms.
- Supported formats, layout behavior, and launch priority are configuration-driven.
- The schema defaults and fallback defaults are not identical in every case, so platform documentation should state the actual mapping values when they are known.
- If a platform later gains a dedicated installer, its documentation should be replaced with a code-specific document rather than continuing to use this generic example.

## Suggested Placeholder Text for TBD Platform Docs

You can adapt the following opening paragraph for remaining platform docs:

> This platform currently uses the plugin's general fallback installer rather than a dedicated platform-specific installer. Installation behavior is configuration-driven and depends on the platform mapping's supported file types, launch priority, and install layout settings.

