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
                        LogStepFailed(step.Phase, result.Message);
                        _logger.Warning($"Install pipeline failed at {step.Phase}: {result.Message}");
                        return result;
                    }

                    LogStepCompleted(step.Phase, context);
                }

                progress?.Report(new InstallProgressEvent(InstallPhase.Completed, "Install completed.", 100));
                _logger.Info("InstallCompleted.");
                return InstallResult.Successful();
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Install pipeline cancelled.");
                _logger.Warning("InstallFailed: Cancelled.");
                return InstallResult.Cancelled();
            }
            catch (Exception ex)
            {
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
            switch (phase)
            {
                case InstallPhase.Downloading:
                    _logger.Info("DownloadStarted.");
                    break;
                case InstallPhase.Installing:
                    _logger.Info("InstallStarted.");
                    break;
            }
        }

        private void LogStepCompleted(InstallPhase phase, InstallContext context)
        {
            switch (phase)
            {
                case InstallPhase.Downloading:
                    _logger.Info("DownloadCompleted.");
                    if (!string.IsNullOrWhiteSpace(context?.ExtractedPath))
                    {
                        _logger.Info($"ExtractionComplete. ExtractedPath='{context.ExtractedPath}'.");
                    }
                    break;
                case InstallPhase.Installing:
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
    }
}
