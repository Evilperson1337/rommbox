using System.Collections.Generic;

namespace RomM.Platforms.PS3.Inspection
{
    public sealed class Ps3GameInspectorResult
    {
        public Ps3GameFormat Format { get; set; } = Ps3GameFormat.Unknown;
        public string GameRootPath { get; set; } = string.Empty;
        public string Ps3GamePath { get; set; } = string.Empty;
        public string IsoPath { get; set; } = string.Empty;
        public string EbootPath { get; set; } = string.Empty;
        public string ParamSfoPath { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public Ps3GameRegion Region { get; set; } = Ps3GameRegion.Unknown;
        public List<Ps3PackageFile> DlcPackages { get; set; } = new();
        public List<Ps3PackageFile> UpdatePackages { get; set; } = new();
        public List<Ps3RapFile> RapFiles { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
