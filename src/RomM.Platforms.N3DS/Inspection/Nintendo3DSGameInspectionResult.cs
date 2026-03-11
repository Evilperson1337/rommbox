using System.Collections.Generic;

namespace RomM.Platforms.N3DS.Inspection
{
    public sealed class Nintendo3DSGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Nintendo3DSContentFormat DetectedFormat { get; set; }
        public Nintendo3DSContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool RequiresEmulatorImport { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsAmbiguous { get; set; }

        public List<Nintendo3DSContentCandidate> CandidateArtifacts { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

