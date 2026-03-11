using System.Runtime.Serialization;
using RomMbox.Models.Install;

namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// User-configurable mapping from a RomM platform to a LaunchBox platform
    /// with optional install/extraction preferences.
    /// </summary>
    [DataContract]
    internal sealed class PlatformMapping
    {
        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        [DataMember(Name = "rommPlatformId", EmitDefaultValue = false)]
        public string RommPlatformId { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the RomM platform.
        /// </summary>
        [DataMember(Name = "rommPlatformName", EmitDefaultValue = false)]
        public string RommPlatformName { get; set; } = string.Empty;

        /// <summary>
        /// LaunchBox platform name to map to.
        /// </summary>
        [DataMember(Name = "launchBoxPlatformName", EmitDefaultValue = false)]
        public string LaunchBoxPlatformName { get; set; } = string.Empty;

        /// <summary>
        /// True if the mapping was auto-resolved instead of user-selected.
        /// </summary>
        [DataMember(Name = "autoMapped", EmitDefaultValue = false)]
        public bool AutoMapped { get; set; }

        /// <summary>
        /// When true, auto import is disabled for this platform.
        /// </summary>
        [DataMember(Name = "disableAutoImport", EmitDefaultValue = false)]
        public bool DisableAutoImport { get; set; }

        /// <summary>
        /// Whether archives should be extracted after download.
        /// </summary>
        [DataMember(Name = "extractAfterDownload", EmitDefaultValue = false)]
        public bool ExtractAfterDownload { get; set; }

        /// <summary>
        /// How extracted content should be arranged on disk.
        /// </summary>
        [DataMember(Name = "extractionBehavior", EmitDefaultValue = false)]
        public ExtractionBehavior ExtractionBehavior { get; set; } = ExtractionBehavior.Subfolder;

        /// <summary>
        /// Installation scenario used for Windows game installs.
        /// </summary>
        [DataMember(Name = "installScenario", EmitDefaultValue = false)]
        public InstallScenario InstallScenario { get; set; } = InstallScenario.Basic;

        /// <summary>
        /// Gets or sets whether extracted content is self-contained for emulator platforms.
        /// </summary>
        [DataMember(Name = "selfContained", EmitDefaultValue = false)]
        public bool SelfContained { get; set; } = true;

        /// <summary>
        /// Target file name(s) to import when extracting multi-file archives.
        /// </summary>
        [DataMember(Name = "targetImportFile", EmitDefaultValue = false)]
        public string TargetImportFile { get; set; } = string.Empty;

        /// <summary>
        /// Optional silent install arguments for installer-based scenarios.
        /// </summary>
        [DataMember(Name = "installerSilentArgs", EmitDefaultValue = false)]
        public string InstallerSilentArgs { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the associated emulator id for this platform.
        /// </summary>
        [DataMember(Name = "associatedEmulatorId", EmitDefaultValue = false)]
        public string AssociatedEmulatorId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the emulator core identifier for ROM platforms.
        /// </summary>
        [DataMember(Name = "emulatorCoreId", EmitDefaultValue = false)]
        public string EmulatorCoreId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the emulator core display name.
        /// </summary>
        [DataMember(Name = "emulatorCoreName", EmitDefaultValue = false)]
        public string EmulatorCoreName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the emulator core path.
        /// </summary>
        [DataMember(Name = "emulatorCorePath", EmitDefaultValue = false)]
        public string EmulatorCorePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the emulator launch arguments template.
        /// </summary>
        [DataMember(Name = "emulatorLaunchArgs", EmitDefaultValue = false)]
        public string EmulatorLaunchArgs { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the install root override for ROM platforms.
        /// </summary>
        [DataMember(Name = "romInstallRoot", EmitDefaultValue = false)]
        public string RomInstallRoot { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the archive policy for ROM platforms.
        /// </summary>
        [DataMember(Name = "romArchivePolicy", EmitDefaultValue = false)]
        public string RomArchivePolicy { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the selected plugin key for this platform mapping.
        /// </summary>
        [DataMember(Name = "pluginKey", EmitDefaultValue = false)]
        public string PluginKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets serialized plugin settings payload for dynamic plugin configuration.
        /// </summary>
        [DataMember(Name = "pluginSettings", EmitDefaultValue = false)]
        public string PluginSettings { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets archive handling mode for general ROM installs.
        /// </summary>
        [DataMember(Name = "archiveHandlingMode", EmitDefaultValue = false)]
        public string ArchiveHandlingMode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets supported file extensions for the general fallback installer.
        /// </summary>
        [DataMember(Name = "supportedFileTypes", EmitDefaultValue = false)]
        public string SupportedFileTypes { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets preferred launch-extension ordering for the general fallback installer.
        /// </summary>
        [DataMember(Name = "preferredLaunchExtensions", EmitDefaultValue = false)]
        public string PreferredLaunchExtensions { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether installs should be placed in a per-game subdirectory.
        /// </summary>
        [DataMember(Name = "useGameSubdirectory", EmitDefaultValue = false)]
        public bool UseGameSubdirectory { get; set; } = true;

        /// <summary>
        /// Gets or sets whether all discovered matching files should be installed.
        /// </summary>
        [DataMember(Name = "installAllMatchingFiles", EmitDefaultValue = false)]
        public bool InstallAllMatchingFiles { get; set; } = true;

        /// <summary>
        /// Gets or sets whether supported archives can be installed/launched directly.
        /// </summary>
        [DataMember(Name = "installFromArchiveDirectly", EmitDefaultValue = false)]
        public bool InstallFromArchiveDirectly { get; set; }

        /// <summary>
        /// Gets or sets install layout behavior for general ROM installs.
        /// </summary>
        [DataMember(Name = "installLayoutMode", EmitDefaultValue = false)]
        public string InstallLayoutMode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets artifact selection strategy when multiple candidates exist.
        /// </summary>
        [DataMember(Name = "artifactSelectionMode", EmitDefaultValue = false)]
        public string ArtifactSelectionMode { get; set; } = string.Empty;

        /// <summary>
        /// True when this mapping should use the general fallback installer.
        /// </summary>
        [DataMember(Name = "useGeneralFallbackInstaller", EmitDefaultValue = false)]
        public bool UseGeneralFallbackInstaller { get; set; }

        /// <summary>
        /// Determines whether installers run manually or silently.
        /// </summary>
        [DataMember(Name = "installerMode", EmitDefaultValue = false)]
        public InstallerMode InstallerMode { get; set; } = InstallerMode.Manual;

        /// <summary>
        /// Root directory for soundtrack installs (if enabled).
        /// </summary>
        [DataMember(Name = "musicRootPath", EmitDefaultValue = false)]
        public string MusicRootPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets where soundtrack content should be installed.
        /// </summary>
        [DataMember(Name = "ostInstallLocation", EmitDefaultValue = false)]
        public OptionalContentLocation OstInstallLocation { get; set; } = OptionalContentLocation.Centralized;

        /// <summary>
        /// Whether to install soundtrack content.
        /// </summary>
        [DataMember(Name = "installOst", EmitDefaultValue = false)]
        public bool InstallOst { get; set; }

        /// <summary>
        /// Root directory for bonus content.
        /// </summary>
        [DataMember(Name = "bonusRootPath", EmitDefaultValue = false)]
        public string BonusRootPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets where bonus content should be installed.
        /// </summary>
        [DataMember(Name = "bonusInstallLocation", EmitDefaultValue = false)]
        public OptionalContentLocation BonusInstallLocation { get; set; } = OptionalContentLocation.Centralized;

        /// <summary>
        /// Whether to install bonus content.
        /// </summary>
        [DataMember(Name = "installBonus", EmitDefaultValue = false)]
        public bool InstallBonus { get; set; }

        /// <summary>
        /// Root directory for prerequisites content.
        /// </summary>
        [DataMember(Name = "preReqsRootPath", EmitDefaultValue = false)]
        public string PreReqsRootPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether to install prerequisite content.
        /// </summary>
        [DataMember(Name = "installPreReqs", EmitDefaultValue = false)]
        public bool InstallPreReqs { get; set; }

        /// <summary>
        /// Optional custom install directory override.
        /// </summary>
        [DataMember(Name = "customInstallDirectory", EmitDefaultValue = false)]
        public string CustomInstallDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the PS3 game directory override.
        /// </summary>
        [DataMember(Name = "ps3GameDirectory", EmitDefaultValue = false)]
        public string Ps3GameDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the RPCS3 executable path for PS3 installs.
        /// </summary>
        [DataMember(Name = "rpcs3ExecutablePath", EmitDefaultValue = false)]
        public string Rpcs3ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether DLC packages should be installed automatically.
        /// </summary>
        [DataMember(Name = "installDlcAutomatically", EmitDefaultValue = false)]
        public bool InstallDlcAutomatically { get; set; }

        /// <summary>
        /// Gets or sets whether update packages should be installed automatically.
        /// </summary>
        [DataMember(Name = "installUpdatesAutomatically", EmitDefaultValue = false)]
        public bool InstallUpdatesAutomatically { get; set; }

        /// <summary>
        /// Gets or sets the RPCS3 license directory override.
        /// </summary>
        [DataMember(Name = "rpcs3LicenseDirectory", EmitDefaultValue = false)]
        public string Rpcs3LicenseDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether region-mismatched DLC should be skipped.
        /// </summary>
        [DataMember(Name = "skipRegionMismatchedDlc", EmitDefaultValue = false)]
        public bool SkipRegionMismatchedDlc { get; set; }

        /// <summary>
        /// Gets or sets whether unmatched RAP files should be skipped.
        /// </summary>
        [DataMember(Name = "skipUnmatchedRapFiles", EmitDefaultValue = false)]
        public bool SkipUnmatchedRapFiles { get; set; }

        /// <summary>
        /// Gets or sets whether metadata-based package matching should be preferred.
        /// </summary>
        [DataMember(Name = "preferMetadataBasedPackageMatching", EmitDefaultValue = false)]
        public bool PreferMetadataBasedPackageMatching { get; set; }

        /// <summary>
        /// Gets or sets the PS4 games directory override.
        /// </summary>
        [DataMember(Name = "ps4GamesDirectory", EmitDefaultValue = false)]
        public string Ps4GamesDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ShadPS4 executable path.
        /// </summary>
        [DataMember(Name = "shadPs4ExecutablePath", EmitDefaultValue = false)]
        public string ShadPs4ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets optional external PKG extractor path for PS4 direct PKG workflows.
        /// </summary>
        [DataMember(Name = "ps4ExternalPkgExtractorPath", EmitDefaultValue = false)]
        public string Ps4ExternalPkgExtractorPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether direct PKG downloads should fail when extractor is missing.
        /// </summary>
        [DataMember(Name = "ps4FailIfDirectPkgExtractorMissing", EmitDefaultValue = false)]
        public bool Ps4FailIfDirectPkgExtractorMissing { get; set; }
    }
}
