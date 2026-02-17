# Extension Guide

This document describes how to extend the plugin safely: adding new services, UI pages, menu items, and RomM workflow features.

## Table of Contents

- [Extending Services](#extending-services)
- [Adding UI Pages](#adding-ui-pages)
- [Adding LaunchBox Menu Actions](#adding-launchbox-menu-actions)
- [Extending RomM API Models](#extending-romm-api-models)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## Extending Services

There is no dependency injection container; services are wired manually. The usual pattern is:

1. Add the service class in `Services/`.
2. Instantiate it where it is needed (typically in a view model or in [`PluginEntry`](src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:12)).
3. Reuse `LoggingService` and `SettingsManager` as shared dependencies.

Example pattern (existing code):

- [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26) is created inside view models when the server URL is available.

## Adding UI Pages

Steps:

1. Create a XAML view under `UI/Views/`.
2. Create a matching view model under `UI/ViewModels/` that inherits [`ObservableObject`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/ObservableObject.cs:7).
3. Add the view model to [`MainWindowViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/MainWindowViewModel.cs:8).
4. Update the navigation list in [`MainWindow.xaml`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml:45).
5. Update `NavigateByIndex` to map to the new page.

## Adding LaunchBox Menu Actions

LaunchBox menu actions are MEF exports. To add a new menu item:

1. Create a class that implements the appropriate interface (`ISystemMenuItemPlugin`, `IGameMenuItemPlugin`, or `IGameMultiMenuItemPlugin`).
2. Decorate with `[Export(typeof(...))]`.
3. Use [`PluginEntry.EnsureInitialized()`](src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:79) to initialize shared services.
4. Use `LoggingService` for diagnostics and safe error handling.

Example reference: [`RomMToolsMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/ToolsMenu/RomMToolsMenuItem.cs:12).

## Extending RomM API Models

RomM DTOs live in `Models/Romm/`. If the API adds new fields:

1. Extend the appropriate model (e.g., [`RommRom`](src/RomM.LaunchBoxPlugin/Models/Romm/RommRom.cs:1)).
2. Ensure the JSON property name matches RomM’s response.
3. Update any import logic that depends on the new fields in [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26).

## Happy Path vs. Failure Paths

**Happy path**

- New extensions reuse existing service wiring and logging.
- UI additions follow the view model + binding patterns.

**Failure paths**

- Missing UI navigation hookup → page not reachable.
- Missing MEF export → LaunchBox never discovers the new menu item.
- Extending models without updating services → new data ignored or unused.

Next: see [Performance Considerations](PerformanceConsiderations.md).
