using System;
using System.Drawing;
using System.IO;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;

namespace RomMbox.Services
{
    internal sealed class BadgeAssetService
    {
        private const string BadgeFileName = "RomM.png";
        private readonly LoggingService _logger;

        public BadgeAssetService(LoggingService logger)
        {
            _logger = logger;
        }

        public string EnsureBadgeImagePath()
        {
            var sourcePath = ResolvePluginBadgeSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                _logger?.Warning($"[RomM Badge] Badge source image missing. SourcePath='{sourcePath ?? string.Empty}'.");
                return string.Empty;
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                _logger?.Warning("[RomM Badge] LaunchBox root unavailable while resolving badge image.");
                return sourcePath;
            }

            var badgesDirectory = Path.Combine(launchBoxRoot, "Images", "Media Packs", "Badges", "Nostalgic Platform Badges");
            var targetPath = Path.Combine(badgesDirectory, BadgeFileName);

            try
            {
                Directory.CreateDirectory(badgesDirectory);
                var shouldCopy = !File.Exists(targetPath)
                    || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(targetPath)
                    || new FileInfo(sourcePath).Length != new FileInfo(targetPath).Length;

                if (shouldCopy)
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    _logger?.Info($"[RomM Badge] Badge image deployed. SourcePath='{sourcePath}', TargetPath='{targetPath}'.");
                }
                else
                {
                    _logger?.Debug($"[RomM Badge] Badge image already up to date. TargetPath='{targetPath}'.");
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                _logger?.Error("[RomM Badge] Failed to deploy badge image.", ex);
                return sourcePath;
            }
        }

        public Image LoadBadgeImage()
        {
            var resolvedPath = EnsureBadgeImagePath();
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                _logger?.Warning($"[RomM Badge] Resolved badge image path is unavailable. Path='{resolvedPath ?? string.Empty}'.");
                return null;
            }

            try
            {
                using var stream = File.OpenRead(resolvedPath);
                using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                _logger?.Debug($"[RomM Badge] Badge image loaded. Path='{resolvedPath}', Width={source.Width}, Height={source.Height}.");
                return new Bitmap(source);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[RomM Badge] Failed to load badge image from '{resolvedPath}'.", ex);
                return null;
            }
        }

        private string ResolvePluginBadgeSourcePath()
        {
            var pluginRoot = PluginPaths.GetPluginRootDirectory();
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                Path.Combine(pluginRoot, "assets", "romm.png"),
                Path.Combine(pluginRoot, "system", "assets", "romm.png")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    _logger?.Debug($"[RomM Badge] Badge source image resolved. Path='{candidate}'.");
                    return candidate;
                }
            }

            return candidates[0];
        }
    }
}
