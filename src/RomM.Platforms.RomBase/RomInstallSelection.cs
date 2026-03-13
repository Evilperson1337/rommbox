using System.Collections.Generic;

namespace RomM.Platforms.RomBase
{
    internal sealed class RomInstallSelection
    {
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string? SourceRoot { get; set; }
        public string? Message { get; set; }
        public bool ShouldPreserveArchive { get; set; }
        public IReadOnlyList<string> Candidates { get; set; } = new List<string>();
    }
}
