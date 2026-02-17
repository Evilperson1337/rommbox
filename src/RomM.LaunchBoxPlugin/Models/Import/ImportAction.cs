using System.ComponentModel;

namespace RomMbox.Models.Import
{
    /// <summary>
    /// Actions available for each ROM row in the import UI.
    /// </summary>
    public enum ImportAction
    {
        [Description("Import")]
        Import,
        [Description("Install")]
        Install,
        [Description("Merge")]
        Merge,
        [Description("Skip")]
        Skip
    }
}
