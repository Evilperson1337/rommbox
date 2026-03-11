using System.Collections.Generic;

namespace RomM.Platforms.Vita.Inspection
{
    public sealed class VitaGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public VitaContentFormat DetectedFormat { get; set; }
        public VitaContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string InstallArtifactPath { get; set; } = string.Empty;
        public VitaContentRole ContentRole { get; set; }
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;

        public List<VitaContentCandidate> CandidateArtifacts { get; } = new();
        public List<VitaContentCandidate> OrderedInstallCandidates { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

