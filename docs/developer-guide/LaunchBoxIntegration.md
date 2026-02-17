# LaunchBox Integration

This document describes how the plugin integrates with LaunchBox’s plugin APIs, how it interacts with the `IDataManager`, and the specific integration points used.

## Table of Contents

- [MEF Plugin Exports](#mef-plugin-exports)
- [Tools Menu Integration](#tools-menu-integration)
- [Game Context Menu Integration](#game-context-menu-integration)
- [IDataManager Usage](#idatamanager-usage)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## MEF Plugin Exports

LaunchBox discovers plugins via MEF. The plugin exports:

- `ISystemMenuItemPlugin` via [`RomMToolsMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/ToolsMenu/RomMToolsMenuItem.cs:12)
- `IGameMultiMenuItemPlugin` via [`RommMultiMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/GameMenu/RommMultiMenuItem.cs:21)

These exports are loaded when LaunchBox starts and when users access the menu entries.

## Tools Menu Integration

[`RomMToolsMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/ToolsMenu/RomMToolsMenuItem.cs:12) adds a “RomM” entry under the LaunchBox Tools menu. Selecting it:

1. Ensures plugin initialization via [`PluginEntry`](src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:12).
2. Creates or reuses a WPF `Application`.
3. Opens the [`MainWindow`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml:1) dialog.

## Game Context Menu Integration

[`RommMultiMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/GameMenu/RommMultiMenuItem.cs:21) injects a “RomM” submenu in the game context menu for RomM-sourced games:

- Install/Uninstall flows
- “Play on RomM” action (opens external URL)

The adapter uses [`InstallStateService.IsRomMSourcedGame`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:338) to decide whether a game is a RomM title.

## IDataManager Usage

The plugin uses the LaunchBox `IDataManager` for all library modifications:

- Create new games: [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:197)
- Update platform mappings and metadata
- Refresh data on UI close: [`MainWindow.OnClosed`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml.cs:22)

If `PluginHelper.DataManager` is null, imports and installs abort with an `InvalidOperationException`.

## Happy Path vs. Failure Paths

**Happy path**

1. LaunchBox discovers the plugin exports.
2. Tools menu opens WPF UI successfully.
3. Import and install flows update LaunchBox library via `IDataManager`.
4. Closing the UI triggers data reloads.

**Failure paths**

- `IDataManager` unavailable → import/install flows fail immediately.
- WPF `Application` not created → Tools menu logs error and no UI appears.
- Game not RomM-sourced → context menu entry hidden.

Next: see [Glossary](Glossary.md).
