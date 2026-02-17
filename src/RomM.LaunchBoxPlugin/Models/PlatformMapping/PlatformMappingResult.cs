using System.Collections.Generic;

namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// Wrapper for a platform mapping discovery result.
    /// </summary>
    internal sealed class PlatformMappingResult
    {
        /// <summary>
        /// Collection of platform mappings discovered or loaded from settings.
        /// </summary>
        public IList<PlatformMapping> Mappings { get; set; } = new List<PlatformMapping>();
    }
}
