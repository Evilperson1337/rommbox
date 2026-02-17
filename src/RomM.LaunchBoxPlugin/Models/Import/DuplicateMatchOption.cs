using System.ComponentModel;

namespace RomMbox.Models.Import
{
    /// <summary>
    /// Duplicate matching strategies offered in the import UI.
    /// </summary>
    public enum DuplicateMatchOption
    {
        [Description("Game Name")]
        GameName,
        [Description("File Name")]
        FileName,
        [Description("MD5")]
        Md5
    }
}
