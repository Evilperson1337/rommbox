using System;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallContext
    {
        public InstallContext(InstallRequest request, LoggingService logger, SettingsManager settingsManager, InstallStateService installStateService)
        {
            Request = request;
            Logger = logger;
            SettingsManager = settingsManager;
            InstallStateService = installStateService;
            Game = request.Game;
            DataManager = request.DataManager;
        }

        public InstallRequest Request { get; }
        public LoggingService Logger { get; }
        public SettingsManager SettingsManager { get; }
        public InstallStateService InstallStateService { get; }
        public IGame Game { get; }
        public IDataManager DataManager { get; }
        public RommRom RommDetails { get; set; }
        public PlatformMapping PlatformMapping { get; set; }
        public string InstallDirectory { get; set; }
        public string DownloadDirectory { get; set; }
        public string ArchivePath { get; set; }
        public string ExtractedPath { get; set; }
        public string TempRoot { get; set; }
        public string InstalledExecutablePath { get; set; }
        public string[] InstallerArguments { get; set; }
        public InstallStateSnapshot InstallStateSnapshot { get; set; }
        public string OperationId { get; set; }
        public DateTimeOffset InstallStartedUtc { get; set; }
        public DateTimeOffset? DownloadStartedUtc { get; set; }
        public DateTimeOffset? DownloadCompletedUtc { get; set; }
        public DateTimeOffset? ExtractionStartedUtc { get; set; }
        public DateTimeOffset? ExtractionCompletedUtc { get; set; }
        public DateTimeOffset? InstallCompletedUtc { get; set; }
    }
}
