using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallCoordinator
    {
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly InstallStateService _installStateService;
        private readonly IReadOnlyList<IInstallStep> _steps;

        public InstallCoordinator(LoggingService logger, SettingsManager settingsManager, InstallStateService installStateService, IReadOnlyList<IInstallStep> steps)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _installStateService = installStateService;
            _steps = steps ?? Array.Empty<IInstallStep>();
        }

        public async Task<InstallResult> RunAsync(InstallRequest request, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return InstallResult.Failed(InstallPhase.Pending, "Install request missing.");
            }

            var operationId = Guid.NewGuid().ToString("N");
            using var operationScope = _logger.BeginOperation(operationId);
            _logger.Info($"Install pipeline started for '{request.Game?.Title ?? "<unknown>"}'.");

            var context = new InstallContext(request, _logger, _settingsManager, _installStateService);
            context.OperationId = operationId;
            context.InstallStartedUtc = DateTimeOffset.UtcNow;
            try
            {
                foreach (var step in _steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LogStepStarted(step.Phase, context);
                    progress?.Report(new InstallProgressEvent(step.Phase, $"{step.Phase}..."));
                    var result = await step.ExecuteAsync(context, progress, cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        UpdateInstallStateFailure(context, step.Phase, result.Message);
                        LogStepFailed(step.Phase, result.Message);
                        _logger.Warning($"Install pipeline failed at {step.Phase}: {result.Message}");
                        return result;
                    }

                    LogStepCompleted(step.Phase, context);
                }

                UpdateInstallStateSuccess(context);
                progress?.Report(new InstallProgressEvent(InstallPhase.Completed, "Install completed.", 100));
                _logger.Info("InstallCompleted.");
                return InstallResult.Successful();
            }
            catch (OperationCanceledException)
            {
                UpdateInstallStateCancelled(context);
                _logger.Warning("Install pipeline cancelled.");
                _logger.Warning("InstallFailed: Cancelled.");
                return InstallResult.Cancelled();
            }
            catch (Exception ex)
            {
                UpdateInstallStateException(context, ex);
                _logger.Error("Install pipeline failed.", ex);
                _logger.Error("InstallFailed: Exception.", ex);
                return InstallResult.Failed(InstallPhase.Failed, ex.Message);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(context.TempRoot))
                {
                    try
                    {
                        if (Directory.Exists(context.TempRoot))
                        {
                            Directory.Delete(context.TempRoot, recursive: true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void LogStepStarted(InstallPhase phase, InstallContext context)
        {
            UpdateInstallStatePhase(context, phase, "InProgress");
            switch (phase)
            {
                case InstallPhase.Downloading:
                    context.DownloadStartedUtc = DateTimeOffset.UtcNow;
                    _logger.Info("DownloadStarted.");
                    break;
                case InstallPhase.Extracting:
                    context.ExtractionStartedUtc = DateTimeOffset.UtcNow;
                    _logger.Info("ExtractionStarted.");
                    break;
                case InstallPhase.Installing:
                    context.ExtractionCompletedUtc = context.ExtractionCompletedUtc ?? DateTimeOffset.UtcNow;
                    _logger.Info("InstallStarted.");
                    break;
            }
        }

        private void LogStepCompleted(InstallPhase phase, InstallContext context)
        {
            switch (phase)
            {
                case InstallPhase.Downloading:
                    context.DownloadCompletedUtc = DateTimeOffset.UtcNow;
                    _logger.Info("DownloadCompleted.");
                    if (!string.IsNullOrWhiteSpace(context?.ExtractedPath))
                    {
                        _logger.Info($"ExtractionComplete. ExtractedPath='{context.ExtractedPath}'.");
                    }
                    break;
                case InstallPhase.Installing:
                    context.InstallCompletedUtc = DateTimeOffset.UtcNow;
                    _logger.Info("InstallStepCompleted.");
                    break;
            }
        }

        private void LogStepFailed(InstallPhase phase, string message)
        {
            if (phase == InstallPhase.Installing)
            {
                _logger.Warning($"InstallFailed. Message='{message}'.");
            }
        }

        private void UpdateInstallStatePhase(InstallContext context, InstallPhase phase, string status)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return;
            }

            context.InstallStateSnapshot.InstallStatus = status;
            context.InstallStateSnapshot.InstallPhase = phase.ToString();
            context.InstallStateSnapshot.LastOperationId = context.OperationId;
            context.InstallStateSnapshot.LastAttemptUtc = context.InstallStateSnapshot.LastAttemptUtc ?? context.InstallStartedUtc;
        }

        private void UpdateInstallStateFailure(InstallContext context, InstallPhase phase, string message)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return;
            }

            context.InstallStateSnapshot.InstallStatus = "Failed";
            context.InstallStateSnapshot.InstallPhase = phase.ToString();
            context.InstallStateSnapshot.LastError = message;
            context.InstallStateSnapshot.LastOperationId = context.OperationId;
            context.InstallStateSnapshot.LastAttemptUtc = context.InstallStateSnapshot.LastAttemptUtc ?? context.InstallStartedUtc;
            context.InstallStateSnapshot.IsInstalled = false;
            context.InstallStateSnapshot.InstalledUtc = null;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;
            PersistInstallState(context);
            LogTimingMetrics(context, phase, "Failed");
        }

        private void UpdateInstallStateCancelled(InstallContext context)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return;
            }

            context.InstallStateSnapshot.InstallStatus = "Cancelled";
            context.InstallStateSnapshot.InstallPhase = InstallPhase.Cancelled.ToString();
            context.InstallStateSnapshot.LastOperationId = context.OperationId;
            context.InstallStateSnapshot.LastAttemptUtc = context.InstallStateSnapshot.LastAttemptUtc ?? context.InstallStartedUtc;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;
            PersistInstallState(context);
            LogTimingMetrics(context, InstallPhase.Cancelled, "Cancelled");
        }

        private void UpdateInstallStateException(InstallContext context, Exception ex)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return;
            }

            context.InstallStateSnapshot.InstallStatus = "Failed";
            context.InstallStateSnapshot.InstallPhase = InstallPhase.Failed.ToString();
            context.InstallStateSnapshot.LastError = ex?.Message ?? "Install failed.";
            context.InstallStateSnapshot.LastOperationId = context.OperationId;
            context.InstallStateSnapshot.LastAttemptUtc = context.InstallStateSnapshot.LastAttemptUtc ?? context.InstallStartedUtc;
            context.InstallStateSnapshot.IsInstalled = false;
            context.InstallStateSnapshot.InstalledUtc = null;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;
            PersistInstallState(context);
            LogTimingMetrics(context, InstallPhase.Failed, "Failed");
        }

        private void UpdateInstallStateSuccess(InstallContext context)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return;
            }

            context.InstallStateSnapshot.InstallStatus = "Completed";
            context.InstallStateSnapshot.InstallPhase = InstallPhase.Completed.ToString();
            context.InstallStateSnapshot.LastError = string.Empty;
            context.InstallStateSnapshot.LastOperationId = context.OperationId;
            context.InstallStateSnapshot.LastCompletedUtc = DateTimeOffset.UtcNow;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;
            LogTimingMetrics(context, InstallPhase.Completed, "Completed");
        }

        private void PersistInstallState(InstallContext context)
        {
            try
            {
                var snapshot = context?.InstallStateSnapshot;
                if (snapshot == null)
                {
                    return;
                }

                context.InstallStateService
                    .UpsertStateAsync(snapshot.ToInstallState(), CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to persist install state snapshot: {ex.Message}");
            }
        }

        private void LogTimingMetrics(InstallContext context, InstallPhase phase, string status)
        {
            if (context == null)
            {
                return;
            }

            var downloadMs = GetDurationMs(context.DownloadStartedUtc, context.DownloadCompletedUtc);
            var extractionMs = GetDurationMs(context.ExtractionStartedUtc, context.ExtractionCompletedUtc);
            var installMs = GetDurationMs(context.InstallStartedUtc, context.InstallCompletedUtc);
            _logger?.Info($"InstallTiming | Status={status}, Phase={phase}, DownloadMs={downloadMs}, ExtractionMs={extractionMs}, InstallMs={installMs}");
        }

        private static long? GetDurationMs(DateTimeOffset? start, DateTimeOffset? end)
        {
            if (!start.HasValue || !end.HasValue)
            {
                return null;
            }

            var duration = end.Value - start.Value;
            return (long)Math.Max(0, duration.TotalMilliseconds);
        }
    }
}
