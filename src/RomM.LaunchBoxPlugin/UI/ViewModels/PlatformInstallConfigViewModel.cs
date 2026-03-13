using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.UI.Models;
using RomMbox.Services.Install;
using RomMbox.UI.Infrastructure;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model for the platform configuration dialog.
/// </summary>
public sealed class PlatformInstallConfigViewModel : ObservableObject
{
    private readonly Action _onSave;
    private readonly Action _onBack;
    /// <summary>
    /// Initializes the view model from the given platform mapping.
    /// </summary>
    /// <param name="mapping">The platform mapping to edit.</param>
    /// <param name="defaultInstallDirectory">Default install directory for the platform.</param>
    public PlatformInstallConfigViewModel(
        Models.PlatformMapping mapping,
        string defaultInstallDirectory,
        PlatformConfigDescriptor configDescriptor = null,
        Action onSave = null,
        Action onBack = null)
    {
        _onSave = onSave;
        _onBack = onBack;
        _mapping = mapping;
        _configDescriptor = configDescriptor;
        LaunchBoxPlatformName = mapping?.LaunchBoxPlatform ?? mapping?.RomMPlatform ?? string.Empty;
        IsWindowsPlatform = InstallDestinationService.IsWindowsPlatform(LaunchBoxPlatformName);
        DefaultInstallDirectory = defaultInstallDirectory ?? string.Empty;
        GamesDirectory = ResolveGamesDirectory(mapping?.CustomInstallDirectory, DefaultInstallDirectory);


        InstallationType = ResolveDefaultInstallType(mapping);
        ExtractAfterDownload = mapping?.ExtractAfterDownload ?? false;
        ExtractionBehavior = mapping?.ExtractionBehavior ?? ExtractionBehavior.Subfolder;
        SelfContained = mapping?.SelfContained ?? true;
        TargetImportFile = mapping?.TargetImportFile ?? string.Empty;

        AssociatedEmulatorId = string.IsNullOrWhiteSpace(mapping?.AssociatedEmulatorId)
            ? ResolveDefaultEmulatorId(LaunchBoxPlatformName)
            : mapping.AssociatedEmulatorId;
        EmulatorCoreId = mapping?.EmulatorCoreId ?? string.Empty;
        EmulatorCoreName = mapping?.EmulatorCoreName ?? string.Empty;
        EmulatorCorePath = mapping?.EmulatorCorePath ?? string.Empty;
        EmulatorLaunchArgs = mapping?.EmulatorLaunchArgs ?? string.Empty;
        RomInstallRoot = mapping?.RomInstallRoot ?? string.Empty;
        RomArchivePolicy = mapping?.RomArchivePolicy ?? string.Empty;
        PluginKey = mapping?.PluginKey ?? string.Empty;
        PluginSettings = mapping?.PluginSettings ?? string.Empty;
        SupportedFileTypes = ResolveConfiguredValue(mapping?.SupportedFileTypes, "SupportedFileTypes");
        PreferredLaunchExtensions = mapping?.PreferredLaunchExtensions ?? string.Empty;
        ArchiveHandlingMode = string.IsNullOrWhiteSpace(mapping?.ArchiveHandlingMode) ? "NeverExtract" : mapping.ArchiveHandlingMode;
        UseGameSubdirectory = mapping?.UseGameSubdirectory ?? true;
        InstallAllMatchingFiles = mapping?.InstallAllMatchingFiles ?? true;
        InstallFromArchiveDirectly = mapping?.InstallFromArchiveDirectly ?? false;
        InstallLayoutMode = string.IsNullOrWhiteSpace(mapping?.InstallLayoutMode) ? "UsePlatformRoot" : mapping.InstallLayoutMode;
        ArtifactSelectionMode = string.IsNullOrWhiteSpace(mapping?.ArtifactSelectionMode) ? "ExtensionPriority" : mapping.ArtifactSelectionMode;
        UseGeneralFallbackInstaller = mapping?.UseGeneralFallbackInstaller ?? false;
        Emulators = new ObservableCollection<EmulatorOption>(LoadEmulators());

        InstallerMode = mapping?.InstallerMode ?? InstallerMode.Manual;
        InstallOst = mapping?.InstallOst ?? false;
        MusicRootPath = mapping?.MusicRootPath ?? string.Empty;
        OstInstallLocation = mapping?.OstInstallLocation ?? OptionalContentLocation.Centralized;
        InstallBonus = mapping?.InstallBonus ?? false;
        BonusRootPath = mapping?.BonusRootPath ?? string.Empty;
        BonusInstallLocation = mapping?.BonusInstallLocation ?? OptionalContentLocation.Centralized;
        InstallPreReqs = mapping?.InstallPreReqs ?? false;
        PreReqsRootPath = mapping?.PreReqsRootPath ?? string.Empty;

        Ps3GameDirectory = mapping?.Ps3GameDirectory ?? string.Empty;
        Rpcs3ExecutablePath = mapping?.Rpcs3ExecutablePath ?? string.Empty;
        InstallDlcAutomatically = mapping?.InstallDlcAutomatically ?? false;
        InstallUpdatesAutomatically = mapping?.InstallUpdatesAutomatically ?? false;
        Rpcs3LicenseDirectory = mapping?.Rpcs3LicenseDirectory ?? string.Empty;
        SkipRegionMismatchedDlc = mapping?.SkipRegionMismatchedDlc ?? false;
        SkipUnmatchedRapFiles = mapping?.SkipUnmatchedRapFiles ?? false;
        PreferMetadataBasedPackageMatching = mapping?.PreferMetadataBasedPackageMatching ?? false;
        Ps4GamesDirectory = mapping?.Ps4GamesDirectory ?? string.Empty;
        ShadPs4ExecutablePath = mapping?.ShadPs4ExecutablePath ?? string.Empty;
        Ps4ExternalPkgExtractorPath = mapping?.Ps4ExternalPkgExtractorPath ?? string.Empty;
        Ps4FailIfDirectPkgExtractorMissing = mapping?.Ps4FailIfDirectPkgExtractorMissing ?? false;
        Pcsx2ExecutablePath = mapping?.Pcsx2ExecutablePath ?? string.Empty;
        PspEmulatorMode = string.IsNullOrWhiteSpace(mapping?.PspEmulatorMode) ? ResolveDefaultPspEmulatorMode(mapping) : mapping.PspEmulatorMode;
        PpssppExecutablePath = mapping?.PpssppExecutablePath ?? string.Empty;
        RetroArchExecutablePath = mapping?.RetroArchExecutablePath ?? string.Empty;
        RetroArchPpssppCorePath = mapping?.RetroArchPpssppCorePath ?? string.Empty;
        ValidateRetroArchPpssppAssets = mapping?.ValidateRetroArchPpssppAssets ?? true;
        FailInstallIfEmulatorNotReady = mapping?.FailInstallIfEmulatorNotReady ?? false;
        Vita3kExecutablePath = mapping?.Vita3kExecutablePath ?? string.Empty;
        VitaFailIfEmulatorNotReady = mapping?.VitaFailIfEmulatorNotReady ?? false;
        VitaInstallUpdatesAutomatically = mapping?.VitaInstallUpdatesAutomatically ?? false;
        VitaInstallDlcAutomatically = mapping?.VitaInstallDlcAutomatically ?? false;
        SwitchEdenExecutablePath = mapping?.SwitchEdenExecutablePath ?? string.Empty;
        AzaharExecutablePath = mapping?.AzaharExecutablePath ?? string.Empty;
        AzaharPlusExecutablePath = mapping?.AzaharPlusExecutablePath ?? string.Empty;
        DolphinExecutablePath = mapping?.DolphinExecutablePath ?? string.Empty;
        CemuExecutablePath = mapping?.CemuExecutablePath ?? string.Empty;

        InstallScenario = InstallationType == InstallTypeChoice.Enhanced
            ? InstallScenario.Enhanced
            : InstallScenario.Basic;
        InstallerSilentArgs = mapping?.InstallerSilentArgs ?? string.Empty;

        SaveCommand = new RelayCommand(() => _onSave?.Invoke(), () => CanSave);
        BackCommand = new RelayCommand(() => _onBack?.Invoke());

        UpdateTargetImportDerivedFields();
        RaisePropertyChanged(nameof(PreReqsTargetSummary));
    }

