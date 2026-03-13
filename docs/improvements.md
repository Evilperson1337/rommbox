# Improvement Report

## System Understanding Summary

The codebase is structured as a LaunchBox plugin with explicit service wiring in [`PluginEntry`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:18). Runtime flows pass through a service layer that manages RomM API access, LaunchBox integration, download/extraction, install orchestration, and local persistence. Platform-specific behavior is split between dynamically loaded installer plugins discovered by [`PlatformInstallerLoader`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerLoader.cs:12) and shared ROM installation helpers in [`RomM.Platforms.*`](../src/RomM.Platforms.General/GeneralPlatformInstaller.cs).

The end-to-end install pipeline is orchestrated by [`InstallCoordinator.RunAsync()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs:26), with download behavior in [`DownloadService.DownloadRomAsync()`](../src/RomM.LaunchBoxPlugin/Services/DownloadService.cs:44), extraction in [`ArchiveService.ExtractAsync()`](../src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:65), and platform-specific installation in the very large [`InstallContentStep.ExecuteAsync()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:32). Configuration is split between JSON settings in [`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:17), credentials in the credential store, and SQLite-backed state/mappings in [`InstallStateService`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:20) and [`PlatformMappingStore`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs:13).

The recommendations below are grounded in those observed implementation boundaries and the associated tests such as [`InstallContentStepCanonicalPathTests`](../src/RomM.LaunchBoxPlugin.Tests/Services/InstallContentStepCanonicalPathTests.cs:31), [`GeneralPlatformInstallerBehaviorTests`](../src/RomM.LaunchBoxPlugin.Tests/Services/GeneralPlatformInstallerBehaviorTests.cs:13), and [`DownloadStepCapabilityTests`](../src/RomM.LaunchBoxPlugin.Tests/Services/DownloadStepCapabilityTests.cs:31).

---

### Break up the install pipeline step into dedicated strategy components

Category: Architecture

Refactor Cost: High

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs), [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs), [`src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs), platform installer registry and related tests

Description:
[`InstallContentStep`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:17) is acting as a monolithic orchestrator for Windows installs, ROM installs, installer resolution, extraction policy, canonical path reconciliation, archive deletion, staging cleanup, and fallback behavior. At over 1,000 lines, it mixes platform policy decisions with filesystem mutations and error recovery. This makes reasoning about regressions difficult and forces many unrelated behaviors to be tested through one class.

Proposed Improvement:
Split the step into focused collaborators such as a Windows install executor, ROM install executor, installer resolution service, canonical layout reconciler, and artifact cleanup/finalization service. Keep [`InstallContentStep.ExecuteAsync()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:32) as a thin coordinator.

Reasoning:
This would reduce coupling, make end-to-end behavior easier to extend, and allow platform policy changes without constantly editing one high-risk file. It also aligns better with the existing pipeline abstraction already present in [`InstallCoordinator`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs:11).

Implementation Notes:
Extract behavior behind internal interfaces first, preserve current tests, then migrate logic piece by piece with characterization tests around current archive/fallback/canonicalization scenarios.

### Consolidate filesystem relocation and canonicalization logic into a shared file operations service

Category: File System Operations

Refactor Cost: Medium

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs), [`src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs), [`src/RomM.Platforms.Abstractions/Install/GameInstallPathHelper.cs`](../src/RomM.Platforms.Abstractions/Install/GameInstallPathHelper.cs), [`src/RomM.Platforms.RomBase/RomInstallPathResolver.cs`](../src/RomM.Platforms.RomBase/RomInstallPathResolver.cs)

Description:
The codebase contains overlapping path and move/copy logic in multiple places: [`InstallContentStep`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:789), [`InstallContentRelocator`](../src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs:9), [`GameInstallPathHelper`](../src/RomM.Platforms.Abstractions/Install/GameInstallPathHelper.cs:7), and [`RomInstallPathResolver`](../src/RomM.Platforms.RomBase/RomInstallPathResolver.cs:8). There are repeated implementations for same-volume move vs copy/delete, directory cleanup, sanitization, and canonical target calculation.

Proposed Improvement:
Introduce a shared internal filesystem/path service that owns canonical game directory resolution, same-volume move/copy semantics, safe overwrite rules, and cleanup behavior. Make both plugin pipeline code and platform libraries depend on that abstraction instead of maintaining separate helpers.

Reasoning:
This removes duplicated logic that can drift over time and lowers the risk of subtle differences in install layout behavior across platforms. It also makes future changes to path rules testable in one place.

