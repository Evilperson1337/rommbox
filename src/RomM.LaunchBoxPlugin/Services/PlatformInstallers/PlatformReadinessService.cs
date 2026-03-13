using System;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomMbox.Models.PlatformMapping;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformReadinessService
    {
        private readonly PlatformInstallerRegistry _registry;

        public PlatformReadinessService(PlatformInstallerRegistry registry)
        {
            _registry = registry;
        }

        public PlatformReadinessResult Evaluate(string platformKey, PlatformMapping mapping)
        {
            var capabilities = _registry?.GetCapabilities(platformKey) ?? new PlatformInstallerCapabilities();

            var requiresEmulatorPath = capabilities.RequiresEmulatorPath;
            var hasEmulator = !string.IsNullOrWhiteSpace(mapping?.AssociatedEmulatorId)
                || !string.IsNullOrWhiteSpace(mapping?.Rpcs3ExecutablePath);

            if (requiresEmulatorPath && !hasEmulator)
            {
                return new PlatformReadinessResult
                {
                    IsReady = false,
                    Status = "Needs Configuration",
                    Message = "Platform requires emulator configuration before install/import."
                };
            }

            return new PlatformReadinessResult
            {
                IsReady = true,
                Status = "Ready",
                Message = "Platform is ready for import/install."
            };
        }
    }

    internal sealed class PlatformReadinessResult
    {
        public bool IsReady { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

