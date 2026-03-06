using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Uninstall
{
    public sealed class UninstallResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int RemovedCount { get; set; }
        public IReadOnlyList<string> Notes { get; set; } = new List<string>();
    }
}

