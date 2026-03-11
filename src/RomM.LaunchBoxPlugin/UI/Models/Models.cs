using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Models;

/// <summary>
/// Represents a platform mapping row used in the platforms UI.
/// </summary>
public sealed class PlatformMapping : ObservableObject
{
    private string _rommPlatformId = "";
    /// <summary>
    /// Gets or sets the RomM platform identifier.
    /// </summary>
    public string RommPlatformId { get => _rommPlatformId; set => SetProperty(ref _rommPlatformId, value); }

    private bool _exclude;
    /// <summary>
    /// Gets or sets whether the platform is excluded from import.
    /// </summary>
    public bool Exclude { get => _exclude; set => SetProperty(ref _exclude, value); }

    private string _romMPlatform = "";
    /// <summary>
    /// Gets or sets the RomM platform display name.
    /// </summary>
    public string RomMPlatform { get => _romMPlatform; set => SetProperty(ref _romMPlatform, value); }

    private string _launchBoxPlatform = "--- Not Mapped ---";
    /// <summary>
    /// Gets or sets the LaunchBox platform name mapping.
    /// </summary>
    public string LaunchBoxPlatform { get => _launchBoxPlatform; set => SetProperty(ref _launchBoxPlatform, value); }

    private bool _disableAutoImport;
    /// <summary>
    /// Gets or sets whether auto-import should be disabled for this platform.
    /// </summary>
    public bool DisableAutoImport { get => _disableAutoImport; set => SetProperty(ref _disableAutoImport, value); }

    private InstallScenario _installScenario = InstallScenario.Basic;
    /// <summary>
    /// Gets or sets the install scenario applied to this platform.
    /// </summary>
    public InstallScenario InstallScenario { get => _installScenario; set => SetProperty(ref _installScenario, value); }

    private bool _selfContained = true;
    /// <summary>
    /// Gets or sets whether the extracted folder is self-contained.
    /// </summary>
    public bool SelfContained { get => _selfContained; set => SetProperty(ref _selfContained, value); }

    private string _targetImportFile = "";
    /// <summary>
    /// Gets or sets the target import file used after extraction or install.
    /// </summary>
    public string TargetImportFile { get => _targetImportFile; set => SetProperty(ref _targetImportFile, value); }

    private string _associatedEmulatorId = "";
    /// <summary>
    /// Gets or sets the associated emulator id.
    /// </summary>
    public string AssociatedEmulatorId { get => _associatedEmulatorId; set => SetProperty(ref _associatedEmulatorId, value); }

    private string _emulatorCoreId = "";
    /// <summary>
    /// Gets or sets the emulator core id.
    /// </summary>
    public string EmulatorCoreId { get => _emulatorCoreId; set => SetProperty(ref _emulatorCoreId, value); }

    private string _emulatorCoreName = "";
    /// <summary>
    /// Gets or sets the emulator core display name.
    /// </summary>
    public string EmulatorCoreName { get => _emulatorCoreName; set => SetProperty(ref _emulatorCoreName, value); }

    private string _emulatorCorePath = "";
    /// <summary>
    /// Gets or sets the emulator core path.
    /// </summary>
    public string EmulatorCorePath { get => _emulatorCorePath; set => SetProperty(ref _emulatorCorePath, value); }

    private string _emulatorLaunchArgs = "";
    /// <summary>
    /// Gets or sets the emulator launch arguments.
    /// </summary>
    public string EmulatorLaunchArgs { get => _emulatorLaunchArgs; set => SetProperty(ref _emulatorLaunchArgs, value); }

    private string _romInstallRoot = "";
    /// <summary>
    /// Gets or sets the ROM install root override.
    /// </summary>
    public string RomInstallRoot { get => _romInstallRoot; set => SetProperty(ref _romInstallRoot, value); }

    private string _romArchivePolicy = "";
    /// <summary>
    /// Gets or sets the ROM archive policy.
    /// </summary>
    public string RomArchivePolicy { get => _romArchivePolicy; set => SetProperty(ref _romArchivePolicy, value); }

    private string _pluginKey = "";
    /// <summary>
    /// Gets or sets the resolved plugin key for this platform.
    /// </summary>
    public string PluginKey { get => _pluginKey; set => SetProperty(ref _pluginKey, value); }