Implementation Notes:
Start by moving low-level helpers such as same-volume move/copy and directory cleanup into one utility, then centralize canonical directory calculations and remove duplicate sanitization routines.

### Introduce explicit install policy objects instead of scattered mapping string checks

Category: Configuration Handling

Refactor Cost: Medium

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs), [`src/RomM.Platforms.General/GeneralPlatformInstaller.cs`](../src/RomM.Platforms.General/GeneralPlatformInstaller.cs), [`src/RomM.LaunchBoxPlugin/Services/DownloadService.cs`](../src/RomM.LaunchBoxPlugin/Services/DownloadService.cs), mapping models/stores

Description:
Installation behavior is driven by many raw string and boolean checks such as `ArchiveHandlingMode`, `RomArchivePolicy`, `InstallLayoutMode`, `ExtractAfterDownload`, and platform-specific preserve rules inside [`InstallContentStep`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:219) and [`GeneralPlatformInstaller`](../src/RomM.Platforms.General/GeneralPlatformInstaller.cs:226). The policy logic is spread across modules instead of being normalized once.

Proposed Improvement:
Create strongly typed install policy objects derived from mappings, with resolved defaults and compatibility normalization. Have the pipeline and installers consume that policy object instead of reinterpreting raw mapping fields independently.

Reasoning:
This would reduce configuration ambiguity, eliminate duplicated defaulting behavior, and make it easier to audit how a mapping affects install behavior end-to-end.

Implementation Notes:
Use parsing/normalization close to settings load or pipeline context creation; retain backward compatibility by mapping legacy booleans into the new policy model.

### Replace ad hoc platform matching with a scored resolution model and cached metadata index

Category: Architecture

Refactor Cost: Medium

Benefit: High

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs), [`src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerRegistry.cs`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerRegistry.cs), platform metadata interfaces

Description:
Installer lookup currently mixes direct key lookup, normalized display-name matching, alias scanning, and supported platform id matching inside [`ResolveInstallerKey()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:952). This logic lives in the pipeline step instead of the registry and is recomputed per install.

Proposed Improvement:
Move resolution logic into [`PlatformInstallerRegistry`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerRegistry.cs:10) and build indexed metadata structures at load time for direct ids, aliases, normalized names, and fallback preference. Return a scored result object that includes why a match was chosen.

Reasoning:
A central resolver would make plugin/platform handling more deterministic, remove lookup duplication from the install path, and improve diagnosability when fallback installers are selected.

Implementation Notes:
Keep the current resolution order initially, but represent it as explicit scoring rules and expose diagnostics for logging/UI troubleshooting.

### Remove mixed persistence responsibilities between JSON settings and SQLite mapping storage

Category: Maintainability

Refactor Cost: High

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs), [`src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs), [`src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs)

Description:
Platform mappings and related configuration appear in both JSON-driven [`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:217) and SQLite-backed [`PlatformMappingStore`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs:26), while [`InstallStateService.InitializeAsync()`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:50) also creates mapping tables. This creates unclear ownership of configuration data and increases migration complexity.

Proposed Improvement:
Choose a single source of truth for structured platform mapping state—preferably SQLite if transactional updates and schema evolution are needed—and make [`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:17) responsible only for small plugin settings and credentials.

Reasoning:
The current split complicates reasoning about defaults, migration, and test setup. A single owner for mappings would simplify data flow and reduce the chance of stale or diverging configuration behavior.

Implementation Notes:
Add an application-level configuration repository abstraction first, then hide both JSON and SQLite details behind it before consolidating storage.

### Decompose SQLite schema initialization and migrations into versioned migrations

Category: Reliability

Refactor Cost: Medium

Benefit: High

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs), [`src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs)

Description:
[`InstallStateService.InitializeAsync()`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:50) contains a large inline schema definition plus repeated PRAGMA checks and `ALTER TABLE` calls for individual columns. This is difficult to audit, hard to extend safely, and couples startup behavior to schema history.

Proposed Improvement:
Introduce versioned migrations with explicit migration classes or scripts, and keep schema bootstrap separate from operational service code.

Reasoning:
Versioned migrations improve startup reliability, simplify schema evolution, and make database changes visible and testable rather than hidden in one large initialization method.

Implementation Notes:
Begin by extracting the current schema and incremental alterations into named migration steps while preserving the existing database filename and compatibility markers.

