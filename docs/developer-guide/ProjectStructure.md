# Project Structure

This document explains the repository layout, namespace conventions, and how the codebase is organized for this LaunchBox plugin.

## Table of Contents

- [Repository Layout](#repository-layout)
- [Key Directories](#key-directories)
- [Namespace Conventions](#namespace-conventions)
- [Assemblies and Build Targets](#assemblies-and-build-targets)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## Repository Layout

```
src/
  RomM.LaunchBoxPlugin/        # Main plugin assembly (RomMbox)
  RomM.LaunchBoxPlugin.Tests/  # Unit tests
implementations_docs/          # Spec + reference docs used for implementation
prompts/                       # Agent prompts for documentation and steps
assets/                        # Branding assets for repo documentation
```

The plugin code lives in [`src/RomM.LaunchBoxPlugin`](src/RomM.LaunchBoxPlugin/RomMbox.csproj:1). This is the assembly LaunchBox loads.

## Key Directories

### `Plugin/`

LaunchBox integration adapters and bootstrap glue.

- [`PluginEntry`](src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:12)
- [`RomMToolsMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/ToolsMenu/RomMToolsMenuItem.cs:12)
- [`RommMultiMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/GameMenu/RommMultiMenuItem.cs:21)

### `Services/`

Core business logic and integration services.

- API client: [`RommClient`](src/RomM.LaunchBoxPlugin/Services/RommClient.cs:18)
- Import orchestration: [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26)
- Download/extraction: [`DownloadService`](src/RomM.LaunchBoxPlugin/Services/DownloadService.cs:14), [`ArchiveService`](src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:18)
- Install tracking: [`InstallStateService`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:16)
- Settings + credentials: [`SettingsManager`](src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:14)

### `UI/`

WPF UI + MVVM view models.

- Shell window: [`MainWindow`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml:1)
- View models: `UI/ViewModels/*`
- Controls/views: `UI/Views/*`
- Infrastructure: [`ObservableObject`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/ObservableObject.cs:7), [`RelayCommand`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/RelayCommand.cs:8)

### `Models/`

Domain and DTO-style models used across services and UI.

- RomM API models: [`RommRom`](src/RomM.LaunchBoxPlugin/Models/Romm/RommRom.cs:1), [`RommPlatform`](src/RomM.LaunchBoxPlugin/Models/Romm/RommPlatform.cs:1)
- Import models: [`ImportResult`](src/RomM.LaunchBoxPlugin/Models/Import/ImportResult.cs:1)
- Platform mapping: [`PlatformMapping`](src/RomM.LaunchBoxPlugin/Models/PlatformMapping/PlatformMapping.cs:1)

### `Storage/`

Local persistence helpers.

- [`CredentialStore`](src/RomM.LaunchBoxPlugin/Storage/CredentialStore.cs:1)

## Namespace Conventions

- `RomMbox.Plugin.*` → LaunchBox integration and entry points.
- `RomMbox.Services.*` → All business logic and external integration.
- `RomMbox.UI.*` → WPF views and MVVM view models.
- `RomMbox.Models.*` → Data models and DTOs.

These namespaces map directly to folder structure, making navigation consistent.

## Assemblies and Build Targets

The plugin targets **.NET 9 (Windows)** and is compiled as a class library:

- Target framework: `net9.0-windows`
- WPF enabled: `<UseWPF>true</UseWPF>`

See [`RomMbox.csproj`](src/RomM.LaunchBoxPlugin/RomMbox.csproj:1) for details.

Key build dependencies:

- `Unbroken.LaunchBox.Plugins` reference from local LaunchBox installation (hinted path in project file).
- `Microsoft.Data.Sqlite` for install state persistence.

## Happy Path vs. Failure Paths

**Happy path**

1. LaunchBox loads the assembly from its `Plugins` folder.
2. MEF discovers the exported adapter classes under `Plugin/Adapters`.
3. The UI and services operate inside the LaunchBox host process.

**Failure paths**

- Wrong LaunchBox DLL reference → build failure until `Unbroken.LaunchBox.Plugins.dll` is correctly referenced.
- Missing assets (icons) → UI elements render without images (handled gracefully in adapters).

Next: see [MVVM Implementation](MVVMImplementation.md).
