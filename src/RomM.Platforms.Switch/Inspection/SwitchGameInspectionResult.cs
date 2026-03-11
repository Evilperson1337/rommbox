using System.Collections.Generic;

namespace RomM.Platforms.Switch.Inspection
{
    public sealed class SwitchGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public SwitchContentFormat DetectedFormat { get; set; }
        public SwitchContentFormat NormalizedFormat { get; set; }
        public bool RequiresExtraction { get; set; }
        public bool RequiresDecompression { get; set; }
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public SwitchPackageType ContentType { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsAmbiguous { get; set; }

        public List<SwitchContentCandidate> BaseGameCandidates { get; } = new();
        public List<SwitchContentCandidate> UpdateCandidates { get; } = new();
        public List<SwitchContentCandidate> DlcCandidates { get; } = new();
        public List<SwitchContentCandidate> AllCandidates { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

