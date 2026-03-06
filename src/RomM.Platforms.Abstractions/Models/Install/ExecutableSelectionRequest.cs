using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class ExecutableSelectionRequest
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? InstallRoot { get; set; }
        public IReadOnlyList<ExecutableCandidate>? Candidates { get; set; }
        public ExecutableCandidate? Recommended { get; set; }
    }
}

