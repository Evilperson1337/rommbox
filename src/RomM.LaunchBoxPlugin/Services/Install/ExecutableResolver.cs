using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RomMbox.Models.Install;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Resolves the most likely game executable in an install directory.
    /// </summary>
    internal sealed class ExecutableResolver
    {
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates a new executable resolver.
        /// </summary>
        /// <param name="logger">Logging service.</param>
        public ExecutableResolver(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Resolves an executable from the install root, optionally excluding subfolders.
        /// </summary>
        /// <param name="installRoot">The install root directory.</param>
        /// <param name="excludedRoots">Subfolder names to exclude from searches.</param>
        /// <returns>The executable resolution result.</returns>
        public ExecutableResolutionResult Resolve(string installRoot, IReadOnlyCollection<string> excludedRoots = null)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                return ExecutableResolutionResult.Failed("Install directory not found.");
            }

            var manifestPath = Path.Combine(installRoot, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var manifestResult = ResolveFromManifest(installRoot, manifestPath);
                if (manifestResult.Success)
                {
                    return manifestResult;
                }
            }

            var excluded = new HashSet<string>(excludedRoots ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var executables = Directory.EnumerateFiles(installRoot, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !IsInExcludedRoot(installRoot, path, excluded))
                .Where(path => !IsUninstaller(path))
                .OrderBy(path => path.Length)
                .ToList();
            if (executables.Count == 0)
            {
                return ExecutableResolutionResult.Failed("No executables found in install directory.");
            }

            if (executables.Count == 1)
            {
                _logger?.Info($"Executable resolver found single candidate '{executables[0]}'. Using without confirmation.");
                return ExecutableResolutionResult.CreateSuccess(executables[0], Array.Empty<string>());
            }

            var preferred = executables
                .FirstOrDefault(path => Path.GetFileName(path).IndexOf("setup", StringComparison.OrdinalIgnoreCase) < 0)
                ?? executables.First();
            _logger?.Info($"Executable resolver found {executables.Count} candidates. Heuristic selected '{preferred}'. Confirmation required. Candidates=[{string.Join(", ", executables)}]");
            return ExecutableResolutionResult.NeedsConfirmation(preferred, executables);
        }

        /// <summary>
        /// Attempts to resolve the executable from a manifest.json file.
        /// </summary>
        /// <param name="installRoot">The install root.</param>
        /// <param name="manifestPath">The manifest path.</param>
        /// <returns>The resolution result.</returns>
        private ExecutableResolutionResult ResolveFromManifest(string installRoot, string manifestPath)
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ManifestExecutable>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Executable))
                {
                    return ExecutableResolutionResult.Failed("Manifest missing executable entry.");
                }

                var resolvedExecutable = ResolveManifestValue(manifest.Executable, installRoot);
                var candidate = Path.GetFullPath(Path.Combine(installRoot, resolvedExecutable));
                if (!File.Exists(candidate))
                {
                    _logger?.Warning($"Manifest executable not found. ManifestPath='{manifestPath}', RawExecutable='{manifest.Executable}', ResolvedExecutable='{resolvedExecutable}', Candidate='{candidate}'.");
                    return ExecutableResolutionResult.Failed("Manifest executable not found on disk.");
                }

                var resolvedArgs = (manifest.Arguments ?? Array.Empty<string>())
                    .Select(arg => ResolveManifestValue(arg, installRoot))
                    .ToArray();

                _logger?.Info($"Executable resolver selected manifest executable '{candidate}'. ManifestPath='{manifestPath}'.");
                if (resolvedArgs.Length > 0)
                {
                    _logger?.Info($"Manifest arguments resolved: {string.Join(" ", resolvedArgs)}");
                }
                return ExecutableResolutionResult.CreateSuccess(candidate, resolvedArgs);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read manifest.json for executable resolution. {ex.Message}");
                return ExecutableResolutionResult.Failed("Manifest parsing failed.");
            }
        }

        private static string ResolveManifestValue(string value, string installRoot)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var resolved = value.Replace("%GAME_DIR%", installRoot ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
                .Replace("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
                .Replace("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
                .Replace("%DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StringComparison.OrdinalIgnoreCase);

            return resolved.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .Trim();
        }

        /// <summary>
        /// Determines whether a file path is inside an excluded root folder.
        /// </summary>
        /// <param name="root">The install root.</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="excludedRoots">Excluded root folder names.</param>
        /// <returns><c>true</c> when the file is in an excluded root.</returns>
        private static bool IsInExcludedRoot(string root, string filePath, IReadOnlyCollection<string> excludedRoots)
        {
            if (excludedRoots == null || excludedRoots.Count == 0)
            {
                return false;
            }

            var relative = Path.GetRelativePath(root, filePath);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length == 0)
            {
                return false;
            }

            return excludedRoots.Contains(parts[0]);
        }

        /// <summary>
        /// Determines whether the file looks like an uninstaller executable.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns><c>true</c> if the file name indicates an uninstaller.</returns>
        private static bool IsUninstaller(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return fileName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Represents the outcome of executable resolution.
    /// </summary>
    internal sealed class ExecutableResolutionResult
    {
        /// <summary>
        /// Gets whether resolution succeeded.
        /// </summary>
        public bool Success { get; private set; }
        /// <summary>
        /// Gets whether user confirmation is required.
        /// </summary>
        public bool RequiresConfirmation { get; private set; }
        /// <summary>
        /// Gets the resolved executable path.
        /// </summary>
        public string ExecutablePath { get; private set; }
        /// <summary>
        /// Gets the arguments to pass when launching the executable.
        /// </summary>
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();
        /// <summary>
        /// Gets the candidate executable paths used for confirmation.
        /// </summary>
        public IReadOnlyList<string> Candidates { get; private set; } = Array.Empty<string>();
        /// <summary>
        /// Gets the failure message when resolution fails.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Creates a successful resolution result.
        /// </summary>
        /// <param name="path">The resolved executable path.</param>
        /// <param name="args">Arguments to pass to the executable.</param>
        /// <returns>The resolution result.</returns>
        public static ExecutableResolutionResult CreateSuccess(string path, IReadOnlyList<string> args)
        {
            return new ExecutableResolutionResult
            {
                Success = true,
                ExecutablePath = path,
                Arguments = args ?? Array.Empty<string>()
            };
        }

        /// <summary>
        /// Creates a successful result that still requires user confirmation.
        /// </summary>
        /// <param name="path">The preferred executable path.</param>
        /// <param name="candidates">Candidate executable paths.</param>
        /// <returns>The resolution result.</returns>
        public static ExecutableResolutionResult NeedsConfirmation(string path, IReadOnlyList<string> candidates = null)
        {
            return new ExecutableResolutionResult
            {
                Success = true,
                RequiresConfirmation = true,
                ExecutablePath = path,
                Candidates = candidates ?? Array.Empty<string>()
            };
        }

        /// <summary>
        /// Creates a failed resolution result.
        /// </summary>
        /// <param name="message">The failure message.</param>
        /// <returns>The resolution result.</returns>
        public static ExecutableResolutionResult Failed(string message)
        {
            return new ExecutableResolutionResult
            {
                Success = false,
                Message = message
            };
        }
    }
}
