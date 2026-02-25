using System;
using RomMbox.Services.Install;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Models
{
    /// <summary>
    /// Represents an executable candidate entry for selection.
    /// </summary>
    public sealed class ExecutableCandidateRow : ObservableObject
    {
        private bool _isRecommended;
        private string _fileName = string.Empty;
        private string _fullPath = string.Empty;
        private string _displayPath = string.Empty;
        private string _fileSizeDisplay = string.Empty;
        private long _fileSizeBytes;
        private string _version = string.Empty;
        private string _lastModifiedDisplay = string.Empty;
        private DateTime _lastModified;
        private string _architecture = string.Empty;
        private ExecutableArchitecture _architectureValue = ExecutableArchitecture.Unknown;

        /// <summary>
        /// Gets or sets whether this candidate is recommended.
        /// </summary>
        public bool IsRecommended { get => _isRecommended; set => SetProperty(ref _isRecommended, value); }

        /// <summary>
        /// Gets or sets the executable file name.
        /// </summary>
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

        /// <summary>
        /// Gets or sets the full file path.
        /// </summary>
        public string FullPath { get => _fullPath; set => SetProperty(ref _fullPath, value); }

        /// <summary>
        /// Gets or sets the display path (relative when available).
        /// </summary>
        public string DisplayPath { get => _displayPath; set => SetProperty(ref _displayPath, value); }

        /// <summary>
        /// Gets or sets the formatted file size.
        /// </summary>
        public string FileSizeDisplay { get => _fileSizeDisplay; set => SetProperty(ref _fileSizeDisplay, value); }

        /// <summary>
        /// Gets or sets the raw file size in bytes.
        /// </summary>
        public long FileSizeBytes { get => _fileSizeBytes; set => SetProperty(ref _fileSizeBytes, value); }

        /// <summary>
        /// Gets or sets the file version string.
        /// </summary>
        public string Version { get => _version; set => SetProperty(ref _version, value); }

        /// <summary>
        /// Gets or sets the formatted last-modified timestamp.
        /// </summary>
        public string LastModifiedDisplay { get => _lastModifiedDisplay; set => SetProperty(ref _lastModifiedDisplay, value); }

        /// <summary>
        /// Gets or sets the last modified timestamp.
        /// </summary>
        public DateTime LastModified { get => _lastModified; set => SetProperty(ref _lastModified, value); }

        /// <summary>
        /// Gets or sets the architecture display label.
        /// </summary>
        public string Architecture { get => _architecture; set => SetProperty(ref _architecture, value); }

        /// <summary>
        /// Gets or sets the architecture enum value.
        /// </summary>
        public ExecutableArchitecture ArchitectureValue { get => _architectureValue; set => SetProperty(ref _architectureValue, value); }
    }
}
