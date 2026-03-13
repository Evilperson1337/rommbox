using System.Collections.Generic;
using RomM.Platforms.Abstractions.Models.Metadata;

namespace RomM.Platforms.Abstractions
{
    public interface IPlatformConfigurationProvider
    {
        string PluginKey { get; }

        PlatformConfigDescriptor GetConfigurationSchema();

        IReadOnlyDictionary<string, string> GetDefaultValues();

        IReadOnlyDictionary<string, string> GetUiMetadata();

        IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string> settings);
    }
}

