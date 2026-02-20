namespace RomMbox.UI.Models
{
    /// <summary>
    /// Represents a selectable emulator entry.
    /// </summary>
    public sealed class EmulatorOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