### Stop synchronously blocking on async persistence inside the install pipeline

Category: Concurrency

Refactor Cost: Medium

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs), [`src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/PersistStateStep.cs`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/PersistStateStep.cs), [`src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs)

Description:
[`PersistInstallState()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs:226) calls [`UpsertStateAsync()`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:673) via `.GetAwaiter().GetResult()`. This introduces avoidable sync-over-async behavior in a workflow that otherwise already runs asynchronously.

Proposed Improvement:
Make pipeline state persistence fully async and await it explicitly. If failure isolation is needed, return structured persistence failures rather than blocking and swallowing exceptions.

Reasoning:
Removing sync-over-async reduces deadlock risk, improves consistency with the rest of the concurrency model, and makes install-state failures easier to reason about during cancellation or host shutdown.

Implementation Notes:
Promote persistence methods to async helpers on the coordinator and keep exception handling local to awaited calls.

### Harden background task orchestration and eliminate fire-and-forget service calls where possible

Category: Concurrency

Refactor Cost: Medium

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:73), [`src/RomM.LaunchBoxPlugin/Services/ImportService.cs`](../src/RomM.LaunchBoxPlugin/Services/ImportService.cs:944), legacy delete/install services, audit service

Description:
Several operations are started without structured supervision, including startup calls in [`PluginEntry.Initialize()`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:73) and various `Task.Run` usages found in [`ImportService`](../src/RomM.LaunchBoxPlugin/Services/ImportService.cs:944), [`RomMAuditService`](../src/RomM.LaunchBoxPlugin/Services/RomMAuditService.cs:164), and legacy services. This makes task lifetime, exception observation, and cancellation behavior inconsistent.

Proposed Improvement:
Adopt a small background task runner abstraction that records task purpose, logs completion/failure uniformly, and supports cancellation or shutdown coordination.

Reasoning:
This improves operational reliability and observability while reducing the chance of silent background failures, especially during plugin initialization and asynchronous cleanup flows.

Implementation Notes:
Start with startup tasks and fire-and-forget calls that already lack return channels; add operation ids and unified exception handling.

### Streamline HTTP client lifecycle and isolate TLS behavior per client instance

Category: Reliability

Refactor Cost: Medium

Benefit: High

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/RommClient.cs`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs), [`src/RomM.LaunchBoxPlugin/Services/Auth/AuthService.cs`](../src/RomM.LaunchBoxPlugin/Services/Auth/AuthService.cs)

Description:
[`RommClient`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:22) mixes a shared static [`HttpClient`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:27), mutable timeout configuration via `_sharedTimeoutSeconds`, per-instance authorization headers, and a separate replacement client when invalid TLS is enabled. This creates inconsistent ownership and makes behavior dependent on construction order.

Proposed Improvement:
Use a dedicated client factory that builds immutable client instances from settings, with explicit handler policies for TLS validation, timeout, and authentication. Avoid mutating shared client defaults per consumer.

Reasoning:
This reduces cross-request interference, clarifies security behavior, and makes testability stronger for connection/auth flows.

Implementation Notes:
An internal client factory is enough; a full DI container is not required.

### Stop buffering entire downloads into memory when disk streaming is already available

Category: Performance

Refactor Cost: Low

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/RommClient.cs`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs), [`src/RomM.LaunchBoxPlugin/Services/ImportService.cs`](../src/RomM.LaunchBoxPlugin/Services/ImportService.cs)

Description:
[`RommClient`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:187), [`DownloadMediaAsync()`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:233), and save download methods still materialize entire responses as byte arrays in several paths, even though ROM downloads already have a streaming-to-file implementation in [`DownloadRomContentToFileAsync()`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:445).

Proposed Improvement:
Extend streaming patterns to save/media transfers and to any large payload path still returning `byte[]`, especially when the immediate consumer writes to disk.

Reasoning:
This reduces peak memory usage and improves scalability for large media/save payloads without changing user-visible behavior.

Implementation Notes:
Add overloads that stream to a target file/stream first, then migrate callers that simply persist bytes to disk.

### Improve API error context and preserve response details in domain exceptions

Category: Error Handling

