using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Models;

/// <summary>
/// Represents a row of download settings bound in the UI.
/// </summary>
public sealed class DownloadSettingsRow : ObservableObject
{
    private string _rommPlatformId = string.Empty;
    /// <summary>
    /// Gets or sets the RomM platform identifier.
    /// </summary>
    public string RommPlatformId { get => _rommPlatformId; set => SetProperty(ref _rommPlatformId, value); }

    private string _rommPlatformName = string.Empty;
    /// <summary>
    /// Gets or sets the human-readable RomM platform name.
    /// </summary>
    public string RommPlatformName { get => _rommPlatformName; set => SetProperty(ref _rommPlatformName, value); }

    private string _launchBoxPlatformName = string.Empty;
    /// <summary>
    /// Gets or sets the associated LaunchBox platform name.
    /// </summary>
    public string LaunchBoxPlatformName { get => _launchBoxPlatformName; set => SetProperty(ref _launchBoxPlatformName, value); }

    private bool _autoMapped;
    /// <summary>
    /// Gets or sets whether the platform was auto-mapped.
    /// </summary>
    public bool AutoMapped { get => _autoMapped; set => SetProperty(ref _autoMapped, value); }

    private bool _extractAfterDownload = false;
    /// <summary>
    /// Gets or sets whether archives should be extracted after download.
    /// </summary>
    public bool ExtractAfterDownload { get => _extractAfterDownload; set => SetProperty(ref _extractAfterDownload, value); }

    private ExtractionBehavior _extractionBehavior = ExtractionBehavior.Subfolder;
    /// <summary>
    /// Gets or sets the extraction behavior for downloaded archives.
    /// </summary>
    public ExtractionBehavior ExtractionBehavior { get => _extractionBehavior; set => SetProperty(ref _extractionBehavior, value); }

    private InstallScenario _installScenario = InstallScenario.Basic;
    /// <summary>
    /// Gets or sets the install scenario for this platform.
    /// </summary>
    public InstallScenario InstallScenario { get => _installScenario; set => SetProperty(ref _installScenario, value); }

    private string _targetImportFile = string.Empty;
    /// <summary>
    /// Gets or sets the target file to import after installation or extraction.
    /// </summary>
    public string TargetImportFile { get => _targetImportFile; set => SetProperty(ref _targetImportFile, value); }

    private string _installerSilentArgs = string.Empty;
    /// <summary>
    /// Gets or sets silent install arguments for installer-based packages.
    /// </summary>
    public string InstallerSilentArgs { get => _installerSilentArgs; set => SetProperty(ref _installerSilentArgs, value); }
}
