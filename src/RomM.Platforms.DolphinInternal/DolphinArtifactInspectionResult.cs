using System.Collections.Generic;

namespace RomM.Platforms.DolphinInternal
{
    public sealed class DolphinArtifactInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public string CanonicalArtifactPath { get; set; } = string.Empty;
        public string CanonicalExtension { get; set; } = string.Empty;
        public List<DolphinArtifactCandidate> CandidateArtifacts { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

