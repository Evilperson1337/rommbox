using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Metadata
{
    public sealed class PlatformConfigDescriptor
    {
        public IReadOnlyList<PlatformConfigFieldDescriptor> Fields { get; set; } = new List<PlatformConfigFieldDescriptor>();
    }
}

