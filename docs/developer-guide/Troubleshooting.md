# Troubleshooting

This document provides common failure symptoms and the likely root causes in the plugin.

## Table of Contents

- [UI Does Not Open](#ui-does-not-open)
- [Connection Test Fails](#connection-test-fails)
- [Platforms Not Loading](#platforms-not-loading)
- [Import Fails or Skips Games](#import-fails-or-skips-games)
- [Install/Uninstall Does Nothing](#installuninstall-does-nothing)
- [Logs and Diagnostics](#logs-and-diagnostics)

## UI Does Not Open

**Symptoms**

- Clicking Tools → RomM shows nothing.

**Likely causes**

- WPF app not initialized or exception thrown in UI thread.
- Plugin failed to initialize services.

**Checks**

- Inspect log file in `system/RomM.Plugin.log` (see [`PluginPaths.GetLogPath`](src/RomM.LaunchBoxPlugin/Services/Paths/PluginPaths.cs:80)).
- Verify [`RomMToolsMenuItem.OnSelected`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/ToolsMenu/RomMToolsMenuItem.cs:69) is being invoked by LaunchBox.

## Connection Test Fails

**Symptoms**

- Status is “Not Connected”.
- Message box shows “Connection failed.”

**Likely causes**

- Server URL invalid or unreachable.
- Credentials incorrect.
- TLS errors (self-signed cert).

**Checks**

- Ensure server URL format and port are valid.
- Toggle “Allow invalid TLS” and retry.
- Review connection logs in [`AuthService`](src/RomM.LaunchBoxPlugin/Services/Auth/AuthService.cs:67).

## Platforms Not Loading

**Symptoms**

- “Server not configured” message in Platforms or Games tab.

**Likely causes**

- Missing credentials or server URL.
- RomM API unreachable.

**Checks**

- Confirm settings saved in [`ConnectionViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ConnectionViewModel.cs:254).
- Review `DiscoverPlatformsAsync` logs in [`PlatformMappingService`](src/RomM.LaunchBoxPlugin/Services/PlatformMappingService.cs:93).

## Import Fails or Skips Games

**Symptoms**

- Import report shows failures or all games skipped.

**Likely causes**

- LaunchBox `IDataManager` unavailable.
- Duplicate detection rules filtering entries.

**Checks**

- Look for `InvalidOperationException` from [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:115).
- Confirm duplicate options in the UI and `AllowDuplicates` setting.

## Install/Uninstall Does Nothing

**Symptoms**

- Selecting Install/Uninstall does not change game state.

**Likely causes**

- Missing RomM identifiers on the game.
- Download or extraction failure.

**Checks**

- Verify `RomM.RomId` and `RomM.PlatformId` custom fields are set on the game.
- Review logs from [`DownloadService`](src/RomM.LaunchBoxPlugin/Services/DownloadService.cs:14) and [`ArchiveService`](src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:18).

## Logs and Diagnostics

Logs are written to:

- `system/RomM.Plugin.log` (resolved via [`PluginPaths`](src/RomM.LaunchBoxPlugin/Services/Paths/PluginPaths.cs:80))

Increase verbosity via `LogLevelName` in [`PluginSettings`](src/RomM.LaunchBoxPlugin/Services/Settings/PluginSettings.cs:15).
