using System;
using System.IO;
using System.Linq;
using System.Text;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Windows.Install
{
    internal sealed class WindowsInstallClassifier
    {
        private readonly IPlatformLogger? _logger;

        public WindowsInstallClassifier(IPlatformLogger? logger)
        {
            _logger = logger;
        }

        public InstallType DetectInstallType(string? archivePath, string? extractedPath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                if (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
                {
                    var setupExe = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(setupExe))
                    {
                        _logger?.Write(PlatformLogLevel.Debug, $"Install type detection found setup.exe at '{setupExe}' with no archive path.");
                        return InstallType.Installer;
                    }

                    _logger?.Write(PlatformLogLevel.Debug, "Install type detection defaulted to portable (archive path missing)." );
                    return InstallType.Portable;
                }

                _logger?.Write(PlatformLogLevel.Debug, "Install type detection skipped: archive path missing.");
                return InstallType.Unknown;
            }

            var fileName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
            if (fileName.IndexOf("(installer)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Write(PlatformLogLevel.Debug, $"Install type detection matched installer marker for '{fileName}'.");
                return InstallType.Installer;
            }

            if (fileName.IndexOf("(portable)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Write(PlatformLogLevel.Debug, $"Install type detection matched portable marker for '{fileName}'.");
                return InstallType.Portable;
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
            {
                var setupExe = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(setupExe))
                {
                    _logger?.Write(PlatformLogLevel.Debug, $"Install type detection found setup.exe at '{setupExe}'.");
                    return InstallType.Installer;
                }

                _logger?.Write(PlatformLogLevel.Debug, $"Install type detection defaulted to portable for '{archivePath}'.");
                return InstallType.Portable;
            }

            _logger?.Write(PlatformLogLevel.Debug, $"Install type detection returned unknown for '{archivePath}'.");
            return InstallType.Unknown;
        }

        public bool IsInnoInstaller(string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return false;
            }

            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return false;
            }

            return IsInnoInstallerExe(setupPath);
        }

        public bool IsInnoInstallerExe(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(exePath);
                var marker = Encoding.ASCII.GetBytes("Inno Setup");
                for (var index = 0; index <= bytes.Length - marker.Length; index++)
                {
                    var matched = true;
                    for (var offset = 0; offset < marker.Length; offset++)
                    {
                        if (bytes[index + offset] != marker[offset])
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (matched)
                    {
                        _logger?.Write(PlatformLogLevel.Debug, $"Detected Inno Setup installer signature in '{exePath}'.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, $"Failed to inspect '{exePath}' for Inno signature.", ex);
            }

            return false;
        }
    }
}