    private string _pluginSettings = "";
    /// <summary>
    /// Gets or sets the serialized plugin settings payload.
    /// </summary>
    public string PluginSettings { get => _pluginSettings; set => SetProperty(ref _pluginSettings, value); }

    private string _supportedFileTypes = "";
    /// <summary>
    /// Gets or sets supported file types for the general fallback installer.
    /// </summary>
    public string SupportedFileTypes { get => _supportedFileTypes; set => SetProperty(ref _supportedFileTypes, value); }

    private string _preferredLaunchExtensions = "";
    /// <summary>
    /// Gets or sets preferred launch extension order for fallback installer selection.
    /// </summary>
    public string PreferredLaunchExtensions { get => _preferredLaunchExtensions; set => SetProperty(ref _preferredLaunchExtensions, value); }

    private string _archiveHandlingMode = "";
    /// <summary>
    /// Gets or sets archive handling mode for general ROM installs.
    /// </summary>
    public string ArchiveHandlingMode { get => _archiveHandlingMode; set => SetProperty(ref _archiveHandlingMode, value); }

    private bool _useGameSubdirectory = true;
    /// <summary>
    /// Gets or sets whether fallback installs use a per-game subdirectory.
    /// </summary>
    public bool UseGameSubdirectory { get => _useGameSubdirectory; set => SetProperty(ref _useGameSubdirectory, value); }

    private bool _installAllMatchingFiles = true;
    /// <summary>
    /// Gets or sets whether fallback installs include all discovered matching files.
    /// </summary>
    public bool InstallAllMatchingFiles { get => _installAllMatchingFiles; set => SetProperty(ref _installAllMatchingFiles, value); }

    private bool _installFromArchiveDirectly;
    /// <summary>
    /// Gets or sets whether fallback installer may launch directly from supported archives.
    /// </summary>
    public bool InstallFromArchiveDirectly { get => _installFromArchiveDirectly; set => SetProperty(ref _installFromArchiveDirectly, value); }

    private string _installLayoutMode = "";
    /// <summary>
    /// Gets or sets install layout mode for general ROM installs.
    /// </summary>
    public string InstallLayoutMode { get => _installLayoutMode; set => SetProperty(ref _installLayoutMode, value); }

    private string _artifactSelectionMode = "";
    /// <summary>
    /// Gets or sets artifact selection mode when multiple candidates are available.
    /// </summary>
    public string ArtifactSelectionMode { get => _artifactSelectionMode; set => SetProperty(ref _artifactSelectionMode, value); }

    private bool _useGeneralFallbackInstaller;
    /// <summary>
    /// Gets or sets whether this mapping should use the general fallback installer.
    /// </summary>
    public bool UseGeneralFallbackInstaller { get => _useGeneralFallbackInstaller; set => SetProperty(ref _useGeneralFallbackInstaller, value); }

    private string _installerSilentArgs = "";
    /// <summary>
    /// Gets or sets installer silent arguments when using installer-based packages.
    /// </summary>
    public string InstallerSilentArgs { get => _installerSilentArgs; set => SetProperty(ref _installerSilentArgs, value); }

    private InstallerMode _installerMode = InstallerMode.Manual;
    /// <summary>
    /// Gets or sets the installer mode used for this platform.
    /// </summary>
    public InstallerMode InstallerMode { get => _installerMode; set => SetProperty(ref _installerMode, value); }

    private string _musicRootPath = "";
    /// <summary>
    /// Gets or sets the root path used for soundtrack installs.
    /// </summary>
    public string MusicRootPath { get => _musicRootPath; set => SetProperty(ref _musicRootPath, value); }

    private OptionalContentLocation _ostInstallLocation = OptionalContentLocation.Centralized;
    /// <summary>
    /// Gets or sets the install location for OST content.
    /// </summary>
    public OptionalContentLocation OstInstallLocation { get => _ostInstallLocation; set => SetProperty(ref _ostInstallLocation, value); }

    private bool _installOst;
    /// <summary>
    /// Gets or sets whether soundtracks should be installed.
    /// </summary>
    public bool InstallOst { get => _installOst; set => SetProperty(ref _installOst, value); }

    private string _bonusRootPath = "";
    /// <summary>
    /// Gets or sets the root path used for bonus content installs.
    /// </summary>
    public string BonusRootPath { get => _bonusRootPath; set => SetProperty(ref _bonusRootPath, value); }

