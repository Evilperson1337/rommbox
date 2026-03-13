namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class ExecutableSelectionResult
    {
        public bool Confirmed { get; set; }
        public ExecutableCandidate? Selected { get; set; }
    }
}

