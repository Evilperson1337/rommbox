using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    public PlatformInstallConfigViewModel(Models.PlatformMapping mapping, string defaultInstallDirectory, Action onSave = null, Action onBack = null)
    {
        _onSave = onSave;
        _onBack = onBack;
        _mapping = mapping;
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
    public string AssociatedEmulatorId { get => _associatedEmulatorId; set => SetProperty(ref _associatedEmulatorId, value); }

    public ObservableCollection<EmulatorOption> Emulators { get; }

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
                RaisePropertyChanged(nameof(SelectedEmulator));
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

    public bool IsGamesDirectoryValid => !string.IsNullOrWhiteSpace(GamesDirectory);

    public bool CanSave => IsGamesDirectoryValid;

    public RelayCommand SaveCommand { get; }

    public RelayCommand BackCommand { get; }

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
}
