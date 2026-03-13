using System.Collections.Generic;

namespace RomM.Platforms.WiiU.Inspection
{
    public sealed class WiiUGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public WiiUContentFormat DetectedFormat { get; set; }
        public WiiUContentFormat NormalizedFormat { get; set; }
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public string InstallSourcePath { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public WiiUContentRole ContentRole { get; set; }
        public WiiUContentFormat SelectedFormat { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        public List<WiiUContentCandidate> BaseGameCandidates { get; } = new();
        public List<WiiUContentCandidate> UpdateCandidates { get; } = new();
        public List<WiiUContentCandidate> DlcCandidates { get; } = new();
        public List<WiiUContentCandidate> AllCandidates { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

