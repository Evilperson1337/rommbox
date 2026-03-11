using System.Collections.Generic;

namespace RomM.Platforms.Xbox.Inspection
{
    public sealed class XboxGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public XboxContentFormat DetectedFormat { get; set; }
        public XboxContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public bool ContainsUnsupportedExtractedLayout { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;

        public List<XboxContentCandidate> CandidateArtifacts { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

