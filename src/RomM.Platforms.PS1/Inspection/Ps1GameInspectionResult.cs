using System.Collections.Generic;

namespace RomM.Platforms.PS1.Inspection
{
    public sealed class Ps1GameInspectionResult
    {
        public Ps1ContentFormat Format { get; set; }
        public string SourceRootPath { get; set; } = string.Empty;
        public string LaunchFilePath { get; set; } = string.Empty;
        public string InstallFolderName { get; set; } = string.Empty;
        public bool IsMultiDisc { get; set; }
        public bool RequiresExtraction { get; set; }
        public List<string> DiscFiles { get; set; } = new List<string>();
        public List<string> OwnedFiles { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
