namespace RomM.Platforms.GameCube.Inspection
{
    public sealed class GameCubeContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public GameCubeContentFormat Format { get; set; }
        public GameCubeContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public string GameId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}