    private readonly Models.PlatformMapping _mapping;
    private readonly PlatformConfigDescriptor _configDescriptor;

    /// <summary>
    /// Gets the LaunchBox platform name for display and defaults.
    /// </summary>
    public string LaunchBoxPlatformName { get; }

    /// <summary>
    /// Gets the default install directory for the platform.
    /// </summary>
    public string DefaultInstallDirectory { get; }


    /// <summary>
    /// Gets whether the platform is considered a Windows install platform.
    /// </summary>
    public bool IsWindowsPlatform { get; }

    private InstallTypeChoice _installationType;
    /// <summary>
    /// Gets or sets the installation type selection for this platform.
    /// </summary>
    public InstallTypeChoice InstallationType
    {
        get => _installationType;
        set
        {
            if (SetProperty(ref _installationType, value))
            {
                RaisePropertyChanged(nameof(IsBasicSelected));
                RaisePropertyChanged(nameof(IsEnhancedSelected));
                RaisePropertyChanged(nameof(IsAssociatedEmulatorEnabled));
                InstallScenario = value == InstallTypeChoice.Enhanced ? InstallScenario.Enhanced : InstallScenario.Basic;
            }
        }
    }

    public bool IsBasicSelected => InstallationType == InstallTypeChoice.Basic;
    public bool IsEnhancedSelected => InstallationType == InstallTypeChoice.Enhanced;

    public bool IsExtractionBehaviorEnabled => ExtractAfterDownload;

    private InstallScenario _installScenario;
    /// <summary>
    /// Gets or sets the selected install scenario and updates dependent flags.
    /// </summary>
    public InstallScenario InstallScenario
    {
        get => _installScenario;
        set
        {
            if (SetProperty(ref _installScenario, value))
            {
                RaisePropertyChanged(nameof(IsEnhancedSelected));
                RaisePropertyChanged(nameof(IsBasicSelected));
            }
        }
    }

    private string _targetImportFile = string.Empty;
    /// <summary>
    /// Gets or sets the target import file for the platform.
    /// </summary>
    public string TargetImportFile
    {
        get => _targetImportFile;
        set
        {
            if (SetProperty(ref _targetImportFile, value))
            {
                UpdateTargetImportDerivedFields();
            }
        }
    }

    private string _installerSilentArgs = string.Empty;
    /// <summary>
    /// Gets or sets silent install arguments for installer packages.
    /// </summary>
    public string InstallerSilentArgs
    {
        get => _installerSilentArgs;
        set => SetProperty(ref _installerSilentArgs, value);
    }

    private InstallerMode _installerMode = InstallerMode.Manual;
    /// <summary>
    /// Gets or sets the installer mode.
    /// </summary>
    public InstallerMode InstallerMode
    {
        get => _installerMode;
        set
        {
            if (SetProperty(ref _installerMode, value))
            {
                RaisePropertyChanged(nameof(IsInstallModeAutomatic));
            }
        }
    }

    private string _musicRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the music root path.
    /// </summary>
    public string MusicRootPath
    {
        get => _musicRootPath;
        set => SetProperty(ref _musicRootPath, value);
    }

    private bool _installOst;
    /// <summary>
    /// Gets or sets whether soundtrack content should be installed.
    /// </summary>
    public bool InstallOst
    {
        get => _installOst;
        set
        {
            if (SetProperty(ref _installOst, value))
            {
                RaisePropertyChanged(nameof(IsOstOptionsEnabled));
                RaisePropertyChanged(nameof(IsOstCentralizedSelected));
                RaisePropertyChanged(nameof(IsOstGameFolderSelected));
            }
        }
    }

    private OptionalContentLocation _ostInstallLocation = OptionalContentLocation.Centralized;
    /// <summary>
    /// Gets or sets where OST content is installed.
    /// </summary>
    public OptionalContentLocation OstInstallLocation
    {
        get => _ostInstallLocation;
        set
        {
            if (SetProperty(ref _ostInstallLocation, value))
            {
                RaisePropertyChanged(nameof(IsOstCentralizedSelected));
                RaisePropertyChanged(nameof(IsOstGameFolderSelected));
            }
        }
    }

    private string _bonusRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the bonus content root path.
    /// </summary>
    public string BonusRootPath
    {
        get => _bonusRootPath;
        set => SetProperty(ref _bonusRootPath, value);
    }

    private bool _installBonus;
    /// <summary>
    /// Gets or sets whether bonus content should be installed.
    /// </summary>
    public bool InstallBonus
    {
        get => _installBonus;
        set
        {
            if (SetProperty(ref _installBonus, value))
            {
                RaisePropertyChanged(nameof(IsBonusOptionsEnabled));
                RaisePropertyChanged(nameof(IsBonusCentralizedSelected));
                RaisePropertyChanged(nameof(IsBonusGameFolderSelected));
            }
        }
    }

