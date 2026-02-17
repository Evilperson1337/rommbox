using System.Collections.Generic;
using System.Linq;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Install;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model for the platform install configuration dialog.
/// </summary>
public sealed class PlatformInstallConfigViewModel : ObservableObject
{
    /// <summary>
    /// Initializes the view model from the given platform mapping.
    /// </summary>
    /// <param name="mapping">The platform mapping to edit.</param>
    /// <param name="defaultInstallDirectory">Default install directory for the platform.</param>
    public PlatformInstallConfigViewModel(Models.PlatformMapping mapping, string defaultInstallDirectory)
    {
        IsWindowsPlatform = InstallDestinationService.IsWindowsPlatform(mapping?.LaunchBoxPlatform ?? mapping?.RomMPlatform ?? string.Empty);
        DisableAutoImport = mapping?.DisableAutoImport ?? false;
        InstallScenarioOptions = new List<InstallScenario>(System.Enum.GetValues(typeof(InstallScenario)).Cast<InstallScenario>());
        InstallScenario = mapping?.InstallScenario ?? InstallScenario.Basic;
        TargetImportFile = mapping?.TargetImportFile ?? string.Empty;
        InstallerSilentArgs = mapping?.InstallerSilentArgs ?? string.Empty;
        InstallerModeOptions = new List<InstallerMode>(System.Enum.GetValues(typeof(InstallerMode)).Cast<InstallerMode>());
        InstallerMode = mapping?.InstallerMode ?? InstallerMode.Manual;
        MusicRootPath = mapping?.MusicRootPath ?? string.Empty;
        InstallOst = mapping?.InstallOst ?? false;
        BonusRootPath = mapping?.BonusRootPath ?? string.Empty;
        InstallBonus = mapping?.InstallBonus ?? false;
        PreReqsRootPath = mapping?.PreReqsRootPath ?? string.Empty;
        InstallPreReqs = mapping?.InstallPreReqs ?? false;
        ExtractAfterDownload = mapping?.ExtractAfterDownload ?? false;
        ExtractionBehavior = mapping?.ExtractionBehavior ?? ExtractionBehavior.Subfolder;
        DefaultInstallDirectory = defaultInstallDirectory ?? string.Empty;
        CustomInstallDirectory = string.IsNullOrWhiteSpace(mapping?.CustomInstallDirectory)
            ? DefaultInstallDirectory
            : mapping.CustomInstallDirectory;
        _customInstallDirectory = CustomInstallDirectory;
        RaisePropertyChanged(nameof(HasCustomInstallDirectory));
    }

    /// <summary>
    /// Gets the default install directory for the platform.
    /// </summary>
    public string DefaultInstallDirectory { get; }

    /// <summary>
    /// Gets whether the platform is considered a Windows install platform.
    /// </summary>
    public bool IsWindowsPlatform { get; }

    private bool _disableAutoImport;
    /// <summary>
    /// Gets or sets whether auto-import is disabled for the platform.
    /// </summary>
    public bool DisableAutoImport { get => _disableAutoImport; set => SetProperty(ref _disableAutoImport, value); }

