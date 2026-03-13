using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Detection
{
    public sealed class DetectionResult
    {
        public bool IsInstalled { get; set; }
        public string? RecommendedExecutablePath { get; set; }
        public IReadOnlyList<string> CandidateExecutablePaths { get; set; } = new List<string>();
        public IReadOnlyList<DetectionWarning> Warnings { get; set; } = new List<DetectionWarning>();
        public IReadOnlyList<DetectionError> Errors { get; set; } = new List<DetectionError>();
    }
}

