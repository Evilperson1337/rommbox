using System.Collections.Generic;
using RomM.Platforms.Abstractions.Models.Install;

namespace RomM.Platforms.Windows.Install
{
    internal sealed class WindowsInstallResult
    {
        public bool Success { get; private set; }
        public string? ExecutablePath { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = new List<string>();
        public string? Message { get; private set; }
        public InstallType? InstallType { get; private set; }

        public static WindowsInstallResult CreateSuccess(string executablePath, IReadOnlyList<string>? args, InstallType? installType = null)
        {
            return new WindowsInstallResult
            {
                Success = true,
                ExecutablePath = executablePath,
                Arguments = args ?? new List<string>(),
                InstallType = installType
            };
        }

        public static WindowsInstallResult Failed(string message)
        {
            return new WindowsInstallResult
            {
                Success = false,
                Message = message
            };
        }
    }
}
