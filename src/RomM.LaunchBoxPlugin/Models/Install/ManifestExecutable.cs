using System.Text.Json.Serialization;

namespace RomMbox.Models.Install
{
    /// <summary>
    /// Executable entry described in a Windows install manifest.
    /// </summary>
    internal sealed class ManifestExecutable
    {
        /// <summary>
        /// Executable path or name.
        /// </summary>
        [JsonPropertyName("Executable")]
        public string Executable { get; set; } = string.Empty;

        /// <summary>
        /// Optional arguments used to launch the executable.
        /// </summary>
        [JsonPropertyName("Arguments")]
        public string[] Arguments { get; set; } = System.Array.Empty<string>();
    }
}
