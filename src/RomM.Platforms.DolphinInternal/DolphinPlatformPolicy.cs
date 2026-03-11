using System.Collections.Generic;

namespace RomM.Platforms.DolphinInternal
{
    public sealed class DolphinPlatformPolicy
    {
        public IReadOnlyCollection<string> SupportedExtensions { get; set; } = new List<string>();
        public IReadOnlyCollection<string> ArchiveExtensions { get; set; } = new List<string> { ".zip", ".7z", ".rar" };
        public IReadOnlyCollection<string> PreferredExtensions { get; set; } = new List<string>();
        public bool AllowRecursiveSearch { get; set; } = true;
        public bool RequireSingleCanonicalArtifact { get; set; } = false;
    }
}

