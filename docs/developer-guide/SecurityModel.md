# Security Model

This document describes how the plugin handles credentials, network access, and local storage from a security perspective.

## Table of Contents

- [Credential Storage](#credential-storage)
- [Transport Security](#transport-security)
- [Local Data Storage](#local-data-storage)
- [Logging and Sensitive Data](#logging-and-sensitive-data)
- [Happy Path vs. Failure Paths](#happy-path-vs-failure-paths)

## Credential Storage

Credentials are stored via [`CredentialStore`](src/RomM.LaunchBoxPlugin/Storage/CredentialStore.cs:1) and accessed through [`SettingsManager`](src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:81).

Settings also track whether saved credentials should be reused:

- `HasSavedCredentials`
- `UseSavedCredentials`

See [`PluginSettings`](src/RomM.LaunchBoxPlugin/Services/Settings/PluginSettings.cs:10).

## Transport Security

By default, RomM calls are made via HTTPS. The user can opt into invalid TLS certificates using the `AllowInvalidTls` flag.

This flag is respected in:

- [`RommClient`](src/RomM.LaunchBoxPlugin/Services/RommClient.cs:67)
- [`AuthService`](src/RomM.LaunchBoxPlugin/Services/Auth/AuthService.cs:55)

**Security tradeoff:** allowing invalid TLS is convenient for self-hosted RomM, but reduces transport security.

## Local Data Storage

The plugin stores:

- Settings JSON in `system/settings.json`
- Install state in `system/romm_install_state.db`
- Logs in `system/RomM.Plugin.log`

Paths are resolved by [`PluginPaths`](src/RomM.LaunchBoxPlugin/Services/Paths/PluginPaths.cs:10).

## Logging and Sensitive Data

The logging system emits diagnostics at the configured log level. There are some guardrails:

- `SettingsManager.SaveCredentials` logs password length, not the password.
- [`AuthService`](src/RomM.LaunchBoxPlugin/Services/Auth/AuthService.cs:107) includes helpers to mask passwords, though only shortened bodies are logged.

Developers should avoid logging raw credentials or full server responses containing secrets.

## Happy Path vs. Failure Paths

**Happy path**

- Credentials are stored securely and reused for background tests.
- TLS validation is enforced unless explicitly disabled.

**Failure paths**

- Missing credentials → `RommClient` throws `AuthExpired`.
- Invalid TLS allowed → requests succeed but without certificate validation.

Next: see [LaunchBox Integration](LaunchBoxIntegration.md).
