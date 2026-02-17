# Error Handling

This document outlines how the plugin detects, classifies, and handles errors across API calls, local storage, and UI workflows.

## Table of Contents

- [Error Taxonomy](#error-taxonomy)
- [RomM API Errors](#romm-api-errors)
- [Settings and Credential Errors](#settings-and-credential-errors)
- [Import + Install Errors](#import--install-errors)
- [UI Error Surfacing](#ui-error-surfacing)

## Error Taxonomy

The codebase uses a mix of typed exceptions and return-result patterns:

- Typed API errors via [`RommApiException`](src/RomM.LaunchBoxPlugin/Services/RommApiException.cs:19)
- Result objects for operations such as downloads and installs
- Logging + message boxes for UI feedback

## RomM API Errors

The RomM HTTP client normalizes responses into `RommApiException` with an error type:

- `AuthExpired`
- `NotFound`
- `RateLimited`
- `ServerError`
- `BadResponse`

See [`RommClient.EnsureSuccessAsync`](src/RomM.LaunchBoxPlugin/Services/RommClient.cs:542).

**Happy path**

- Successful HTTP responses deserialize into model objects.

**Failure paths**

- Non-2xx response → throws `RommApiException`.
- Missing credentials → `AuthExpired` error type.

## Settings and Credential Errors

Settings operations are intentionally resilient:

- [`SettingsManager.Load()`](src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:42) falls back to defaults on failure.
- Save failures are logged and do not crash the plugin host.

Credentials are stored using [`CredentialStore`](src/RomM.LaunchBoxPlugin/Storage/CredentialStore.cs:1). Exceptions are logged and in some cases rethrown when saving credentials.

## Import + Install Errors

### ImportService

The import loop treats each ROM independently:

- Per-ROM failures are logged and counted.
- If a game creation fails, it is removed from LaunchBox when possible.
- Final `ImportResult` includes counts of success/failure/skipped.

See [`ImportService.ImportPlatformCatalogAsync`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:95).

### Download + Extraction

- [`DownloadService`](src/RomM.LaunchBoxPlugin/Services/DownloadService.cs:14) returns a `DownloadResult` with `Success=false` and an error message when downloads fail.
- [`ArchiveService`](src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:18) logs failures and throws when no extraction fallback exists.

### Install State

Install state writes are guarded by `SemaphoreSlim` and exceptions are logged; failures do not crash the host.

See [`InstallStateService.UpsertStateAsync`](src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:254).

## UI Error Surfacing

View models typically surface errors through:

- Message boxes (`MessageBox.Show`)
- Status text fields bound to the UI
- Logging via [`LoggingService`](src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs:5)

Example: [`ConnectionViewModel`](src/RomM.LaunchBoxPlugin/UI/ViewModels/ConnectionViewModel.cs:127) shows a message box when the connection test fails.

Next: see [Extension Guide](ExtensionGuide.md).
