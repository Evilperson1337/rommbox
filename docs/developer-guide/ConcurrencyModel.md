# Concurrency Model

This document describes how the plugin uses asynchronous operations, background tasks, and UI thread marshaling to keep the LaunchBox host responsive.

## Table of Contents

- [Async Patterns](#async-patterns)
- [UI Thread Dispatching](#ui-thread-dispatching)
- [Background Tasks](#background-tasks)
- [Synchronization Primitives](#synchronization-primitives)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## Async Patterns

The codebase uses `async`/`await` heavily in service and view model operations to avoid blocking the UI thread:

- [`RommClient`](src/RomM.LaunchBoxPlugin/Services/RommClient.cs:18) wraps HTTP calls asynchronously.
- [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26) paginates and downloads with async API calls.
- [`ArchiveService`](src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:18) uses `Task.Run` for extraction work.

## UI Thread Dispatching

View models often update observable collections and properties through the WPF dispatcher:

- [`ImportViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ImportViewModel.cs:20) marshals collection updates with `Application.Current.Dispatcher.InvokeAsync`.
- [`ConnectionViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ConnectionViewModel.cs:17) updates status colors and text on the UI thread.

This avoids cross-thread collection access exceptions in WPF.

## Background Tasks

### Background connection test

[`PluginEntry`](src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:12) starts a background connection test if saved credentials are present. It uses `Task.Run` to avoid blocking LaunchBox and emits an event when done.

### Install/Uninstall flows

[`RommMultiMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/GameMenu/RommMultiMenuItem.cs:21) runs install/uninstall workflows on background tasks while updating a progress window.

### Extraction and downloads

[`DownloadService`](src/RomM.LaunchBoxPlugin/Services/DownloadService.cs:14) performs downloads asynchronously and reports progress. Archive extraction uses a background task to avoid UI stalling.

## Synchronization Primitives

### InstallStateService

[`InstallStateService`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:16) uses two `SemaphoreSlim` instances:

- `_sync`: serializes DB operations.
- `_initSync`: ensures one-time initialization.

This prevents multiple concurrent SQLite writes and schema initialization races.

## Happy Path vs. Failure Paths

**Happy path**

- Async operations return without blocking UI.
- UI updates are safely marshaled to the dispatcher.
- Install state and background tasks run concurrently but safely.

**Failure paths**

- UI dispatcher unavailable → view model falls back to no update or logs error.
- Task failures → caught and logged, often with user-facing error message boxes.
- Cancellation tokens in import operations cause `OperationCanceledException`, caught by view models and treated as a user cancellation.

Next: see [Error Handling](ErrorHandling.md).
