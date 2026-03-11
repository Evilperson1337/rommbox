using System.Collections.Generic;

namespace RomM.Platforms.Xbox360.Inspection
{
    public sealed class Xbox360GameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Xbox360ContentFormat DetectedFormat { get; set; }
        public Xbox360ContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public bool ContainsExtractedLayout { get; set; }
        public bool ContainsGodLayout { get; set; }
        public bool GodLayoutSupported { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string MediaId { get; set; } = string.Empty;
        public bool IsMultiDisc { get; set; }

        public List<Xbox360ContentCandidate> CandidateArtifacts { get; } = new();
        public List<Xbox360ContentCandidate> AdditionalLaunchArtifacts { get; } = new();
        public List<Xbox360ContentCandidate> PackageArchives { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

