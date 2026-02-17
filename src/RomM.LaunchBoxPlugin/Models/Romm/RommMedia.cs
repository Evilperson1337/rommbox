using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Media URLs associated with a RomM ROM.
    /// </summary>
    internal sealed class RommMedia
    {
        /// <summary>
        /// Box front image URL.
        /// </summary>
        [JsonPropertyName("box_front_url")]
        public string BoxFrontUrl { get; set; }

        /// <summary>
        /// Screenshot image URL.
        /// </summary>
        [JsonPropertyName("screenshot_url")]
        public string ScreenshotUrl { get; set; }

        /// <summary>
        /// Cover image URL.
        /// </summary>
        [JsonPropertyName("url_cover")]
        public string CoverUrl { get; set; }
    }
}
