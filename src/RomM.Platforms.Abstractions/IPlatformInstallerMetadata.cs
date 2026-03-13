using RomM.Platforms.Abstractions.Models.Metadata;

namespace RomM.Platforms.Abstractions
{
    public interface IPlatformInstallerMetadata
    {
        PlatformInstallerCapabilities Capabilities { get; }

        PlatformConfigDescriptor? GetConfigDescriptor();
    }
}

