namespace RomMbox.Models.PlatformFolders
{
    /// <summary>
    /// Represents a resolved platform folder entry.
    /// </summary>
    internal sealed class PlatformFolderEntry
    {
        public PlatformFolderEntry(PlatformFolderType type, string name, string path, bool isResolved)
        {
            Type = type;
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            IsResolved = isResolved;
        }

        public PlatformFolderType Type { get; }

        public string Name { get; }

        public string Path { get; }

        public bool IsResolved { get; }
    }
}
