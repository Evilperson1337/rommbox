# MVVM Implementation

This document describes how the plugin’s WPF UI follows a lightweight MVVM pattern, how view models are wired, and how commands and property change notifications are handled.

## Table of Contents

- [MVVM Building Blocks](#mvvm-building-blocks)
- [View ↔ ViewModel Binding](#view--viewmodel-binding)
- [Commands and UI Actions](#commands-and-ui-actions)
- [Cross-ViewModel Coordination](#cross-viewmodel-coordination)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## MVVM Building Blocks

### ObservableObject

All view models inherit from [`ObservableObject`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/ObservableObject.cs:7), which implements `INotifyPropertyChanged` and provides `SetProperty`/`RaisePropertyChanged` helpers.

Key behavior:

- Only raises change events when values change.
- Uses `CallerMemberName` to avoid manual property name strings.

### RelayCommand

Commands are implemented using [`RelayCommand`](src/RomM.LaunchBoxPlugin/UI/Infrastructure/RelayCommand.cs:8), a minimal `ICommand` wrapper for actions and optional `CanExecute` checks. It also supports UI-thread-safe `RaiseCanExecuteChanged`.

## View ↔ ViewModel Binding

The main shell view (`MainWindow`) directly creates its view model instance in XAML:

```xml
<Window.DataContext>
    <vm:MainWindowViewModel/>
</Window.DataContext>
```

See [`MainWindow.xaml`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml:10).

`MainWindowViewModel` then instantiates page view models:

- [`ConnectionViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ConnectionViewModel.cs:17)
- [`PlatformsViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/PlatformsViewModel.cs:14)
- [`ImportViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ImportViewModel.cs:20)
- [`TestViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/TestViewModel.cs:7)

Navigation is done by swapping the `CurrentPage` object, which the view binds to `ContentControl.Content` in [`MainWindow.xaml`](src/RomM.LaunchBoxPlugin/UI/MainWindow.xaml:56).

## Commands and UI Actions

Most user actions are bound to command properties in view models:

- Connection: `TestConnectionCommand`, `SaveCommand` in [`ConnectionViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ConnectionViewModel.cs:118).
- Platform mapping: `RefreshCommand`, `SaveMappingsCommand` in [`PlatformsViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/PlatformsViewModel.cs:49).
- Import: `RefreshCommand`, `ImportCommand` in [`ImportViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ImportViewModel.cs:85).

Commands typically:

1. Validate input or connection state.
2. Trigger async work (`Task`/`await`).
3. Marshal UI updates via `Application.Current.Dispatcher`.

## Cross-ViewModel Coordination

The shell view model acts as the coordination hub:

- `IsConnected` and `IsServerConfigured` control navigation availability.
- `SelectedPlatform` is updated by Import view selections.
- The footer buttons are controlled by `SelectedNavIndex`.

See [`MainWindowViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/MainWindowViewModel.cs:8) for these coordination points.

## Happy Path vs. Failure Paths

**Happy path**

1. User navigates to Connection.
2. Connection test succeeds; UI status updates.
3. Platforms and Import tabs enable.
4. User imports games; UI shows progress updates.

**Failure paths**

- Connection test failure → UI shows error and status stays “Not Connected”.
- Missing server URL or credentials → Import tab shows “Server not configured” message.
- Background errors → Logged via [`LoggingService`](src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs:5); UI typically shows message boxes.

Next: see [Data Flow](DataFlow.md) for workflow-level interactions across view models and services.
