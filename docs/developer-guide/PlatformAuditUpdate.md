# Platform Audit & Update System

## High-level system design

```text
Tools Menu -> RomMAuditWindow -> RomMAuditViewModel
                                   |  (options, progress, results)
                                   v
                           RomMAuditService
                                   |  (controlled parallelism, cancellation)
                                   v
                           ImportService (matching engine)
                                   |  (match index, evaluate)
                                   v
                           InstallStateService (persist RomM ID)
```

## Data flow

1. User selects platform + options in `RomMAuditWindow`.
2. `RomMAuditViewModel` creates `RomMAuditRequest` and starts async execution.
3. `RomMAuditService` loads LaunchBox games for the platform, builds a match index, and fetches RomM candidates once.
4. Each game is processed with controlled parallelism, reports progress, and logs a single outcome.
5. Results are summarized and displayed in the UI.

## Concurrency model

- `RomMAuditService` uses `SemaphoreSlim` to cap parallelism.
- A static `AuditGuard` prevents concurrent audits.
- Cancellation propagates via `CancellationToken`.

## Error handling approach

- Per-game try/catch so failures do not halt the audit.
- Audit-level try/catch for recoverable errors (UI receives summary).
- Any exception logs a structured event with correlation id.

## Logging design

- Correlation ID created per audit session.
- Events include:
  - `AuditStarted` (Platform, TotalGames)
  - `GameAuditStarted` (GameTitle, Platform)
  - `GameMatchFound` (OldRomMId, NewRomMId)
  - `GameMatchUnchanged`
  - `GameMatchNotFound`
  - `GameAuditFailed` (Exception)
  - `AuditCompleted` (Duration, UpdatedCount, FailedCount)
  - `AuditCancelled`

## Code skeletons

- `RomMbox.Models.Audit.RomMAuditModels` (options, request, results)
- `RomMbox.Services.RomMAuditService` (execution engine)
- `RomMbox.UI.ViewModels.RomMAuditViewModel` (MVVM)
- `RomMbox.UI.RomMAuditWindow` (UI)

## UI layout overview

Sections:

1. Configuration
2. Summary
3. Execution Progress
4. Results Summary + Results Log

Controls:

- Start / Cancel / Close
- ProgressBar
- Live status text
- Scrollable results log

## Migration strategy

- Existing per-game update logic remains intact.
- Platform Audit & Update uses ImportService match engine as-is.
- No changes to logging registration.

## Unit testing strategy

- Add unit tests with stubbed `IRommClient` and data manager.
- Validate: options gating, cancellation, result counts, and logging.

## Performance considerations

- RomM candidate list is loaded once per platform.
- Controlled parallelism to avoid API overload.
- Optional API delay for throttling.