    /// <summary>
    /// Gets whether the custom install directory matches the default.
    /// </summary>
    public bool IsGameDirectoryDefault => string.Equals(CustomInstallDirectory, DefaultInstallDirectory, System.StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Gets whether a custom install directory is configured.
    /// </summary>
    public bool HasCustomInstallDirectory => !string.IsNullOrWhiteSpace(CustomInstallDirectory);

    /// <summary>
    /// Gets the available install scenario options.
    /// </summary>
    public IReadOnlyList<InstallScenario> InstallScenarioOptions { get; }

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
                RaisePropertyChanged(nameof(IsEnhanced));
                RaisePropertyChanged(nameof(IsBasicOrInstaller));
            }
        }
    }

    private string _targetImportFile = string.Empty;
    /// <summary>
    /// Gets or sets the target import file for the platform.
    /// </summary>
    public string TargetImportFile { get => _targetImportFile; set => SetProperty(ref _targetImportFile, value); }

    private string _installerSilentArgs = string.Empty;
    /// <summary>
    /// Gets or sets silent install arguments for installer packages.
    /// </summary>
    public string InstallerSilentArgs { get => _installerSilentArgs; set => SetProperty(ref _installerSilentArgs, value); }

    /// <summary>
    /// Gets the available installer modes.
    /// </summary>
    public IReadOnlyList<InstallerMode> InstallerModeOptions { get; }

    private InstallerMode _installerMode = InstallerMode.Manual;
    /// <summary>
    /// Gets or sets the installer mode.
    /// </summary>
    public InstallerMode InstallerMode { get => _installerMode; set => SetProperty(ref _installerMode, value); }

    private string _musicRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the music root path.
    /// </summary>
    public string MusicRootPath { get => _musicRootPath; set => SetProperty(ref _musicRootPath, value); }

    private bool _installOst;
    /// <summary>
    /// Gets or sets whether soundtrack content should be installed.
    /// </summary>
    public bool InstallOst { get => _installOst; set => SetProperty(ref _installOst, value); }

    private string _bonusRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the bonus content root path.
    /// </summary>
    public string BonusRootPath { get => _bonusRootPath; set => SetProperty(ref _bonusRootPath, value); }

    private bool _installBonus;
    /// <summary>
    /// Gets or sets whether bonus content should be installed.
    /// </summary>
    public bool InstallBonus { get => _installBonus; set => SetProperty(ref _installBonus, value); }

    private string _preReqsRootPath = string.Empty;
    /// <summary>
    /// Gets or sets the prerequisites root path.
    /// </summary>
    public string PreReqsRootPath { get => _preReqsRootPath; set => SetProperty(ref _preReqsRootPath, value); }

    private bool _installPreReqs;
    /// <summary>
    /// Gets or sets whether prerequisites should be installed.
    /// </summary>
    public bool InstallPreReqs { get => _installPreReqs; set => SetProperty(ref _installPreReqs, value); }

    private bool _extractAfterDownload = false;
    /// <summary>
    /// Gets or sets whether extraction should run after download.
    /// </summary>
    public bool ExtractAfterDownload { get => _extractAfterDownload; set => SetProperty(ref _extractAfterDownload, value); }

    private ExtractionBehavior _extractionBehavior = ExtractionBehavior.Subfolder;
    /// <summary>
    /// Gets or sets the extraction behavior.
    /// </summary>
    public ExtractionBehavior ExtractionBehavior { get => _extractionBehavior; set => SetProperty(ref _extractionBehavior, value); }

    private string _customInstallDirectory = string.Empty;
    /// <summary>
    /// Gets or sets the custom install directory and updates dependent flags.
    /// </summary>
    public string CustomInstallDirectory
    {
        get => _customInstallDirectory;
        set
        {
            if (SetProperty(ref _customInstallDirectory, value))
            {
                RaisePropertyChanged(nameof(IsGameDirectoryDefault));
                RaisePropertyChanged(nameof(HasCustomInstallDirectory));
            }
        }
    }

    /// <summary>
    /// Gets whether the install scenario is enhanced.
    /// </summary>
    public bool IsEnhanced => InstallScenario == InstallScenario.Enhanced;
    /// <summary>
    /// Gets whether the install scenario is basic or installer based.
    /// </summary>
    public bool IsBasicOrInstaller => InstallScenario == InstallScenario.Basic || InstallScenario == InstallScenario.Installer;

    /// <summary>
    /// Gets the available extraction behavior options.
    /// </summary>
    public IReadOnlyList<ExtractionBehavior> ExtractionBehaviorOptions { get; } =
        new List<ExtractionBehavior>(System.Enum.GetValues(typeof(ExtractionBehavior)).Cast<ExtractionBehavior>());
}
