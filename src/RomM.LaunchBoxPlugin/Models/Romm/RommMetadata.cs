using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Metadata fields returned by RomM for a ROM/game.
    /// These values are used to populate LaunchBox fields when available.
    /// </summary>
    internal sealed class RommMetadata
    {
        /// <summary>
        /// List of genre names provided by RomM (e.g., "Action", "RPG").
        /// </summary>
        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }

        /// <summary>
        /// Companies associated with the game (developer/publisher, depending on source data).
        /// </summary>
        [JsonPropertyName("companies")]
        public List<string> Companies { get; set; }

        /// <summary>
        /// Franchise/series names the game belongs to.
        /// </summary>
        [JsonPropertyName("franchises")]
        public List<string> Franchises { get; set; }

        /// <summary>
        /// Gameplay modes, typically including single/multi/co-op variants.
        /// </summary>
        [JsonPropertyName("game_modes")]
        public List<string> GameModes { get; set; }

        /// <summary>
        /// Age rating labels from the metadata provider (ESRB/PEGI/etc.).
        /// </summary>
        [JsonPropertyName("age_ratings")]
        public List<string> AgeRatings { get; set; }

        /// <summary>
        /// Player count value provided as a string to preserve original formatting.
        /// </summary>
        [JsonPropertyName("player_count")]
        public string PlayerCount { get; set; }

        /// <summary>
        /// First release date expressed as a Unix timestamp (seconds or milliseconds).
        /// </summary>
        [JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; set; }

        /// <summary>
        /// Average rating score (0-100) from the metadata provider.
        /// </summary>
        [JsonPropertyName("average_rating")]
        public double? AverageRating { get; set; }

        /// <summary>
        /// Optional LaunchBox game id to copy authoritative metadata from.
        /// </summary>
        [JsonPropertyName("launchbox_id")]
        public int? LaunchBoxId { get; set; }
    }
}
