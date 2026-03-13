# Platform Installers (Windows PoC)

## Current Windows Install Flow (Core)

Windows install logic currently lives in core services and flows through:

- [`RomMInstallService`](src/RomM.LaunchBoxPlugin/Services/Legacy/RomMInstallService.cs:1) and [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:1) for legacy install paths.
- [`InstallCoordinator`](src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs:1) and steps, especially [`InstallContentStep`](src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:1), which calls [`WindowsInstallSubsystem`](src/RomM.LaunchBoxPlugin/Services/Install/WindowsInstallSubsystem.cs:1).
- [`WindowsInstallSubsystem`](src/RomM.LaunchBoxPlugin/Services/Install/WindowsInstallSubsystem.cs:1) orchestrates installer vs portable handling, optional content, executable resolution, and installer invocation.
- [`WindowsInstallClassifier`](src/RomM.LaunchBoxPlugin/Services/Install/WindowsInstallClassifier.cs:1) detects install type and Inno signatures.
- [`ExecutableResolver`](src/RomM.LaunchBoxPlugin/Services/Install/ExecutableResolver.cs:1) handles executable candidate discovery and recommendation.
- Uninstall flows use [`RomMUninstallService`](src/RomM.LaunchBoxPlugin/Services/Install/RomMUninstallService.cs:1) and [`RomMDeleteService`](src/RomM.LaunchBoxPlugin/Services/Legacy/RomMDeleteService.cs:1) to clean content and update install state.

UI for install/uninstall progress and executable selection is in WPF views under [`UI`](src/RomM.LaunchBoxPlugin/UI/README.md:1). Logging is handled by [`LoggingService`](src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs:1). Install state persistence is in [`InstallStateService`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:1), backed by `romm.db`.

## Target Refactor: Extract Windows Platform Logic

Introduce a platform installer abstraction and a Windows implementation loaded via reflection. The goal is to move Windows-specific detection/install/uninstall logic out of core and into a platform library while retaining core responsibilities:

### Moves to Windows Platform Library

- Detection logic and executable candidate discovery (`WindowsInstallClassifier`, `ExecutableResolver`).
- Installer workflows and elevation-sensitive execution (`WindowsInstallSubsystem`, installer batch, Inno detection/uninstall).
- Uninstall cleanup logic currently in [`RomMDeleteService`](src/RomM.LaunchBoxPlugin/Services/Legacy/RomMDeleteService.cs:1).
- Structured progress reporting and warnings.
- Structured logging using the existing `LoggingService` (passed via context).

### Remains in Core

- RomM server communication (metadata, downloads).
- Archive extraction and staging management.
- LaunchBox registration, UI, and user confirmation surfaces.
- Install state persistence in `romm.db` (source of truth).
- Logging configuration and operation correlation.

Core will orchestrate when to call platform installers and how to persist results. The platform library will not reference LaunchBox UI types; any UI prompts (e.g., executable selection) will be mediated by core through callbacks and context.

## Platform Installer Contract

Define an abstraction in `RomM.Platforms.Abstractions`:

```csharp
public interface IPlatformInstaller
{
    string PlatformKey { get; }
    string DisplayName { get; }

    Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct);

    Task<InstallResult> InstallAsync(
        InstallContext ctx,
        IProgress<InstallProgress> progress,
        CancellationToken ct);

    Task<UninstallResult> UninstallAsync(
        UninstallContext ctx,
        IProgress<InstallProgress> progress,
        CancellationToken ct);

    Task<VerifyResult> VerifyAsync(
        VerifyContext ctx,
        CancellationToken ct);
}
```

All models are serializable and structured for logs and future UI surfaces.

## Reflection Loader (Core)

Core will load platform installers from:

```
<LaunchBox>/Plugins/RomMbox/system/platforms/*.dll
```

Loading steps:

1. Scan the folder for `.dll` files.
2. Load assemblies via reflection (restricted to that folder).
3. Discover types implementing `IPlatformInstaller`.
4. Instantiate safely (prefer DI if available, fallback to parameterless constructors).
5. Register installers by `PlatformKey`.
6. Log load success/failure, duplicate keys, and contract mismatches.

Failure to find a Windows installer will be handled gracefully by core (install/uninstall actions will surface a structured error).

## Folder Layout

```
LaunchBox/
  Plugins/
    RomMbox/
      system/
        platforms/
          RomM.Platforms.Windows.dll
          RomM.Platforms.Snes.dll
          RomM.Platforms.Arcade.dll
```

Core will not load from other locations.

## State Boundary

`romm.db` remains the source of truth and is updated only by core. The platform library returns structured results including resolved executable, install root, and install type; core persists these fields via `InstallStateService`.

## ROM Platform Base

ROM-based platforms (e.g., Arcade, SNES) use a shared base implementation that provides:

- ROM candidate discovery based on file extensions.
- Archive policy control (extract vs preserve).
- Deterministic install path resolution in per-platform ROM folders.
- File placement with logging and progress reporting.
- Install/detect/verify/uninstall flows that treat ROMs as first-class citizens.

ROM platform DLLs expose a `RomPlatformInstallerBase` subclass that declares a `RomInstallProfile` (extensions, archive policy, and emulator metadata). Core supplies any per-platform config through `RomInstallSettings`.

## Testing Expectations

- Unit tests for reflection loader (success, failure, duplicate key).
- Unit tests for Windows detection / executable resolution logic.
