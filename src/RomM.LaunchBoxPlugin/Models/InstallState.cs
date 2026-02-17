using System;

namespace RomMbox.Models
{
    /// <summary>
    /// Persistence model for local install state tracked per LaunchBox game.
    /// </summary>
    internal sealed class InstallState
    {
        /// <summary>
        /// LaunchBox game identifier (primary key).
        /// </summary>
        public string LaunchBoxGameId { get; set; }

        /// <summary>
        /// RomM ROM identifier.
        /// </summary>
        public string RommRomId { get; set; }

        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        public string RommPlatformId { get; set; }

        /// <summary>
        /// RomM server URL used during install.
        /// </summary>
        public string ServerUrl { get; set; }

        /// <summary>
        /// Remote MD5 hash reported by RomM.
        /// </summary>
        public string RemoteMd5 { get; set; }

        /// <summary>
        /// Local MD5 hash computed from the installed file.
        /// </summary>
        public string LocalMd5 { get; set; }

        /// <summary>
        /// Windows install type (e.g., Installer, Portable) when applicable.
        /// </summary>
        public string WindowsInstallType { get; set; }

        /// <summary>
        /// Installed executable or content path on disk.
        /// </summary>
        public string InstalledPath { get; set; }

        /// <summary>
        /// Local archive path when the original download is retained.
        /// </summary>
        public string ArchivePath { get; set; }

        /// <summary>
        /// Root folder for the install, if applicable.
        /// </summary>
        public string InstallRootPath { get; set; }

        /// <summary>
        /// Whether the game is currently installed.
        /// </summary>
        public bool IsInstalled { get; set; }

        /// <summary>
        /// When the game was installed (UTC).
        /// </summary>
        public DateTimeOffset? InstalledUtc { get; set; }

        /// <summary>
        /// When the install state was last validated against disk (UTC).
        /// </summary>
        public DateTimeOffset? LastValidatedUtc { get; set; }
    }
}