Refactor Cost: Low

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/RommClient.cs`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs), UI/services consuming [`RommApiException`](../src/RomM.LaunchBoxPlugin/Services/RommApiException.cs:19)

Description:
[`EnsureSuccessAsync()`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:552) classifies HTTP status codes, but for most categories it discards request-specific context such as endpoint, status body, and retry guidance. This limits debugging of field compatibility problems and server-side contract changes.

Proposed Improvement:
Enrich [`RommApiException`](../src/RomM.LaunchBoxPlugin/Services/RommApiException.cs:19) with endpoint, status code, sanitized response body excerpt, and optionally retryable metadata.

Reasoning:
Better domain exceptions improve UI messaging, logging quality, and supportability without changing the outward control flow model.

Implementation Notes:
Keep bodies truncated and sanitized to avoid leaking credentials or excessive payload content.

### Make logging structured and centralized rather than format-string driven

Category: Logging

Refactor Cost: Medium

Benefit: High

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs`](../src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs), [`src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs`](../src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs), many service classes

Description:
Although [`LoggingService`](../src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs:10) supports properties, much of the code still writes interpolated free-form strings. [`FileLogSink.FormatMessage()`](../src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs:93) also emits property values verbatim and uses plain text formatting. This reduces consistency and makes correlation/search harder.

Proposed Improvement:
Define standard event names and property keys for installs, downloads, extraction, plugin loading, and persistence. Prefer structured logging calls throughout core flows and consider a machine-parseable sink format such as JSON lines.

Reasoning:
This significantly improves operational visibility, especially around race conditions, fallback installer selection, and cleanup failures.

Implementation Notes:
Focus first on install/download flows and startup lifecycle events where most diagnostics matter.

### Sanitize logged paths and values consistently at sink boundaries

Category: Security

Refactor Cost: Low

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs`](../src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs), [`src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs`](../src/RomM.LaunchBoxPlugin/Services/Logging/LoggingService.cs)

Description:
Some services sanitize paths and URLs manually, but [`FileLogSink.FormatMessage()`](../src/RomM.LaunchBoxPlugin/Services/Logging/FileLogSink.cs:93) writes exception strings and property values as-is. That creates a risk of leaking unsanitized filesystem paths or server details whenever callers forget to sanitize before logging.

Proposed Improvement:
Introduce optional sink-level sanitization for known property keys and exception/path formatting, so redaction is not left entirely to each caller.

Reasoning:
Centralized sanitization reduces accidental disclosure risk and creates more consistent log hygiene across a large codebase.

Implementation Notes:
Keep caller-level sanitization for custom messages, but add defensive redaction in the sink for common sensitive patterns.

### Simplify settings persistence APIs and remove repetitive load-modify-save patterns

Category: Maintainability

Refactor Cost: Medium

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs)

Description:
[`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:17) contains many methods that repeat the same sequence: `LoadInternal()`, mutate a list/array, apply defaults, save, and refresh cache. This increases code size and the chance of inconsistent behavior when adding new settings mutations.

Proposed Improvement:
Refactor around a small transactional update helper such as `UpdateSettings(Func<PluginSettings, PluginSettings>)` or a mutation callback that standardizes save/cache/default behavior.

Reasoning:
This lowers maintenance cost, makes settings changes easier to audit, and reduces the risk of future mutation paths bypassing defaults or cache refresh.

Implementation Notes:
Use the helper internally first without changing the public surface area.

### Revisit file-based settings caching and static credential cache behavior

Category: Reliability

Refactor Cost: Medium

Benefit: Medium

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs)

Description:
[`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:19) uses a short-lived cache for settings and a static credential cache shared across all instances. This can create stale reads or cross-instance interactions, especially because the plugin frequently constructs new managers/clients in [`PluginEntry`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:112).

Proposed Improvement:
Make cache ownership instance-scoped or move it behind a singleton configuration repository with explicit invalidation semantics.

Reasoning:
This would make configuration behavior more predictable and reduce hidden coupling between unrelated service instances.

Implementation Notes:
Audit all places that construct new [`SettingsManager`](../src/RomM.LaunchBoxPlugin/Services/Settings/SettingsManager.cs:33) instances before changing cache lifetime assumptions.

### Replace direct reflection-based plugin construction with a constrained activation model

Category: Security

Refactor Cost: Medium

Benefit: Medium

Risk: Medium

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerLoader.cs`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerLoader.cs)

Description:
[`PlatformInstallerLoader`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerLoader.cs:59) loads all DLLs from a folder and instantiates any non-abstract type assignable to [`IPlatformInstaller`](../src/RomM.Platforms.Abstractions/IPlatformInstaller.cs). There is little validation beyond duplicate keys and constructor success.

