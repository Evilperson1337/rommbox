using System;
using RomMbox.Models;
using RomMbox.Models.Romm;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    /// <summary>
    /// Context model for the game properties dialog.
    /// </summary>
    internal sealed class GamePropertiesContext
    {
        /// <summary>
        /// LaunchBox game reference.
        /// </summary>
        public IGame Game { get; set; }

        /// <summary>
        /// RomM ROM details, if available.
        /// </summary>
        public RommRom RommRom { get; set; }

        /// <summary>
        /// Install state snapshot, if available.
        /// </summary>
        public InstallState InstallState { get; set; }

        /// <summary>
        /// RomM ROM ID.
        /// </summary>
        public string RommRomId { get; set; }

        /// <summary>
        /// RomM platform ID.
        /// </summary>
        public string RommPlatformId { get; set; }

        /// <summary>
        /// RomM server URL.
        /// </summary>
        public string ServerUrl { get; set; }

        /// <summary>
        /// Resolved LaunchBox platform name.
        /// </summary>
        public string LaunchBoxPlatformName { get; set; }
    }
}
