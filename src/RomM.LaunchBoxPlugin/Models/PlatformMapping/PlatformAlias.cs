using System;
using System.Runtime.Serialization;

namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// User-defined alias to help map RomM platform names to LaunchBox platforms.
    /// </summary>
    [DataContract]
    internal sealed class PlatformAlias
    {
        /// <summary>
        /// Unique id for the alias entry.
        /// </summary>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Alias value entered by the user.
        /// </summary>
        [DataMember(Name = "alias", EmitDefaultValue = false)]
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// LaunchBox platform name that the alias should resolve to.
        /// </summary>
        [DataMember(Name = "launchBoxPlatformName", EmitDefaultValue = false)]
        public string LaunchBoxPlatformName { get; set; } = string.Empty;
    }
}