Proposed Improvement:
Restrict plugin discovery through explicit assembly naming rules, signed/manifested plugin metadata, or a whitelist/manifest produced by the build pipeline. Also validate metadata before activation.

Reasoning:
This reduces brittleness and the security risk of arbitrary DLL placement in the platforms directory, while also improving diagnostics for malformed plugins.

Implementation Notes:
The repository already has manifest files and build scripts; leverage those to formalize plugin registration.

### Consolidate duplicated platform mapping SQL into reusable command builders or repositories

Category: Code Quality

Refactor Cost: Medium

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs), [`src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs)

Description:
[`PlatformMappingStore`](../src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs:26) contains large repeated column lists for queries and upserts. Similar schema awareness also exists in [`InstallStateService`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:63). This is error-prone when fields change.

Proposed Improvement:
Extract reusable SQL fragments/parameter mappers or move to a lightweight repository mapper abstraction for mapping entities to SQLite commands.

Reasoning:
Reducing repeated schema declarations lowers maintenance overhead and decreases the chance of incomplete updates when fields are added.

Implementation Notes:
Even simple internal constants plus shared parameter binding helpers would materially improve maintainability.

### Expand tests around concurrency, persistence failures, and network edge cases

Category: Testing

Refactor Cost: Medium

Benefit: High

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin.Tests`](../src/RomM.LaunchBoxPlugin.Tests), especially tests for [`InstallStateService`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs), [`RommClient`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs), [`InstallCoordinator`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs), [`PlatformInstallerLoader`](../src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallerLoader.cs)

Description:
The test suite covers many installer behaviors well, including fallback and canonicalization scenarios, but there is comparatively less visible coverage for concurrent SQLite access, HTTP error classification, TLS/client-construction behavior, plugin loading edge cases, and failure handling around blocking persistence.

Proposed Improvement:
Add focused unit/integration tests for concurrent install-state operations, plugin loading failures, API exception enrichment, startup background tasks, and cleanup failure paths.

Reasoning:
These are the areas most likely to cause operational regressions and the least likely to be caught by platform-specific installer tests.

Implementation Notes:
Characterization tests around current failure behavior should be added before refactors in the persistence and HTTP layers.

### Clarify service ownership and reduce repeated manual composition in plugin bootstrap

Category: Developer Experience

Refactor Cost: Medium

Benefit: Medium

Risk: Low

Files Affected: [`src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs)

Description:
[`PluginEntry`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:18) manually wires logging, settings, install state, installer loading, import, download, and background connection behaviors. Factory methods such as [`CreateImportService()`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:112) and [`CreateDownloadService()`](../src/RomM.LaunchBoxPlugin/Plugin/PluginEntry.cs:124) recreate dependency graphs ad hoc.

Proposed Improvement:
Introduce a lightweight composition root with explicit singleton/transient ownership and shared factories for HTTP clients, repositories, and install services.

Reasoning:
This keeps the existing “no DI container” design decision while making lifecycle and dependency ownership clearer for future contributors.

Implementation Notes:
An internal service graph object or factory registry would be sufficient; a third-party DI framework is not required.

## High Priority Improvements

1. Break up [`InstallContentStep`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:17) into dedicated strategy/services because it is the highest concentration of install, platform, and filesystem complexity.
2. Consolidate filesystem relocation/canonicalization logic across [`InstallContentStep`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/Steps/InstallContentStep.cs:686), [`InstallContentRelocator`](../src/RomM.LaunchBoxPlugin/Services/Install/InstallContentRelocator.cs:9), and [`GameInstallPathHelper`](../src/RomM.Platforms.Abstractions/Install/GameInstallPathHelper.cs:7) to reduce regression risk.
3. Unify configuration ownership for platform mappings and move toward typed install policy objects instead of scattered string/boolean interpretation in pipeline and installer code.
4. Refactor [`InstallStateService.InitializeAsync()`](../src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs:50) into versioned migrations and remove sync-over-async persistence in [`InstallCoordinator.PersistInstallState()`](../src/RomM.LaunchBoxPlugin/Services/Install/Pipeline/InstallCoordinator.cs:226).
5. Rework [`RommClient`](../src/RomM.LaunchBoxPlugin/Services/RommClient.cs:22) client lifecycle and auth/TLS handling to avoid shared mutable HTTP behavior and improve reliability/security.
6. Add stronger structured logging and background task supervision around startup, install, and cleanup paths to improve diagnosis of intermittent operational failures.
