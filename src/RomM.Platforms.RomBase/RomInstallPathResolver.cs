using System;
using System.IO;
using RomM.Platforms.Abstractions.Install;
using RomM.Platforms.Abstractions.Models.Rom;

namespace RomM.Platforms.RomBase
{
    internal sealed class RomInstallPathResolver
    {
        private readonly RomInstallProfile _profile;

        public RomInstallPathResolver(RomInstallProfile profile)
        {
            _profile = profile;
        }

        public string ResolveInstallRoot(string baseInstallDirectory, RomInstallSettings? settings)
        {
            var root = settings?.RomRootPath;
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            if (!string.IsNullOrWhiteSpace(baseInstallDirectory))
            {
                return baseInstallDirectory;
            }

            return string.Empty;
        }

        public string ResolveInstallDirectory(string baseInstallDirectory, RomInstallSettings? settings)
        {
            var root = ResolveInstallRoot(baseInstallDirectory, settings);
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            if (!_profile.UsePlatformSubdirectory)
            {
                return root;
            }

            var folder = string.IsNullOrWhiteSpace(_profile.PlatformFolderName)
                ? _profile.DisplayName
                : _profile.PlatformFolderName;
            if (string.IsNullOrWhiteSpace(folder))
            {
                return root;
            }

            return Path.Combine(root, folder);
        }

        public string ResolveTargetFileName(string? gameName, string? sourcePath)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var fileName = Path.GetFileName(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }

            var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);
            return safeName + ".rom";
        }

        public string ResolveGameInstallDirectory(string installDirectory, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return string.Empty;
            }

            return GameInstallPathHelper.ResolveGameDirectory(installDirectory, gameName, _profile.DisplayName, _profile.PlatformFolderName);
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var index = 0; index < chars.Length; index++)
            {
                if (Array.IndexOf(invalid, chars[index]) >= 0)
                {
                    chars[index] = '_';
                }
            }

            var cleaned = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Game" : cleaned;
        }
    }
}
