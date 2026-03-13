using System.Collections.Generic;

namespace RomM.Platforms.Arcade.Inspection
{
    public sealed class ArcadeGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string DetectedArchive { get; set; } = string.Empty;
        public string RomSetName { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public List<string> CandidateRomSets { get; set; } = new List<string>();
        public List<string> SupportedEmulators { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
