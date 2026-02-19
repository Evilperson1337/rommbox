using System;
using System.Runtime.Serialization;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Settings
{
    /// <summary>
    /// Serializable settings persisted for the RomM plugin.
    /// </summary>
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Name = "logLevel", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the log level name persisted in settings.
        /// </summary>
        public string LogLevelName { get; set; } = "Debug";

        [DataMember(Name = "serverUrl", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the RomM server URL.
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        [DataMember(Name = "hasSavedCredentials", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether credentials are stored locally.
        /// </summary>
        public bool HasSavedCredentials { get; set; }

        [DataMember(Name = "useSavedCredentials", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether saved credentials should be used automatically.
        /// </summary>
        public bool UseSavedCredentials { get; set; } = true;

        [DataMember(Name = "allowInvalidTls", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether invalid TLS certificates are allowed.
        /// </summary>
        public bool AllowInvalidTls { get; set; }

        [DataMember(Name = "connectionTimeoutSeconds", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the connection timeout in seconds.
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; } = 10;

        [DataMember(Name = "platformMappings", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets saved platform mappings.
        /// </summary>
        public PlatformMapping[] PlatformMappings { get; set; } = Array.Empty<PlatformMapping>();

        [DataMember(Name = "platformAliases", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets saved platform aliases.
        /// </summary>
        public PlatformAlias[] PlatformAliases { get; set; } = Array.Empty<PlatformAlias>();

        [DataMember(Name = "excludedRommPlatformIds", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the RomM platform IDs excluded from mapping.
        /// </summary>
        public string[] ExcludedRommPlatformIds { get; set; } = Array.Empty<string>();

        [DataMember(Name = "sevenZipPath", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the 7-Zip executable path.
        /// </summary>
        public string SevenZipPath { get; set; } = string.Empty;

        [DataMember(Name = "useSevenZipFallback", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether 7-Zip fallback extraction is enabled.
        /// </summary>
        public bool? UseSevenZipFallback { get; set; }

        [DataMember(Name = "keepArchivesAfterExtraction", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether archives are kept after extraction.
        /// </summary>
        public bool? KeepArchivesAfterExtraction { get; set; }

        [DataMember(Name = "promptForWindowsInstallDirectory", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets whether the user should be prompted for Windows install directories.
        /// </summary>
        public bool? PromptForWindowsInstallDirectory { get; set; }

        [DataMember(Name = "defaultWindowsInstallDirectory", EmitDefaultValue = false)]
        /// <summary>
        /// Gets or sets the default Windows install directory.
        /// </summary>
        public string DefaultWindowsInstallDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Parses the configured log level name into the <see cref="LogLevel"/> enum.
        /// </summary>
        /// <returns>The parsed log level, or <see cref="LogLevel.Debug"/> on failure.</returns>
        public LogLevel GetLogLevel()
        {
            if (Enum.TryParse(LogLevelName, true, out LogLevel parsed))
            {
                return parsed;
            }

            return LogLevel.Debug;
        }

        /// <summary>
        /// Gets the configured 7-Zip path or empty when not set.
        /// </summary>
        public string GetSevenZipPath() => SevenZipPath ?? string.Empty;

        /// <summary>
        /// Gets whether 7-Zip fallback extraction is enabled.
        /// </summary>
        public bool GetUseSevenZipFallback() => UseSevenZipFallback ?? true;

        /// <summary>
        /// Gets whether archives should be kept after extraction.
        /// </summary>
        public bool GetKeepArchivesAfterExtraction() => KeepArchivesAfterExtraction ?? false;

        /// <summary>
        /// Gets whether the user should be prompted for Windows install directories.
        /// </summary>
        public bool GetPromptForWindowsInstallDirectory() => PromptForWindowsInstallDirectory ?? true;

        /// <summary>
        /// Gets the default Windows install directory.
        /// </summary>
        public string GetDefaultWindowsInstallDirectory() => DefaultWindowsInstallDirectory ?? string.Empty;

        /// <summary>
        /// Ensures default values are applied to nullable or missing fields.
        /// </summary>
        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(LogLevelName))
            {
                LogLevelName = "Debug";
            }

            if (ConnectionTimeoutSeconds <= 0)
            {
                ConnectionTimeoutSeconds = 10;
            }

            if (PlatformMappings == null)
            {
                PlatformMappings = Array.Empty<PlatformMapping>();
            }

            EnsurePlatformMappingDefaults();

            if (PlatformAliases == null)
            {
                PlatformAliases = Array.Empty<PlatformAlias>();
            }

            if (ExcludedRommPlatformIds == null)
            {
                ExcludedRommPlatformIds = Array.Empty<string>();
            }

            if (SevenZipPath == null)
            {
                SevenZipPath = string.Empty;
            }

            if (UseSevenZipFallback == null)
            {
                UseSevenZipFallback = true;
            }

            if (KeepArchivesAfterExtraction == null)
            {
                KeepArchivesAfterExtraction = false;
            }

            if (PromptForWindowsInstallDirectory == null)
            {
                PromptForWindowsInstallDirectory = true;
            }

            if (DefaultWindowsInstallDirectory == null)
            {
                DefaultWindowsInstallDirectory = string.Empty;
            }
        }

        /// <summary>
        /// Ensures platform mappings have default values for required fields.
        /// </summary>
        private void EnsurePlatformMappingDefaults()
        {
            foreach (var mapping in PlatformMappings)
            {
                if (mapping == null)
                {
                    continue;
                }

                mapping.ExtractAfterDownload = false;

                // DisableAutoImport defaults to false.

                if (!Enum.IsDefined(typeof(ExtractionBehavior), mapping.ExtractionBehavior))
                {
                    mapping.ExtractionBehavior = ExtractionBehavior.Subfolder;
                }

                if (!Enum.IsDefined(typeof(InstallScenario), mapping.InstallScenario))
                {
                    mapping.InstallScenario = InstallScenario.Basic;
                }

                if (mapping.TargetImportFile == null)
                {
                    mapping.TargetImportFile = string.Empty;
                }

                if (mapping.InstallerSilentArgs == null)
                {
                    mapping.InstallerSilentArgs = string.Empty;
                }

                if (!Enum.IsDefined(typeof(InstallerMode), mapping.InstallerMode))
                {
                    mapping.InstallerMode = InstallerMode.Manual;
                }

                if (mapping.MusicRootPath == null)
                {
                    mapping.MusicRootPath = string.Empty;
                }

                if (mapping.BonusRootPath == null)
                {
                    mapping.BonusRootPath = string.Empty;
                }

                if (mapping.PreReqsRootPath == null)
                {
                    mapping.PreReqsRootPath = string.Empty;
                }

                if (mapping.CustomInstallDirectory == null)
                {
                    mapping.CustomInstallDirectory = string.Empty;
                }
            }
        }
    }
}
