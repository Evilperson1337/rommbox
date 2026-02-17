# Glossary

This glossary defines terms used throughout the developer guide.

## Terms

**LaunchBox DataManager**

LaunchBox service that manages the game library, platforms, and metadata. Accessed via `PluginHelper.DataManager` and used heavily in [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26).

**MEF (Managed Extensibility Framework)**

The discovery mechanism LaunchBox uses to load plugin classes that export interfaces like `ISystemMenuItemPlugin` and `IGameMultiMenuItemPlugin`.

**MVVM**

Model-View-ViewModel pattern used by the WPF UI. Implemented via [`ObservableObject`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/ObservableObject.cs:7) and [`RelayCommand`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/RelayCommand.cs:8).

**RomM**

The remote server that hosts ROM metadata, files, and saves. Accessed via [`RommClient`](src/RomM.LaunchBoxPlugin/Services/RommClient.cs:18).

**RomM-sourced game**

A LaunchBox game entry with `Source = "RomM"`. Checked via [`InstallStateService.IsRomMSourcedGame`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:338).

**Install State**

Local persistence that tracks which RomM games are installed, paths, and timestamps, stored in SQLite via [`InstallStateService`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:16).

**Platform Mapping**

Mapping between RomM platform identifiers and LaunchBox platform names. Managed by [`PlatformMappingService`](src/RomM.LaunchBoxPlugin/Services/PlatformMappingService.cs:20).

**Stub Application Path**

Fallback executable path stored when a game is imported but not installed, to satisfy LaunchBox expectations. Created via [`StubApplicationPathService`](src/RomM.LaunchBoxPlugin/Services/StubApplicationPathService.cs:1).