    private OptionalContentLocation _bonusInstallLocation = OptionalContentLocation.Centralized;
    /// <summary>
    /// Gets or sets the install location for bonus content.
    /// </summary>
    public OptionalContentLocation BonusInstallLocation { get => _bonusInstallLocation; set => SetProperty(ref _bonusInstallLocation, value); }

    private bool _installBonus;
    /// <summary>
    /// Gets or sets whether bonus content should be installed.
    /// </summary>
    public bool InstallBonus { get => _installBonus; set => SetProperty(ref _installBonus, value); }

    private string _preReqsRootPath = "";
    /// <summary>
    /// Gets or sets the root path for prerequisites installs.
    /// </summary>
    public string PreReqsRootPath { get => _preReqsRootPath; set => SetProperty(ref _preReqsRootPath, value); }

    private bool _installPreReqs;
    /// <summary>
    /// Gets or sets whether prerequisites should be installed.
    /// </summary>
    public bool InstallPreReqs { get => _installPreReqs; set => SetProperty(ref _installPreReqs, value); }

    private bool _extractAfterDownload = false;
    /// <summary>
    /// Gets or sets whether archives should be extracted after download.
    /// </summary>
    public bool ExtractAfterDownload { get => _extractAfterDownload; set => SetProperty(ref _extractAfterDownload, value); }

    private ExtractionBehavior _extractionBehavior = ExtractionBehavior.Subfolder;
    /// <summary>
    /// Gets or sets the extraction behavior for this platform.
    /// </summary>
    public ExtractionBehavior ExtractionBehavior { get => _extractionBehavior; set => SetProperty(ref _extractionBehavior, value); }

    private string _customInstallDirectory = "";
    /// <summary>
    /// Gets or sets a custom install directory override.
    /// </summary>
    public string CustomInstallDirectory { get => _customInstallDirectory; set => SetProperty(ref _customInstallDirectory, value); }

    private string _ps3GameDirectory = "";
    /// <summary>
    /// Gets or sets the PS3 games directory override.
    /// </summary>
    public string Ps3GameDirectory { get => _ps3GameDirectory; set => SetProperty(ref _ps3GameDirectory, value); }

    private string _rpcs3ExecutablePath = "";
    /// <summary>
    /// Gets or sets the RPCS3 executable path.
    /// </summary>
    public string Rpcs3ExecutablePath { get => _rpcs3ExecutablePath; set => SetProperty(ref _rpcs3ExecutablePath, value); }

    private bool _installDlcAutomatically;
    /// <summary>
    /// Gets or sets whether DLC should be installed automatically.
    /// </summary>
    public bool InstallDlcAutomatically { get => _installDlcAutomatically; set => SetProperty(ref _installDlcAutomatically, value); }

    private bool _installUpdatesAutomatically;
    /// <summary>
    /// Gets or sets whether updates should be installed automatically.
    /// </summary>
    public bool InstallUpdatesAutomatically { get => _installUpdatesAutomatically; set => SetProperty(ref _installUpdatesAutomatically, value); }

    private string _rpcs3LicenseDirectory = "";
    /// <summary>
    /// Gets or sets the RPCS3 license directory override.
    /// </summary>
    public string Rpcs3LicenseDirectory { get => _rpcs3LicenseDirectory; set => SetProperty(ref _rpcs3LicenseDirectory, value); }

    private bool _skipRegionMismatchedDlc;
    /// <summary>
    /// Gets or sets whether region-mismatched DLC should be skipped.
    /// </summary>
    public bool SkipRegionMismatchedDlc { get => _skipRegionMismatchedDlc; set => SetProperty(ref _skipRegionMismatchedDlc, value); }

    private bool _skipUnmatchedRapFiles;
    /// <summary>
    /// Gets or sets whether unmatched RAP files should be skipped.
    /// </summary>
    public bool SkipUnmatchedRapFiles { get => _skipUnmatchedRapFiles; set => SetProperty(ref _skipUnmatchedRapFiles, value); }

    private bool _preferMetadataBasedPackageMatching;
    /// <summary>
    /// Gets or sets whether metadata-based package matching is preferred.
    /// </summary>
    public bool PreferMetadataBasedPackageMatching { get => _preferMetadataBasedPackageMatching; set => SetProperty(ref _preferMetadataBasedPackageMatching, value); }

