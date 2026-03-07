using System;
using System.Collections.Generic;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Metadata;

#nullable enable

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformInstallerRegistry
    {
        private readonly Dictionary<string, IPlatformInstaller> _installers;

        public PlatformInstallerRegistry(Dictionary<string, IPlatformInstaller> installers)
        {
            _installers = installers ?? new Dictionary<string, IPlatformInstaller>(StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGetInstaller(string platformKey, out IPlatformInstaller? installer)
        {
            installer = null;
            if (string.IsNullOrWhiteSpace(platformKey))
            {
                return false;
            }

            return _installers.TryGetValue(platformKey, out installer);
        }

        public IReadOnlyDictionary<string, IPlatformInstaller> GetAll()
        {
            return _installers;
        }

        public PlatformInstallerCapabilities GetCapabilities(string platformKey)
        {
            if (TryGetInstaller(platformKey, out var installer)
                && installer is IPlatformInstallerMetadata metadata
                && metadata.Capabilities != null)
            {
                return metadata.Capabilities;
            }

            return new PlatformInstallerCapabilities();
        }

        public PlatformConfigDescriptor? GetConfigDescriptor(string platformKey)
        {
            if (TryGetInstaller(platformKey, out var installer)
                && installer is IPlatformInstallerMetadata metadata)
            {
                return metadata.GetConfigDescriptor();
            }

            return null;
        }
    }
}
