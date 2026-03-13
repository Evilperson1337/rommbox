using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class InstallResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ExecutablePath { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = new List<string>();
        public IReadOnlyList<AdditionalApplicationLaunchInfo> AdditionalApplications { get; set; } = new List<AdditionalApplicationLaunchInfo>();
        public string? PlatformContentId { get; set; }
        public InstallType? InstallType { get; set; }
        public string? InstallRootPath { get; set; }
    }
}

