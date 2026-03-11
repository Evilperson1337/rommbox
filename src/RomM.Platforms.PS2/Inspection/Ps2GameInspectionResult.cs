using System.Collections.Generic;

namespace RomM.Platforms.PS2.Inspection
{
    public sealed class Ps2GameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Ps2ContentFormat DetectedFormat { get; set; }
        public Ps2ContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public string DiscId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        public List<Ps2ContentCandidate> CandidateArtifacts { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

