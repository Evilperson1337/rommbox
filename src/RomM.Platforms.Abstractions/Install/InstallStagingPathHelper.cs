using System;
using System.IO;
using System.Linq;

namespace RomM.Platforms.Abstractions.Install
{
    public static class InstallStagingPathHelper
    {
        public static string ResolvePlatformStagingRoot(string platformInstallRoot)
        {
            var root = GameInstallPathHelper.ResolvePlatformRoot(platformInstallRoot);
            return string.IsNullOrWhiteSpace(root)
                ? string.Empty
                : Path.Combine(root, ".staging");
        }

        public static string ResolveOperationRoot(string platformInstallRoot, string? operationId)
        {
            var stagingRoot = ResolvePlatformStagingRoot(platformInstallRoot);
            if (string.IsNullOrWhiteSpace(stagingRoot))
            {
                return string.Empty;
            }

            var safeOperationId = string.IsNullOrWhiteSpace(operationId)
                ? Guid.NewGuid().ToString("N")
                : operationId.Trim();
            return Path.Combine(stagingRoot, safeOperationId);
        }

        public static string ResolveOperationPath(string platformInstallRoot, string? operationId, params string[] segments)
        {
            var operationRoot = ResolveOperationRoot(platformInstallRoot, operationId);
            if (string.IsNullOrWhiteSpace(operationRoot))
            {
                return string.Empty;
            }

            var path = operationRoot;
            foreach (var segment in segments ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    path = Path.Combine(path, segment);
                }
            }

            return path;
        }

        public static string? TryResolveOperationRootFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var currentPath = Directory.Exists(path)
                    ? Path.GetFullPath(path)
                    : Path.GetDirectoryName(Path.GetFullPath(path));

                while (!string.IsNullOrWhiteSpace(currentPath))
                {
                    var parent = Path.GetDirectoryName(currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(parent))
                    {
                        break;
                    }

                    if (string.Equals(Path.GetFileName(parent), ".staging", StringComparison.OrdinalIgnoreCase))
                    {
                        return currentPath;
                    }

                    currentPath = parent;
                }
            }
            catch
            {
            }

            return null;
        }

        public static void TryDeleteOperationRootAndEmptyParents(string? path)
        {
            var operationRoot = TryResolveOperationRootFromPath(path);
            if (string.IsNullOrWhiteSpace(operationRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(operationRoot))
                {
                    Directory.Delete(operationRoot, recursive: true);
                }
            }
            catch
            {
                return;
            }

            try
            {
                var currentPath = Path.GetDirectoryName(operationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                while (!string.IsNullOrWhiteSpace(currentPath)
                    && Directory.Exists(currentPath)
                    && !Directory.EnumerateFileSystemEntries(currentPath).Any())
                {
                    var currentName = Path.GetFileName(currentPath);
                    var parent = Path.GetDirectoryName(currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    Directory.Delete(currentPath, recursive: false);

                    if (!string.Equals(currentName, ".staging", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentPath = parent;
                }
            }
            catch
            {
            }
        }
    }
}
