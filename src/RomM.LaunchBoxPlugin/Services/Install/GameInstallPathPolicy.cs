using System;

namespace RomMbox.Services.Install
{
    internal static class GameInstallPathPolicy
    {
        public static bool ShouldUseGameSubfolder(string platformName, string rommPlatformId)
        {
            if (InstallDestinationService.IsWindowsPlatform(platformName ?? string.Empty))
            {
                return false;
            }

            return true;
        }
    }
}