    private OptionalContentLocation _bonusInstallLocation = OptionalContentLocation.Centralized;
    /// <summary>
    /// Gets or sets where bonus content is installed.
    /// </summary>
    public OptionalContentLocation BonusInstallLocation
    {
        get => _bonusInstallLocation;
        set
        {
            if (SetProperty(ref _bonusInstallLocation, value))
            {
                RaisePropertyChanged(nameof(IsBonusCentralizedSelected));
                RaisePropertyChanged(nameof(IsBonusGameFolderSelected));
            }
        }
    }

    private string _preReqsRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the prerequisites root path.
    /// </summary>
    public string PreReqsRootPath
    {
        get => _preReqsRootPath;
        set
        {
            if (SetProperty(ref _preReqsRootPath, value))
            {
                RaisePropertyChanged(nameof(PreReqsTargetSummary));
            }
        }
    }

    private bool _installPreReqs;
    /// <summary>
    /// Gets or sets whether prerequisites should be installed.
    /// </summary>
    public bool InstallPreReqs
    {
        get => _installPreReqs;
        set
        {
            if (SetProperty(ref _installPreReqs, value))
            {
                RaisePropertyChanged(nameof(IsPreReqsOptionsEnabled));
                RaisePropertyChanged(nameof(PreReqsTargetSummary));
            }
        }
    }

    private bool _extractAfterDownload = false;
    /// <summary>
    /// Gets or sets whether extraction should run after download.
    /// </summary>
    public bool ExtractAfterDownload
    {
        get => _extractAfterDownload;
        set
        {
            if (SetProperty(ref _extractAfterDownload, value))
            {
                RaisePropertyChanged(nameof(IsExtractionBehaviorEnabled));
            }
        }
    }

    private ExtractionBehavior _extractionBehavior = ExtractionBehavior.Subfolder;
    /// <summary>
    /// Gets or sets the extraction behavior.
    /// </summary>
    public ExtractionBehavior ExtractionBehavior { get => _extractionBehavior; set => SetProperty(ref _extractionBehavior, value); }

