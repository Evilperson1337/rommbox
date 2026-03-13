using System.Collections.Generic;

namespace RomM.Platforms.Abstractions
{
    /// <summary>
    /// Optional identity metadata used for platform installer resolution.
    /// Implementers can declare RomM platform IDs and additional aliases.
    /// </summary>
    public interface IPlatformInstallerIdentityMetadata
    {
        IReadOnlyCollection<string>? SupportedPlatformIds { get; }

        IReadOnlyCollection<string>? SupportedPlatformAliases { get; }
    }
}

