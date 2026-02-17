using System;
using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Save file metadata returned by the RomM API.
    /// </summary>
    internal sealed class RommSave
    {
        /// <summary>
        /// Save record identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// RomM ROM identifier for the save.
        /// </summary>
        [JsonPropertyName("rom_id")]
        public int RomId { get; set; }

        /// <summary>
        /// RomM user identifier for the save owner.
        /// </summary>
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>
        /// Original file name.
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// File name with tags stripped.
        /// </summary>
        [JsonPropertyName("file_name_no_tags")]
        public string FileNameNoTags { get; set; }

        /// <summary>
        /// File name without extension.
        /// </summary>
        [JsonPropertyName("file_name_no_ext")]
        public string FileNameNoExt { get; set; }

        /// <summary>
        /// File extension for the save.
        /// </summary>
        [JsonPropertyName("file_extension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// Relative file path on the RomM server.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string FilePath { get; set; }

        /// <summary>
        /// Size of the save file in bytes.
        /// </summary>
        [JsonPropertyName("file_size_bytes")]
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Full file path on the RomM server.
        /// </summary>
        [JsonPropertyName("full_path")]
        public string FullPath { get; set; }

        /// <summary>
        /// Download path or URL for the save.
        /// </summary>
        [JsonPropertyName("download_path")]
        public string DownloadPath { get; set; }

        /// <summary>
        /// True when the file is missing from the server filesystem.
        /// </summary>
        [JsonPropertyName("missing_from_fs")]
        public bool MissingFromFs { get; set; }

        /// <summary>
        /// When the save was created (UTC).
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// When the save was last updated (UTC).
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// Emulator or core name associated with the save.
        /// </summary>
        [JsonPropertyName("emulator")]
        public string Emulator { get; set; }
    }
}