    private string _gamesDirectory = string.Empty;
    /// <summary>
    /// Gets or sets the games directory for this platform.
    /// </summary>
    public string GamesDirectory
    {
        get => _gamesDirectory;
        set
        {
            if (SetProperty(ref _gamesDirectory, value))
            {
                RaisePropertyChanged(nameof(IsGamesDirectoryValid));
                RaisePropertyChanged(nameof(CanSave));
                SaveCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _selfContained = true;
    /// <summary>
    /// Gets or sets whether the extracted folder is self-contained.
    /// </summary>
    public bool SelfContained
    {
        get => _selfContained;
        set
        {
            if (SetProperty(ref _selfContained, value))
            {
                RaisePropertyChanged(nameof(IsTargetFilesEnabled));
            }
        }
    }

    private string _associatedEmulatorId = string.Empty;
    /// <summary>
    /// Gets or sets the associated emulator id for this platform.
    /// </summary>
    public string AssociatedEmulatorId
    {
        get => _associatedEmulatorId;
        set
        {
            if (SetProperty(ref _associatedEmulatorId, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _emulatorCoreId = string.Empty;
    /// <summary>
    /// Gets or sets the emulator core id.
    /// </summary>
    public string EmulatorCoreId { get => _emulatorCoreId; set => SetProperty(ref _emulatorCoreId, value); }

    private string _emulatorCoreName = string.Empty;
    /// <summary>
    /// Gets or sets the emulator core name.
    /// </summary>
    public string EmulatorCoreName { get => _emulatorCoreName; set => SetProperty(ref _emulatorCoreName, value); }

    private string _emulatorCorePath = string.Empty;
    /// <summary>
    /// Gets or sets the emulator core path.
    /// </summary>
    public string EmulatorCorePath { get => _emulatorCorePath; set => SetProperty(ref _emulatorCorePath, value); }

    private string _emulatorLaunchArgs = string.Empty;
    /// <summary>
    /// Gets or sets the emulator launch arguments.
    /// </summary>
    public string EmulatorLaunchArgs { get => _emulatorLaunchArgs; set => SetProperty(ref _emulatorLaunchArgs, value); }

    private string _romInstallRoot = string.Empty;
    /// <summary>
    /// Gets or sets the ROM install root override.
    /// </summary>
    public string RomInstallRoot { get => _romInstallRoot; set => SetProperty(ref _romInstallRoot, value); }

    private string _romArchivePolicy = string.Empty;
    /// <summary>
    /// Gets or sets the ROM archive policy.
    /// </summary>
    public string RomArchivePolicy { get => _romArchivePolicy; set => SetProperty(ref _romArchivePolicy, value); }

    private string _pluginKey = string.Empty;
    /// <summary>
    /// Gets or sets the resolved plugin key for this platform.
    /// </summary>
    public string PluginKey { get => _pluginKey; set => SetProperty(ref _pluginKey, value); }

    private string _pluginSettings = string.Empty;
    /// <summary>
    /// Gets or sets serialized plugin settings payload.
    /// </summary>
    public string PluginSettings { get => _pluginSettings; set => SetProperty(ref _pluginSettings, value); }

    private string _supportedFileTypes = string.Empty;
    /// <summary>
    /// Gets or sets supported file types for the general fallback installer.
    /// </summary>
    public string SupportedFileTypes { get => _supportedFileTypes; set => SetProperty(ref _supportedFileTypes, value); }

    private string _preferredLaunchExtensions = string.Empty;
    /// <summary>
    /// Gets or sets preferred launch extension ordering for fallback selection.
    /// </summary>
    public string PreferredLaunchExtensions { get => _preferredLaunchExtensions; set => SetProperty(ref _preferredLaunchExtensions, value); }

    private string _archiveHandlingMode = "NeverExtract";
    /// <summary>
    /// Gets or sets archive handling mode for general ROM platforms.
    /// </summary>
    public string ArchiveHandlingMode { get => _archiveHandlingMode; set => SetProperty(ref _archiveHandlingMode, value); }

    private bool _useGameSubdirectory = true;
    /// <summary>
    /// Gets or sets whether fallback installs use a game subdirectory.
    /// </summary>
    public bool UseGameSubdirectory
    {
        get => _useGameSubdirectory;
        set
        {
            if (SetProperty(ref _useGameSubdirectory, value))
            {
                RaisePropertyChanged(nameof(IsInstallInRootLayout));
                RaisePropertyChanged(nameof(IsInstallInSubdirectoryLayout));
            }
        }
    }

    private bool _installAllMatchingFiles = true;
    /// <summary>
    /// Gets or sets whether fallback installs include all matching files.
    /// </summary>
    public bool InstallAllMatchingFiles { get => _installAllMatchingFiles; set => SetProperty(ref _installAllMatchingFiles, value); }

    private bool _installFromArchiveDirectly;
    /// <summary>
    /// Gets or sets whether fallback installs can launch from archives directly.
    /// </summary>
    public bool InstallFromArchiveDirectly { get => _installFromArchiveDirectly; set => SetProperty(ref _installFromArchiveDirectly, value); }

    private string _installLayoutMode = "UsePlatformRoot";
    /// <summary>
    /// Gets or sets install layout mode for general ROM platforms.
    /// </summary>
    public string InstallLayoutMode
    {
        get => _installLayoutMode;
        set
        {
            if (SetProperty(ref _installLayoutMode, value))
            {
                RaisePropertyChanged(nameof(IsInstallInRootLayout));
                RaisePropertyChanged(nameof(IsInstallInSubdirectoryLayout));
            }
        }
    }

    private string _artifactSelectionMode = "ExtensionPriority";
    /// <summary>
    /// Gets or sets artifact selection mode for general ROM platforms.
    /// </summary>
    public string ArtifactSelectionMode { get => _artifactSelectionMode; set => SetProperty(ref _artifactSelectionMode, value); }

    private bool _useGeneralFallbackInstaller;
    /// <summary>
    /// Gets or sets whether this mapping should allow general fallback installer usage.
    /// </summary>
    public bool UseGeneralFallbackInstaller { get => _useGeneralFallbackInstaller; set => SetProperty(ref _useGeneralFallbackInstaller, value); }

    public ObservableCollection<EmulatorOption> Emulators { get; }

    public string PageTitle => string.IsNullOrWhiteSpace(LaunchBoxPlatformName)
        ? "Platform Configuration"
        : $"{LaunchBoxPlatformName} Configuration";

    public string PageSubtitle => ResolvePageSubtitle();

    public string BannerPlatformLabel => string.IsNullOrWhiteSpace(LaunchBoxPlatformName)
        ? "Platform"
        : $"Platform: {LaunchBoxPlatformName}";

    public string SelectedEmulatorName => SelectedEmulator?.Name ?? "Not configured";

    public bool ShowBannerEmulatorSelector => Emulators.Count > 0 && !IsWindowsPlatform;

    public string BannerStatusText => ResolveBannerStatusText();

    public bool ShowBannerStatusBadge => !string.IsNullOrWhiteSpace(BannerStatusText);

    public string PlatformOptionsDescription => "Set default paths and content-handling behavior for this platform.";

    public string EmulatorOptionsDescription => "Configure emulator-specific paths, supported content formats, and launch behavior.";

    public string ExtraConfigurationDescription => "Optional plugin-specific settings and workflow controls for this platform.";

    public bool IsInstallInRootLayout
    {
        get => string.Equals(GetNormalizedInstallLayoutMode(), "UsePlatformRoot", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SetInstallLayoutMode("UsePlatformRoot");
            }
        }
    }

    public bool IsInstallInSubdirectoryLayout
    {
        get => !IsInstallInRootLayout;
        set
        {
            if (value)
            {
                SetInstallLayoutMode("CreatePerGameSubfolder");
            }
        }
    }

    public EmulatorOption SelectedEmulator
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AssociatedEmulatorId))
            {
                return null;
            }

            return Emulators.FirstOrDefault(item => string.Equals(item.Id, AssociatedEmulatorId, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            var nextId = value?.Id ?? string.Empty;
            if (!string.Equals(AssociatedEmulatorId, nextId, StringComparison.OrdinalIgnoreCase))
            {
                AssociatedEmulatorId = nextId;
                NotifyBannerStateChanged();
            }
        }
    }

    public bool IsTargetFilesEnabled => !SelfContained;
    public bool IsOstOptionsEnabled => InstallOst;
    public bool IsBonusOptionsEnabled => InstallBonus;
    public bool IsPreReqsOptionsEnabled => InstallPreReqs;
    public bool IsOstCentralizedSelected => InstallOst && OstInstallLocation == OptionalContentLocation.Centralized;
    public bool IsOstGameFolderSelected => InstallOst && OstInstallLocation == OptionalContentLocation.GameFolder;
    public bool IsBonusCentralizedSelected => InstallBonus && BonusInstallLocation == OptionalContentLocation.Centralized;
    public bool IsBonusGameFolderSelected => InstallBonus && BonusInstallLocation == OptionalContentLocation.GameFolder;

    public bool IsInstallModeAutomatic => InstallerMode == InstallerMode.AutoInnoSilent;

    public bool IsAssociatedEmulatorEnabled => InstallationType == InstallTypeChoice.Basic;
    public bool IsGeneralPlugin => string.Equals(PluginKey, "general", StringComparison.OrdinalIgnoreCase);
    public bool HasPluginDescriptorFields => _configDescriptor?.Fields != null && _configDescriptor.Fields.Count > 0;
    public bool HasPluginSpecificTooling => HasPluginDescriptorFields && !IsGeneralPlugin;
    public bool ShowPlatformOptionsSection => true;
    public bool ShowEmulatorOptionsSection =>
        ShowRetroArchCoreSelectionField
        || ShowSupportedFileTypesField
        || ShowRpcs3ExecutablePathField
        || ShowRpcs3LicenseDirectoryField
        || ShowShadPs4ExecutablePathField
        || ShowPs4ExternalPkgExtractorPathField
        || ShowPs4FailIfDirectPkgExtractorMissingField
        || ShowPcsx2ExecutablePathField
        || ShowPspEmulatorModeField
        || ShowPpssppExecutablePathField
        || ShowRetroArchExecutablePathField
        || ShowRetroArchPpssppCorePathField
        || ShowValidateRetroArchPpssppAssetsField
        || ShowFailInstallIfEmulatorNotReadyField
        || ShowVita3kExecutablePathField
        || ShowVitaFailIfEmulatorNotReadyField
        || ShowSwitchEdenExecutablePathField
        || ShowAzaharExecutablePathField
        || ShowAzaharPlusExecutablePathField
        || ShowDolphinExecutablePathField
        || ShowCemuExecutablePathField;
    public bool ShowExtraConfigurationSection =>
        ShowSkipRegionMismatchedDlcField
        || ShowSkipUnmatchedRapFilesField
        || ShowPreferMetadataBasedPackageMatchingField
        || ShowVitaInstallUpdatesAutomaticallyField
        || ShowVitaInstallDlcAutomaticallyField
        || ShowVitaFailIfEmulatorNotReadyField;
    public bool ShowInstallationTypeSection => IsWindowsPlatform;
    public bool ShowAssociatedEmulatorField => IsGeneralPlugin;
    public bool ShowRetroArchCoreSelectionField => IsRetroArchSelected && !ShowPspEmulatorModeField;
    public bool ShowExtractAfterDownloadField => false;
    public bool ShowRomInstallRootField => false;
    public bool ShowRomArchivePolicyField => false;
    public bool ShowBasicOptionsSection => IsBasicSelected && IsWindowsPlatform;
    public bool ShowEnhancedOptionsSection => IsEnhancedSelected && IsWindowsPlatform;

    public bool ShowPs3GameDirectoryField => false;
    public bool ShowRpcs3ExecutablePathField => HasConfigField("Rpcs3ExecutablePath");
    public bool ShowRpcs3LicenseDirectoryField => HasConfigField("Rpcs3LicenseDirectory");
    public bool ShowSkipRegionMismatchedDlcField => HasConfigField("SkipRegionMismatchedDlc");
    public bool ShowSkipUnmatchedRapFilesField => HasConfigField("SkipUnmatchedRapFiles");
    public bool ShowPreferMetadataBasedPackageMatchingField => HasConfigField("PreferMetadataBasedPackageMatching");
    public bool ShowSupportedFileTypesField => HasConfigField("SupportedFileTypes") && HasSelectedEmulatorSpecificConfiguration;
    public bool ShowPreferredLaunchExtensionsField => false;
    public bool ShowArchiveHandlingModeField => false;
    public bool ShowUseGameSubdirectoryField => false;
    public bool ShowInstallAllMatchingFilesField => false;
    public bool ShowInstallFromArchiveDirectlyField => false;
    public bool ShowInstallLayoutModeField => false;
    public bool ShowArtifactSelectionModeField => false;
    public bool ShowUseGeneralFallbackInstallerField => HasConfigField("UseGeneralFallbackInstaller") && IsWindowsPlatform;
    public bool ShowPs4GamesDirectoryField => false;
    public bool ShowShadPs4ExecutablePathField => HasConfigField("ShadPs4ExecutablePath");
    public bool ShowPs4ExternalPkgExtractorPathField => HasConfigField("Ps4ExternalPkgExtractorPath");
    public bool ShowPs4FailIfDirectPkgExtractorMissingField => HasConfigField("Ps4FailIfDirectPkgExtractorMissing") && ShowPs4ExternalPkgExtractorPathField;
    public bool ShowPcsx2ExecutablePathField => HasConfigField("Pcsx2ExecutablePath");
    public bool ShowPspEmulatorModeField => HasConfigField("PspEmulatorMode");
    public bool ShowPpssppExecutablePathField => HasConfigField("PpssppExecutablePath") && (ShowPspEmulatorModeField ? IsPspStandaloneMode : IsEmulatorFamilySelectedOrUnspecified("ppsspp"));
    public bool ShowRetroArchExecutablePathField => HasConfigField("RetroArchExecutablePath") && (ShowPspEmulatorModeField ? IsPspRetroArchMode : IsRetroArchSelected);
    public bool ShowRetroArchPpssppCorePathField => HasConfigField("RetroArchPpssppCorePath") && IsPspRetroArchMode;
    public bool ShowValidateRetroArchPpssppAssetsField => HasConfigField("ValidateRetroArchPpssppAssets") && IsPspRetroArchMode;
    public bool ShowFailInstallIfEmulatorNotReadyField => HasConfigField("FailInstallIfEmulatorNotReady") && (ShowPpssppExecutablePathField || ShowRetroArchExecutablePathField || ShowRetroArchCoreSelectionField);
    public bool ShowVita3kExecutablePathField => HasConfigField("Vita3kExecutablePath");
    public bool ShowVitaFailIfEmulatorNotReadyField => HasConfigField("VitaFailIfEmulatorNotReady");
    public bool ShowVitaInstallUpdatesAutomaticallyField => HasConfigField("VitaInstallUpdatesAutomatically");
    public bool ShowVitaInstallDlcAutomaticallyField => HasConfigField("VitaInstallDlcAutomatically");
    public bool ShowSwitchEdenExecutablePathField => HasConfigField("SwitchEdenExecutablePath");
    public bool ShowAzaharExecutablePathField => HasConfigField("AzaharExecutablePath");
    public bool ShowAzaharPlusExecutablePathField => HasConfigField("AzaharPlusExecutablePath");
    public bool ShowDolphinExecutablePathField => HasConfigField("DolphinExecutablePath");
    public bool ShowCemuExecutablePathField => HasConfigField("CemuExecutablePath");
    public bool IsRetroArchSelected => IsEmulatorFamilySelected("retroarch");
    public bool HasSelectedEmulatorSpecificConfiguration =>
        ShowShadPs4ExecutablePathField
        || ShowPs4ExternalPkgExtractorPathField
        || ShowRpcs3ExecutablePathField
        || ShowRpcs3LicenseDirectoryField
        || ShowPcsx2ExecutablePathField
        || ShowPpssppExecutablePathField
        || ShowRetroArchExecutablePathField
        || ShowRetroArchPpssppCorePathField
        || ShowVita3kExecutablePathField
        || ShowSwitchEdenExecutablePathField
        || ShowAzaharExecutablePathField
        || ShowAzaharPlusExecutablePathField
        || ShowDolphinExecutablePathField
        || ShowCemuExecutablePathField
        || ShowRetroArchCoreSelectionField
        || ShowPspEmulatorModeField;
    public bool IsPspRetroArchMode => string.Equals(PspEmulatorMode, "RetroArchPPSSPP", StringComparison.OrdinalIgnoreCase);
    public bool IsPspStandaloneMode => !IsPspRetroArchMode;

    public bool IsGamesDirectoryValid => !string.IsNullOrWhiteSpace(GamesDirectory);

    public bool CanSave => IsGamesDirectoryValid;

    public RelayCommand SaveCommand { get; }

    public RelayCommand BackCommand { get; }

    private string _ps3GameDirectory = string.Empty;
    /// <summary>
    /// Gets or sets the PS3 games directory override.
    /// </summary>
    public string Ps3GameDirectory { get => _ps3GameDirectory; set => SetProperty(ref _ps3GameDirectory, value); }

    private string _rpcs3ExecutablePath = string.Empty;
    /// <summary>
    /// Gets or sets the RPCS3 executable path.
    /// </summary>
    public string Rpcs3ExecutablePath
    {
        get => _rpcs3ExecutablePath;
        set
        {
            if (SetProperty(ref _rpcs3ExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private bool _installDlcAutomatically;
    /// <summary>
    /// Gets or sets whether DLC packages should be installed automatically.
    /// </summary>
    public bool InstallDlcAutomatically { get => _installDlcAutomatically; set => SetProperty(ref _installDlcAutomatically, value); }

    private bool _installUpdatesAutomatically;
    /// <summary>
    /// Gets or sets whether update packages should be installed automatically.
    /// </summary>
    public bool InstallUpdatesAutomatically { get => _installUpdatesAutomatically; set => SetProperty(ref _installUpdatesAutomatically, value); }

    private string _rpcs3LicenseDirectory = string.Empty;
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

    private string _ps4GamesDirectory = string.Empty;
    /// <summary>
    /// Gets or sets the PS4 games directory override.
    /// </summary>
    public string Ps4GamesDirectory { get => _ps4GamesDirectory; set => SetProperty(ref _ps4GamesDirectory, value); }

    private string _shadPs4ExecutablePath = string.Empty;
    /// <summary>
    /// Gets or sets the ShadPS4 executable path.
    /// </summary>
    public string ShadPs4ExecutablePath
    {
        get => _shadPs4ExecutablePath;
        set
        {
            if (SetProperty(ref _shadPs4ExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _ps4ExternalPkgExtractorPath = string.Empty;
    /// <summary>
    /// Gets or sets optional PS4 external PKG extractor path.
    /// </summary>
    public string Ps4ExternalPkgExtractorPath { get => _ps4ExternalPkgExtractorPath; set => SetProperty(ref _ps4ExternalPkgExtractorPath, value); }

    private bool _ps4FailIfDirectPkgExtractorMissing;
    /// <summary>
    /// Gets or sets whether direct PS4 PKG install should fail when extractor path is missing.
    /// </summary>
    public bool Ps4FailIfDirectPkgExtractorMissing { get => _ps4FailIfDirectPkgExtractorMissing; set => SetProperty(ref _ps4FailIfDirectPkgExtractorMissing, value); }

    private string _pcsx2ExecutablePath = string.Empty;
    public string Pcsx2ExecutablePath
    {
        get => _pcsx2ExecutablePath;
        set
        {
            if (SetProperty(ref _pcsx2ExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _pspEmulatorMode = string.Empty;
    public string PspEmulatorMode
    {
        get => _pspEmulatorMode;
        set
        {
            if (SetProperty(ref _pspEmulatorMode, value))
            {
                RaisePropertyChanged(nameof(IsPspRetroArchMode));
                RaisePropertyChanged(nameof(IsPspStandaloneMode));
                NotifyBannerStateChanged();
            }
        }
    }

    private string _ppssppExecutablePath = string.Empty;
    public string PpssppExecutablePath
    {
        get => _ppssppExecutablePath;
        set
        {
            if (SetProperty(ref _ppssppExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _retroArchExecutablePath = string.Empty;
    public string RetroArchExecutablePath
    {
        get => _retroArchExecutablePath;
        set
        {
            if (SetProperty(ref _retroArchExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _retroArchPpssppCorePath = string.Empty;
    public string RetroArchPpssppCorePath { get => _retroArchPpssppCorePath; set => SetProperty(ref _retroArchPpssppCorePath, value); }

    private bool _validateRetroArchPpssppAssets = true;
    public bool ValidateRetroArchPpssppAssets { get => _validateRetroArchPpssppAssets; set => SetProperty(ref _validateRetroArchPpssppAssets, value); }

    private bool _failInstallIfEmulatorNotReady;
    public bool FailInstallIfEmulatorNotReady { get => _failInstallIfEmulatorNotReady; set => SetProperty(ref _failInstallIfEmulatorNotReady, value); }

    private string _vita3kExecutablePath = string.Empty;
    public string Vita3kExecutablePath
    {
        get => _vita3kExecutablePath;
        set
        {
            if (SetProperty(ref _vita3kExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private bool _vitaFailIfEmulatorNotReady;
    public bool VitaFailIfEmulatorNotReady { get => _vitaFailIfEmulatorNotReady; set => SetProperty(ref _vitaFailIfEmulatorNotReady, value); }

    private bool _vitaInstallUpdatesAutomatically;
    public bool VitaInstallUpdatesAutomatically { get => _vitaInstallUpdatesAutomatically; set => SetProperty(ref _vitaInstallUpdatesAutomatically, value); }

    private bool _vitaInstallDlcAutomatically;
    public bool VitaInstallDlcAutomatically { get => _vitaInstallDlcAutomatically; set => SetProperty(ref _vitaInstallDlcAutomatically, value); }

    private string _switchEdenExecutablePath = string.Empty;
    public string SwitchEdenExecutablePath
    {
        get => _switchEdenExecutablePath;
        set
        {
            if (SetProperty(ref _switchEdenExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _azaharExecutablePath = string.Empty;
    public string AzaharExecutablePath
    {
        get => _azaharExecutablePath;
        set
        {
            if (SetProperty(ref _azaharExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _azaharPlusExecutablePath = string.Empty;
    public string AzaharPlusExecutablePath
    {
        get => _azaharPlusExecutablePath;
        set
        {
            if (SetProperty(ref _azaharPlusExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _dolphinExecutablePath = string.Empty;
    public string DolphinExecutablePath
    {
        get => _dolphinExecutablePath;
        set
        {
            if (SetProperty(ref _dolphinExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    private string _cemuExecutablePath = string.Empty;
    public string CemuExecutablePath
    {
        get => _cemuExecutablePath;
        set
        {
            if (SetProperty(ref _cemuExecutablePath, value))
            {
                NotifyBannerStateChanged();
            }
        }
    }

    public Models.PlatformMapping BuildMappingForSave()
    {
        if (_mapping == null)
        {
            return null;
        }

        _mapping.InstallScenario = InstallScenario;
        _mapping.SelfContained = SelfContained;
        _mapping.TargetImportFile = TargetImportFile;
        _mapping.InstallerSilentArgs = InstallerSilentArgs;
        _mapping.InstallerMode = InstallerMode;
        _mapping.AssociatedEmulatorId = AssociatedEmulatorId;
        _mapping.EmulatorCoreId = EmulatorCoreId;
        _mapping.EmulatorCoreName = EmulatorCoreName;
        _mapping.EmulatorCorePath = EmulatorCorePath;
        _mapping.EmulatorLaunchArgs = EmulatorLaunchArgs;
        _mapping.MusicRootPath = MusicRootPath;
        _mapping.InstallOst = InstallOst;
        _mapping.OstInstallLocation = OstInstallLocation;
        _mapping.BonusRootPath = BonusRootPath;
        _mapping.InstallBonus = InstallBonus;
        _mapping.BonusInstallLocation = BonusInstallLocation;
        _mapping.PreReqsRootPath = PreReqsRootPath;
        _mapping.InstallPreReqs = InstallPreReqs;
        _mapping.ExtractAfterDownload = ExtractAfterDownload;
        _mapping.ExtractionBehavior = ExtractionBehavior;
        _mapping.CustomInstallDirectory = ResolveGamesDirectoryForSave(GamesDirectory);
        _mapping.RomInstallRoot = RomInstallRoot;
        _mapping.RomArchivePolicy = RomArchivePolicy;
        _mapping.PluginKey = PluginKey;
        _mapping.PluginSettings = PluginSettings;
        _mapping.SupportedFileTypes = SupportedFileTypes;
        _mapping.PreferredLaunchExtensions = PreferredLaunchExtensions;
        _mapping.ArchiveHandlingMode = ArchiveHandlingMode;
        _mapping.UseGameSubdirectory = UseGameSubdirectory;
        _mapping.InstallAllMatchingFiles = InstallAllMatchingFiles;
        _mapping.InstallFromArchiveDirectly = InstallFromArchiveDirectly;
        _mapping.InstallLayoutMode = InstallLayoutMode;
        _mapping.ArtifactSelectionMode = ArtifactSelectionMode;
        _mapping.UseGeneralFallbackInstaller = UseGeneralFallbackInstaller;
        _mapping.Ps3GameDirectory = Ps3GameDirectory;
        _mapping.Rpcs3ExecutablePath = Rpcs3ExecutablePath;
        _mapping.InstallDlcAutomatically = InstallDlcAutomatically;
        _mapping.InstallUpdatesAutomatically = InstallUpdatesAutomatically;
        _mapping.Rpcs3LicenseDirectory = Rpcs3LicenseDirectory;
        _mapping.SkipRegionMismatchedDlc = SkipRegionMismatchedDlc;
        _mapping.SkipUnmatchedRapFiles = SkipUnmatchedRapFiles;
        _mapping.PreferMetadataBasedPackageMatching = PreferMetadataBasedPackageMatching;
        _mapping.Ps4GamesDirectory = Ps4GamesDirectory;
        _mapping.ShadPs4ExecutablePath = ShadPs4ExecutablePath;
        _mapping.Ps4ExternalPkgExtractorPath = Ps4ExternalPkgExtractorPath;
        _mapping.Ps4FailIfDirectPkgExtractorMissing = Ps4FailIfDirectPkgExtractorMissing;
        _mapping.Pcsx2ExecutablePath = Pcsx2ExecutablePath;
        _mapping.PspEmulatorMode = PspEmulatorMode;
        _mapping.PpssppExecutablePath = PpssppExecutablePath;
        _mapping.RetroArchExecutablePath = RetroArchExecutablePath;
        _mapping.RetroArchPpssppCorePath = RetroArchPpssppCorePath;
        _mapping.ValidateRetroArchPpssppAssets = ValidateRetroArchPpssppAssets;
        _mapping.FailInstallIfEmulatorNotReady = FailInstallIfEmulatorNotReady;
        _mapping.Vita3kExecutablePath = Vita3kExecutablePath;
        _mapping.VitaFailIfEmulatorNotReady = VitaFailIfEmulatorNotReady;
        _mapping.VitaInstallUpdatesAutomatically = VitaInstallUpdatesAutomatically;
        _mapping.VitaInstallDlcAutomatically = VitaInstallDlcAutomatically;
        _mapping.SwitchEdenExecutablePath = SwitchEdenExecutablePath;
        _mapping.AzaharExecutablePath = AzaharExecutablePath;
        _mapping.AzaharPlusExecutablePath = AzaharPlusExecutablePath;
        _mapping.DolphinExecutablePath = DolphinExecutablePath;
        _mapping.CemuExecutablePath = CemuExecutablePath;
        return _mapping;
    }

    public string PreReqsTargetSummary =>
        InstallPreReqs
            ? "Pre-requisites will install to the selected location."
            : "Pre-requisites are disabled.";

    /// <summary>
    /// Gets the available extraction behavior options.
    /// </summary>
    public IReadOnlyList<ExtractionBehavior> ExtractionBehaviorOptions { get; } =
        new List<ExtractionBehavior>(System.Enum.GetValues(typeof(ExtractionBehavior)).Cast<ExtractionBehavior>());

    public IReadOnlyList<InstallerMode> InstallerModeOptions { get; } =
        new List<InstallerMode>(System.Enum.GetValues(typeof(InstallerMode)).Cast<InstallerMode>());

    public IReadOnlyList<OptionalContentLocation> OptionalContentLocations { get; } =
        new List<OptionalContentLocation>(System.Enum.GetValues(typeof(OptionalContentLocation)).Cast<OptionalContentLocation>());

    public string TargetImportFilesSummary
    {
        get
        {
            if (!IsTargetFilesEnabled)
            {
                return "Extracted folder will be used directly as the ROM path.";
            }

            var entries = ParseTargetImportFiles(TargetImportFile);
            return entries.Count == 0
                ? "No target files configured. Import will use the extracted folder as fallback."
                : $"Priority order: {string.Join(", ", entries)}";
        }
    }

    private void UpdateTargetImportDerivedFields()
    {
        RaisePropertyChanged(nameof(TargetImportFilesSummary));
    }

    private static InstallTypeChoice ResolveDefaultInstallType(Models.PlatformMapping mapping)
    {
        if (InstallDestinationService.IsWindowsPlatform(mapping?.LaunchBoxPlatform ?? mapping?.RomMPlatform ?? string.Empty))
        {
            return InstallTypeChoice.Enhanced;
        }

        return InstallTypeChoice.Basic;
    }

    private static string ResolveGamesDirectory(string storedPath, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(storedPath) && !System.IO.Path.IsPathRooted(storedPath))
        {
            try
            {
                var root = RomMbox.Services.Paths.PluginPaths.GetLaunchBoxRootDirectory();
                if (!string.IsNullOrWhiteSpace(root))
                {
                    return System.IO.Path.Combine(root, storedPath);
                }
            }
            catch
            {
            }
        }

        return string.IsNullOrWhiteSpace(storedPath) ? fallback : storedPath;
    }

    private static string ResolveGamesDirectoryForSave(string gamesDirectory)
    {
        if (string.IsNullOrWhiteSpace(gamesDirectory))
        {
            return string.Empty;
        }

        try
        {
            var root = RomMbox.Services.Paths.PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return gamesDirectory;
            }

            var normalizedRoot = System.IO.Path.GetFullPath(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                + System.IO.Path.DirectorySeparatorChar;
            var normalizedPath = System.IO.Path.GetFullPath(gamesDirectory.Trim());

            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedPath.Substring(normalizedRoot.Length);
                return relative.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
            }
        }
        catch
        {
        }

        return gamesDirectory;
    }

    private static IReadOnlyList<string> ParseTargetImportFiles(string targetImportFile)
    {
        if (string.IsNullOrWhiteSpace(targetImportFile))
        {
            return Array.Empty<string>();
        }

        var separators = new[] { ',', ';', '|' };
        var values = targetImportFile
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return values.Count == 0 ? Array.Empty<string>() : values;
    }

    private static List<EmulatorOption> LoadEmulators()
    {
        var results = new List<EmulatorOption>();
        try
        {
            var dataManager = PluginHelper.DataManager;
            var emulators = dataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                if (emulator == null || string.IsNullOrWhiteSpace(emulator.Id))
                {
                    continue;
                }

                results.Add(new EmulatorOption
                {
                    Id = emulator.Id,
                    Name = emulator.Title ?? emulator.ApplicationPath ?? emulator.Id
                });
            }
        }
        catch
        {
        }

        return results
            .OrderBy(entry => entry.Name)
            .ToList();
    }

    private static string ResolveDefaultEmulatorId(string platformName)
    {
        try
        {
            var dataManager = PluginHelper.DataManager;
            var emulators = dataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var emulatorPlatform in platforms)
                {
                    if (string.Equals(emulatorPlatform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && emulatorPlatform?.IsDefault == true)
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var emulatorPlatform in platforms)
                {
                    if (string.Equals(emulatorPlatform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string ResolveDefaultPspEmulatorMode(Models.PlatformMapping mapping)
    {
        var combined = string.Join(" ", new[]
        {
            mapping?.AssociatedEmulatorId,
            mapping?.EmulatorCoreId,
            mapping?.EmulatorCoreName,
            mapping?.EmulatorCorePath,
            mapping?.RetroArchExecutablePath,
            mapping?.RetroArchPpssppCorePath
        });

        return combined.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0
            || combined.IndexOf("ppsspp", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrWhiteSpace(mapping?.RetroArchPpssppCorePath)
            ? "RetroArchPPSSPP"
            : "StandalonePPSSPP";
    }

    private string ResolveConfiguredValue(string configuredValue, string fieldKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue;
        }

        return _configDescriptor?.Fields?
            .FirstOrDefault(field => field != null && string.Equals(field.Key, fieldKey, StringComparison.OrdinalIgnoreCase))?
            .DefaultValue ?? string.Empty;
    }

    private string GetNormalizedInstallLayoutMode()
    {
        if (!string.IsNullOrWhiteSpace(InstallLayoutMode))
        {
            return InstallLayoutMode;
        }

        return UseGameSubdirectory ? "CreatePerGameSubfolder" : "UsePlatformRoot";
    }

    private void SetInstallLayoutMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        InstallLayoutMode = mode;
        UseGameSubdirectory = !string.Equals(mode, "UsePlatformRoot", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEmulatorFamilySelected(params string[] terms)
    {
        var name = SelectedEmulator?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || terms == null || terms.Length == 0)
        {
            return false;
        }

        return terms.Any(term =>
            !string.IsNullOrWhiteSpace(term)
            && name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private bool IsEmulatorFamilySelectedOrUnspecified(params string[] terms)
    {
        if (SelectedEmulator == null)
        {
            return true;
        }

        return IsEmulatorFamilySelected(terms);
    }

    private string ResolvePageSubtitle()
    {
        if (ShowShadPs4ExecutablePathField)
        {
            return "Configure ShadPS4 launch settings and direct PKG install tooling.";
        }

        if (ShowRpcs3ExecutablePathField)
        {
            return "Configure RPCS3 launch, install, and package handling settings.";
        }

        if (ShowRetroArchExecutablePathField || IsPspRetroArchMode)
        {
            return "Configure RetroArch integration, content handling, and emulator-specific options.";
        }

        return "Configure installation, emulator, and plugin behavior for this platform.";
    }

    private string ResolveBannerStatusText()
    {
        if (ShowShadPs4ExecutablePathField && !string.IsNullOrWhiteSpace(ShadPs4ExecutablePath))
        {
            return "ShadPS4 Path Configured";
        }

        if (ShowRpcs3ExecutablePathField && !string.IsNullOrWhiteSpace(Rpcs3ExecutablePath))
        {
            return "RPCS3 Path Configured";
        }

        if (ShowRetroArchExecutablePathField && !string.IsNullOrWhiteSpace(RetroArchExecutablePath))
        {
            return "RetroArch Path Configured";
        }

        if (ShowPpssppExecutablePathField && IsPspStandaloneMode && !string.IsNullOrWhiteSpace(PpssppExecutablePath))
        {
            return "PPSSPP Path Configured";
        }

        if (ShowPcsx2ExecutablePathField && !string.IsNullOrWhiteSpace(Pcsx2ExecutablePath))
        {
            return "PCSX2 Path Configured";
        }

        if (ShowVita3kExecutablePathField && !string.IsNullOrWhiteSpace(Vita3kExecutablePath))
        {
            return "Vita3K Path Configured";
        }

        if (ShowSwitchEdenExecutablePathField && !string.IsNullOrWhiteSpace(SwitchEdenExecutablePath))
        {
            return "Eden Path Configured";
        }

        if (ShowAzaharExecutablePathField && !string.IsNullOrWhiteSpace(AzaharExecutablePath))
        {
            return "Azahar Path Configured";
        }

        if (ShowAzaharPlusExecutablePathField && !string.IsNullOrWhiteSpace(AzaharPlusExecutablePath))
        {
            return "AzaharPlus Path Configured";
        }

        if (ShowDolphinExecutablePathField && !string.IsNullOrWhiteSpace(DolphinExecutablePath))
        {
            return "Dolphin Path Configured";
        }

        if (ShowCemuExecutablePathField && !string.IsNullOrWhiteSpace(CemuExecutablePath))
        {
            return "Cemu Path Configured";
        }

        return string.Empty;
    }

    private void NotifyBannerStateChanged()
    {
        RaisePropertyChanged(nameof(SelectedEmulator));
        RaisePropertyChanged(nameof(SelectedEmulatorName));
        RaisePropertyChanged(nameof(PageSubtitle));
        RaisePropertyChanged(nameof(BannerStatusText));
        RaisePropertyChanged(nameof(ShowBannerStatusBadge));
    }

    private bool HasConfigField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (_configDescriptor?.Fields == null || _configDescriptor.Fields.Count == 0)
        {
            return false;
        }

        return _configDescriptor.Fields.Any(field =>
            field != null &&
            string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}
