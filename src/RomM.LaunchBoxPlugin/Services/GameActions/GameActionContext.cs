using System;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    /// <summary>
    /// Carries game-related data for the game actions UI.
    /// </summary>
    internal sealed class GameActionContext
    {
        /// <summary>
        /// Gets or sets the LaunchBox game instance.
        /// </summary>
        public IGame Game { get; set; }
        /// <summary>
        /// Gets or sets the RomM ROM identifier.
        /// </summary>
        public string RommRomId { get; set; }
        /// <summary>
        /// Gets or sets the RomM server URL.
        /// </summary>
        public string ServerUrl { get; set; }
        /// <summary>
        /// Gets or sets the platform name for display.
        /// </summary>
        public string PlatformName { get; set; }
        /// <summary>
        /// Gets or sets whether the game is installed locally.
        /// </summary>
        public bool IsInstalled { get; set; }
        /// <summary>
        /// Gets or sets the URL used to play the game on RomM.
        /// </summary>
        public string PlayUrl { get; set; }
        /// <summary>
        /// Gets or sets whether play-on-RomM is available.
        /// </summary>
        public bool CanPlayOnRomM { get; set; }
        /// <summary>
        /// Gets or sets the release date for display.
        /// </summary>
        public DateTime? ReleaseDate { get; set; }
        /// <summary>
        /// Gets or sets the genre list for display.
        /// </summary>
        public string Genres { get; set; }
        /// <summary>
        /// Gets or sets the description text for display.
        /// </summary>
        public string Description { get; set; }
    }
}
