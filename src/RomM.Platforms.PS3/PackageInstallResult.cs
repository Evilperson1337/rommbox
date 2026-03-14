using System;

namespace RomM.Platforms.PS3.Packages
{
    /// <summary>
    /// Captures the outcome of an RPCS3 package installation invocation.
    /// </summary>
    public sealed class PackageInstallResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }
}
