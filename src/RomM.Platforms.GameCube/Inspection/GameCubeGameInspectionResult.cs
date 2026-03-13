using System.Collections.Generic;

namespace RomM.Platforms.GameCube.Inspection
{
    public sealed class GameCubeGameInspectionResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public GameCubeContentFormat DetectedFormat { get; set; }
        public GameCubeContentFormat NormalizedFormat { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string StagedContentRoot { get; set; } = string.Empty;
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public bool RequiresExtraction { get; set; }
        public bool IsAmbiguous { get; set; }
        public string GameId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        public List<GameCubeContentCandidate> CandidateArtifacts { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

