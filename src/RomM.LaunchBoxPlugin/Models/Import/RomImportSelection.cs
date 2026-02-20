using RomMbox.Models.Romm;

namespace RomMbox.Models.Import
{
    /// <summary>
    /// Represents a single ROM selection from the import UI, including flags
    /// for install/download actions.
    /// </summary>
    internal sealed class RomImportSelection
    {
        /// <summary>
        /// Creates a selection with default action (Import) and no save download.
        /// </summary>
        public RomImportSelection(RommRom rom, bool installLocally)
        {
            Rom = rom;
            InstallLocally = installLocally;
            DownloadSaves = false;
            Action = ImportAction.Import;
        }

        /// <summary>
        /// Creates a selection with optional save download.
        /// </summary>
        public RomImportSelection(RommRom rom, bool installLocally, bool downloadSaves)
        {
            Rom = rom;
            InstallLocally = installLocally;
            DownloadSaves = downloadSaves;
            Action = ImportAction.Import;
        }

        /// <summary>
        /// Creates a selection with explicit action and flags.
        /// </summary>
        public RomImportSelection(RommRom rom, bool installLocally, bool downloadSaves, ImportAction action)
        {
            Rom = rom;
            InstallLocally = installLocally;
            DownloadSaves = downloadSaves;
            Action = action;
        }

        /// <summary>
        /// RomM ROM data that the selection refers to.
        /// </summary>
        public RommRom Rom { get; }

        /// <summary>
        /// Whether the ROM should be installed locally.
        /// </summary>
        public bool InstallLocally { get; }

        /// <summary>
        /// Whether saves should be downloaded after import.
        /// </summary>
        public bool DownloadSaves { get; }


        /// <summary>
        /// Import action chosen for this ROM.
        /// </summary>
        public ImportAction Action { get; }
    }
}
