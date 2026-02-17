using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// RomM platform record returned by the API.
    /// </summary>
    internal sealed class RommPlatform
    {
        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string Id { get; set; }

        /// <summary>
        /// Display name for the platform.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
