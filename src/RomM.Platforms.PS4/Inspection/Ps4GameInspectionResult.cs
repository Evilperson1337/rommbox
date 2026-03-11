using System.Collections.Generic;

namespace RomM.Platforms.PS4.Inspection
{
    public sealed class Ps4GameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsAmbiguous { get; set; }
        public bool ArchiveExtractionEnabled { get; set; }
        public bool IsDirectPkgDownload { get; set; }
        public bool DirectPkgSupportEnabled { get; set; }

        public List<Ps4ContentCandidate> DetectedItems { get; } = new();
        public List<Ps4ContentCandidate> BaseGameItems { get; } = new();
        public List<Ps4ContentCandidate> UpdateItems { get; } = new();
        public List<Ps4ContentCandidate> DlcItems { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

