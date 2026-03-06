using System;
using System.Collections.Generic;
using RomM.Platforms.Abstractions;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformInstallerRegistry
    {
        private readonly Dictionary<string, IPlatformInstaller> _installers;

        public PlatformInstallerRegistry(Dictionary<string, IPlatformInstaller> installers)
        {
            _installers = installers ?? new Dictionary<string, IPlatformInstaller>(StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGetInstaller(string platformKey, out IPlatformInstaller installer)
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
    }
}
