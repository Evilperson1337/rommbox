using System;
using System.Collections.Generic;
using System.Linq;
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

        public bool TryGetInstallerOrFallback(string platformKey, out IPlatformInstaller? installer, out bool usedFallback)
        {
            usedFallback = false;
            if (TryGetInstaller(platformKey, out installer))
            {
                return true;
            }

            if (TryGetInstaller("general", out installer))
            {
                usedFallback = true;
                return true;
            }

            return false;
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

        public string ResolveConfigPluginKey(string preferredKey)
        {
            if (TryGetInstaller(preferredKey, out var installer) && installer != null)
            {
                return installer.PlatformKey ?? preferredKey ?? string.Empty;
            }

            var resolvedByIdentity = PlatformIdentityResolver.Resolve(
                this,
                new PlatformResolutionEvidence
                {
                    PlatformKey = preferredKey ?? string.Empty,
                    PlatformDisplayName = preferredKey ?? string.Empty,
                    LaunchBoxPlatformName = preferredKey ?? string.Empty
                }).ResolvedPlatformKey;
            if (!string.IsNullOrWhiteSpace(resolvedByIdentity))
            {
                return resolvedByIdentity;
            }

            if (TryGetInstaller("general", out var fallback) && fallback != null)
            {
                return fallback.PlatformKey ?? "general";
            }

            return preferredKey ?? string.Empty;
        }
    }
}
