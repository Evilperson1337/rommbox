namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class AdditionalApplicationLaunchInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ApplicationPath { get; set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; set; } = System.Array.Empty<string>();
    }
}
