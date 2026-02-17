using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// RomM ROM record returned by the API.
    /// </summary>
    internal sealed class RommRom
    {
        /// <summary>
        /// RomM ROM identifier.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string Id { get; set; }

        /// <summary>
        /// Primary title from RomM metadata.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// Fallback name provided by the server.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        [JsonPropertyName("platform_id")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string PlatformId { get; set; }

        /// <summary>
        /// Filesystem path on the RomM server (if available).
        /// </summary>
        [JsonPropertyName("fs_path")]
        public string FsPath { get; set; }

        /// <summary>
        /// Filename on the RomM server (if available).
        /// </summary>
        [JsonPropertyName("fs_name")]
        public string FsName { get; set; }

        /// <summary>
        /// Full path on the RomM server.
        /// </summary>
        [JsonPropertyName("full_path")]
        public string FullPath { get; set; }

        /// <summary>
        /// MD5 hash for the ROM content.
        /// </summary>
        [JsonPropertyName("md5_hash")]
        public string Md5 { get; set; }

        /// <summary>
        /// Release date provided by RomM metadata.
        /// </summary>
        [JsonPropertyName("release_date")]
        public DateTimeOffset? ReleaseDate { get; set; }

        /// <summary>
        /// Created timestamp from RomM.
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// Region label from metadata.
        /// </summary>
        [JsonPropertyName("region")]
        public string Region { get; set; }

        /// <summary>
        /// Genre list from RomM metadata.
        /// </summary>
        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }

        /// <summary>
        /// Developer name from metadata.
        /// </summary>
        [JsonPropertyName("developer")]
        public string Developer { get; set; }

        /// <summary>
        /// Publisher name from metadata.
        /// </summary>
        [JsonPropertyName("publisher")]
        public string Publisher { get; set; }

        /// <summary>
        /// Long description text.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// Short summary text.
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        /// <summary>
        /// Revision/version information.
        /// </summary>
        [JsonPropertyName("revision")]
        public string Revision { get; set; }

        /// <summary>
        /// Display name for the platform.
        /// </summary>
        [JsonPropertyName("platform_display_name")]
        public string PlatformDisplayName { get; set; }

        /// <summary>
        /// YouTube video id for trailers.
        /// </summary>
        [JsonPropertyName("youtube_video_id")]
        public string YoutubeVideoId { get; set; }

        /// <summary>
        /// Age rating label.
        /// </summary>
        [JsonPropertyName("rating")]
        public string Rating { get; set; }

        /// <summary>
        /// Download payload metadata for this ROM.
        /// </summary>
        [JsonPropertyName("payload")]
        public RommPayload Payload { get; set; }

        /// <summary>
        /// File id list used to select specific files for download.
        /// </summary>
        [JsonPropertyName("file_ids")]
        public List<int> FileIds { get; set; }

        /// <summary>
        /// Media URLs associated with the ROM.
        /// </summary>
        [JsonPropertyName("media")]
        public RommMedia Media { get; set; }

        /// <summary>
        /// Direct cover URL provided by the API.
        /// </summary>
        [JsonPropertyName("url_cover")]
        public string UrlCover { get; set; }

        /// <summary>
        /// Path to large cover image.
        /// </summary>
        [JsonPropertyName("path_cover_large")]
        public string PathCoverLarge { get; set; }

        /// <summary>
        /// Path to small cover image.
        /// </summary>
        [JsonPropertyName("path_cover_small")]
        public string PathCoverSmall { get; set; }

        /// <summary>
        /// Screenshot URLs merged from metadata sources.
        /// </summary>
        [JsonPropertyName("merged_screenshots")]
        public List<string> MergedScreenshots { get; set; }

        /// <summary>
        /// Additional metadata block.
        /// </summary>
        [JsonPropertyName("metadatum")]
        public RommMetadata Metadatum { get; set; }

        /// <summary>
        /// Preferred title for display purposes.
        /// </summary>
        [JsonIgnore]
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                return Payload?.FileName ?? string.Empty;
            }
        }
    }
}
