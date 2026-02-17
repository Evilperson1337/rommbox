using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Download payload metadata for a ROM.
    /// </summary>
    internal sealed class RommPayload
    {
        /// <summary>
        /// File name to use when saving the download.
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// Size of the payload in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// File extension reported by the server.
        /// </summary>
        [JsonPropertyName("extension")]
        public string Extension { get; set; }

        /// <summary>
        /// Direct download URL for the payload.
        /// </summary>
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; }
    }
}
