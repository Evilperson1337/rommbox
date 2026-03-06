namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class ExecutableCandidate
    {
        public string? FullPath { get; set; }
        public string? DisplayPath { get; set; }
        public string? FileName { get; set; }
        public long FileSizeBytes { get; set; }
        public string? FileSizeDisplay { get; set; }
        public string? Version { get; set; }
        public string? Architecture { get; set; }
        public string? LastModifiedDisplay { get; set; }
        public bool IsRecommended { get; set; }
    }
}