    private string _ps4GamesDirectory = "";
    /// <summary>
    /// Gets or sets the PS4 games directory override.
    /// </summary>
    public string Ps4GamesDirectory { get => _ps4GamesDirectory; set => SetProperty(ref _ps4GamesDirectory, value); }

    private string _shadPs4ExecutablePath = "";
    /// <summary>
    /// Gets or sets the ShadPS4 executable path.
    /// </summary>
    public string ShadPs4ExecutablePath { get => _shadPs4ExecutablePath; set => SetProperty(ref _shadPs4ExecutablePath, value); }

    private string _ps4ExternalPkgExtractorPath = "";
    /// <summary>
    /// Gets or sets the optional PS4 external PKG extractor path.
    /// </summary>
    public string Ps4ExternalPkgExtractorPath { get => _ps4ExternalPkgExtractorPath; set => SetProperty(ref _ps4ExternalPkgExtractorPath, value); }

    private bool _ps4FailIfDirectPkgExtractorMissing;
    /// <summary>
    /// Gets or sets whether direct PKG install should fail if extractor configuration is missing.
    /// </summary>
    public bool Ps4FailIfDirectPkgExtractorMissing { get => _ps4FailIfDirectPkgExtractorMissing; set => SetProperty(ref _ps4FailIfDirectPkgExtractorMissing, value); }

    private string _readinessStatus = string.Empty;
    /// <summary>
    /// Gets or sets the platform readiness status.
    /// </summary>
    public string ReadinessStatus { get => _readinessStatus; set => SetProperty(ref _readinessStatus, value); }

    private string _readinessMessage = string.Empty;
    /// <summary>
    /// Gets or sets the platform readiness details.
    /// </summary>
    public string ReadinessMessage { get => _readinessMessage; set => SetProperty(ref _readinessMessage, value); }
}

/// <summary>
/// Represents a row in the import selection grid.
/// </summary>
public sealed class ImportGameRow : ObservableObject
{
    private bool _import;
    /// <summary>
    /// Gets or sets whether the game should be imported.
    /// </summary>
    public bool Import { get => _import; set => SetProperty(ref _import, value); }

    private bool _download;
    /// <summary>
    /// Gets or sets whether the game's media should be downloaded.
    /// </summary>
    public bool Download { get => _download; set => SetProperty(ref _download, value); }

    private bool _saves;
    /// <summary>
    /// Gets or sets whether save data should be imported.
    /// </summary>
    public bool Saves { get => _saves; set => SetProperty(ref _saves, value); }

    private string _name = "";
    /// <summary>
    /// Gets or sets the display name shown in the UI.
    /// </summary>
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _skipReason = "";
    /// <summary>
    /// Gets or sets the reason a row is skipped, if any.
    /// </summary>
    public string SkipReason { get => _skipReason; set => SetProperty(ref _skipReason, value); }

    private string _statusText = "";
    /// <summary>
    /// Gets or sets the status text for the import row.
    /// </summary>
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private RomMbox.Models.Import.ImportAction _action = RomMbox.Models.Import.ImportAction.Import;
    /// <summary>
    /// Gets or sets the import action applied to this game.
    /// </summary>
    public RomMbox.Models.Import.ImportAction Action { get => _action; set => SetProperty(ref _action, value); }

    /// <summary>
    /// Gets or sets whether a merge is suggested for this row.
    /// </summary>
    public bool IsMergeSuggested { get; set; }

    /// <summary>
    /// Gets or sets whether the action was overridden by the user.
    /// </summary>
    public bool IsActionUserOverride { get; set; }

    /// <summary>
    /// Gets or sets the RomM ROM associated with this row.
    /// </summary>
    internal RommRom Rom { get; set; }
}

/// <summary>
/// Represents a RomM platform option used in selectors.
/// </summary>
public sealed class RommPlatformOption
{
    /// <summary>
    /// Gets or sets the RomM platform identifier.
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// Gets or sets the display name shown in the UI.
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// Gets or sets the mapped LaunchBox platform name.
    /// </summary>
    public string LaunchBoxPlatformName { get; set; } = "";

    /// <summary>
    /// Returns the display name for list controls.
    /// </summary>
    public override string ToString() => Name;
}
