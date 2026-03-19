using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using RomMbox.Models;
using RomMbox.Plugin;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    internal sealed class RommAdditionalApplicationLaunchService
    {
        private readonly LoggingService _logger;
        private readonly InstallStateService _installStateService;

        public RommAdditionalApplicationLaunchService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
        }

        public RommAdditionalApplicationContext GetContext(IGame game)
        {
            if (game == null || !IsWindowsGame(game))
            {
                return RommAdditionalApplicationContext.Unavailable(game, null, null);
            }

            InstallState state = null;
            try
            {
                state = _installStateService?.GetStateAsync(game.Id, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to resolve RomM additional application state for '{game?.Title}'. {ex.Message}");
            }

            var additionalAppId = state?.RommAdditionalAppId;
            var additionalApplication = (game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                .FirstOrDefault(app => RommAdditionalApplicationService.IsRommAdditionalApplication(app, game.Id, additionalAppId));

            var hasRomMState = state != null
                && (!string.IsNullOrWhiteSpace(state.RommRomId)
                    || !string.IsNullOrWhiteSpace(state.RommAdditionalAppId)
                    || string.Equals(state.RommMergedBaseGameId, game.Id, StringComparison.OrdinalIgnoreCase));

            var available = additionalApplication != null || hasRomMState;
            var installed = IsInstalled(state, additionalApplication);

            return new RommAdditionalApplicationContext(game, state, additionalApplication, available, installed);
        }

        public bool Play(IGame game)
        {
            var context = GetContext(game);
            if (!context.IsAvailable || !context.IsInstalled || context.AdditionalApplication == null)
            {
                _logger?.Warning($"RomM additional application play skipped for '{game?.Title}'. Available={context.IsAvailable}, Installed={context.IsInstalled}, HasAdditionalApplication={context.AdditionalApplication != null}.");
                ShowInfoDialog("RomM", "No installed RomM version is available for this game.");
                return false;
            }

            try
            {
                var mainViewModel = PluginHelper.LaunchBoxMainViewModel;
                if (mainViewModel != null)
                {
                    _logger?.Info($"Launching RomM additional application through LaunchBox for '{game?.Title}'. AdditionalAppId='{context.AdditionalApplication.Id}'.");
                    mainViewModel.PlayGame(game, context.AdditionalApplication, null, null);
                    return true;
                }

                var error = context.AdditionalApplication.Launch(game);
                if (string.IsNullOrWhiteSpace(error))
                {
                    _logger?.Info($"Launching RomM additional application directly for '{game?.Title}'. AdditionalAppId='{context.AdditionalApplication.Id}'.");
                    return true;
                }

                _logger?.Warning($"RomM additional application launch failed for '{game?.Title}'. {error}");
                ShowInfoDialog("RomM", error);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error("RomM additional application play failed.", ex);
                ShowInfoDialog("RomM", "Unable to launch the installed RomM version.");
                return false;
            }
        }

        internal static bool IsWindowsGame(IGame game)
        {
            return game != null
                && string.Equals(game.Platform ?? string.Empty, "Windows", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInstalled(InstallState state, IAdditionalApplication additionalApplication)
        {
            if (state?.IsInstalled == true && HasLaunchablePath(state?.RommLaunchPath ?? state?.InstalledPath))
            {
                return true;
            }

            return additionalApplication?.Installed == true
                && HasLaunchablePath(additionalApplication.ApplicationPath);
        }

        private static bool HasLaunchablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var resolvedPath = path;
                if (!Path.IsPathRooted(resolvedPath))
                {
                    var root = PluginPaths.GetLaunchBoxRootDirectory();
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        resolvedPath = Path.Combine(root, resolvedPath);
                    }
                }

                return File.Exists(resolvedPath) || Directory.Exists(resolvedPath);
            }
            catch
            {
                return false;
            }
        }

        private static void ShowInfoDialog(string title, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            dispatcher.Invoke(() => MessageBox.Show(Application.Current?.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information));
        }
    }

    internal sealed class RommAdditionalApplicationContext
    {
        public RommAdditionalApplicationContext(IGame game, InstallState state, IAdditionalApplication additionalApplication, bool isAvailable, bool isInstalled)
        {
            Game = game;
            State = state;
            AdditionalApplication = additionalApplication;
            IsAvailable = isAvailable;
            IsInstalled = isInstalled;
        }

        public IGame Game { get; }
        public InstallState State { get; }
        public IAdditionalApplication AdditionalApplication { get; }
        public bool IsAvailable { get; }
        public bool IsInstalled { get; }

        public static RommAdditionalApplicationContext Unavailable(IGame game, InstallState state, IAdditionalApplication additionalApplication)
        {
            return new RommAdditionalApplicationContext(game, state, additionalApplication, false, false);
        }
    }
}
