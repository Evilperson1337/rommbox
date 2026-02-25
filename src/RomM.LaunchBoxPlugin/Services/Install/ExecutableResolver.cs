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
        /// <param name="gameName">Optional game name for filename similarity matching.</param>
        /// <param name="excludedRoots">Subfolder names to exclude from searches.</param>
        /// <returns>The executable resolution result.</returns>
        public ExecutableResolutionResult Resolve(string installRoot, string gameName, IReadOnlyCollection<string> excludedRoots = null)
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
            var normalizedGameName = NormalizeName(gameName);
            var shortcutTarget = ResolveShortcutTarget(installRoot, normalizedGameName, excluded);
            var executables = Directory.EnumerateFiles(installRoot, "*.exe", SearchOption.AllDirectories)
                .Where(path => !IsInExcludedRoot(installRoot, path, excluded))
                .Where(path => !IsUninstaller(path))
                .Where(path => !IsInRedistFolder(installRoot, path))
                .Where(path => !IsUnityCrashExecutable(path))
                .ToList();
            if (!string.IsNullOrWhiteSpace(shortcutTarget)
                && !executables.Contains(shortcutTarget, StringComparer.OrdinalIgnoreCase))
            {
                executables.Add(shortcutTarget);
            }
            if (executables.Count == 0)
            {
                return ExecutableResolutionResult.Failed("No executables found in install directory.");
            }

            if (executables.Count == 1)
            {
                _logger?.Info($"Executable resolver found single candidate '{executables[0]}'. Using without confirmation.");
                return ExecutableResolutionResult.CreateSuccess(executables[0], Array.Empty<string>());
            }

            var ordered = OrderExecutableCandidates(installRoot, normalizedGameName, executables, shortcutTarget);
            var preferred = ordered.First();
            var preferredScore = ScoreExecutable(installRoot, normalizedGameName, preferred, shortcutTarget);
            _logger?.Info($"Executable resolver found {executables.Count} candidates. Heuristic selected '{preferred}' (RootFirst={(preferredScore?.IsRoot == true)}, NameDistance={preferredScore?.NameDistance}, Arch={preferredScore?.Architecture}, ShortcutMatch={(preferredScore?.ShortcutMatch == true)}). Confirmation required. Candidates=[{string.Join(", ", ordered)}]");
            return ExecutableResolutionResult.NeedsConfirmation(preferred, ordered);
        }

        /// <summary>
        /// Resolves an executable from the install root, optionally excluding subfolders.
        /// </summary>
        /// <param name="installRoot">The install root directory.</param>
        /// <param name="excludedRoots">Subfolder names to exclude from searches.</param>
        /// <returns>The executable resolution result.</returns>
        public ExecutableResolutionResult Resolve(string installRoot, IReadOnlyCollection<string> excludedRoots = null)
        {
            return Resolve(installRoot, null, excludedRoots);
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

        private static bool IsUnityCrashExecutable(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return fileName.IndexOf("UnityCrash", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInRedistFolder(string root, string filePath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var relative = Path.GetRelativePath(root, filePath);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => string.Equals(part.Trim('_'), "redist", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> OrderExecutableCandidates(string installRoot, string normalizedGameName, IReadOnlyList<string> executables, string shortcutTarget)
        {
            var scores = executables
                .Select(path => ScoreExecutable(installRoot, normalizedGameName, path, shortcutTarget))
                .Where(score => score != null)
                .ToList();

            if (string.IsNullOrWhiteSpace(normalizedGameName))
            {
                return scores
                    .OrderBy(score => score.RootPenalty)
                    .ThenBy(score => score.ShortcutPenalty)
                    .ThenBy(score => score.ArchitecturePenalty)
                    .ThenBy(score => score.SetupPenalty)
                    .ThenBy(score => score.PathLength)
                    .Select(score => score.Path)
                    .ToList();
            }

            return scores
                .OrderBy(score => score.RootPenalty)
                .ThenBy(score => score.ShortcutPenalty)
                .ThenBy(score => score.ArchitecturePenalty)
                .ThenBy(score => score.NameDistance)
                .ThenBy(score => score.SetupPenalty)
                .ThenBy(score => score.PathLength)
                .Select(score => score.Path)
                .ToList();
        }

        private static ExecutableScore ScoreExecutable(string installRoot, string normalizedGameName, string path, string shortcutTarget)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var relative = Path.GetRelativePath(installRoot, path);
            var depth = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
            var isRoot = depth <= 1;
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var normalizedFile = NormalizeName(fileName);
            var nameDistance = string.IsNullOrWhiteSpace(normalizedGameName)
                ? int.MaxValue
                : ComputeLevenshteinDistance(normalizedGameName, normalizedFile);
            var setupPenalty = fileName.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0;
            var architecture = ExecutableArchitectureDetector.GetArchitecture(path);
            var architecturePenalty = ExecutableArchitectureDetector.GetPreferencePenalty(architecture);
            var shortcutMatch = !string.IsNullOrWhiteSpace(shortcutTarget)
                && string.Equals(path, shortcutTarget, StringComparison.OrdinalIgnoreCase);

            return new ExecutableScore
            {
                Path = path,
                IsRoot = isRoot,
                RootPenalty = isRoot ? 0 : 1,
                NameDistance = nameDistance,
                SetupPenalty = setupPenalty,
                PathLength = path.Length,
                Architecture = architecture,
                ArchitecturePenalty = architecturePenalty,
                ShortcutMatch = shortcutMatch,
                ShortcutPenalty = shortcutMatch ? 0 : 1
            };
        }

        private string ResolveShortcutTarget(string installRoot, string normalizedGameName, IReadOnlyCollection<string> excludedRoots)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(normalizedGameName))
            {
                return null;
            }

            var shortcuts = Directory.EnumerateFiles(installRoot, "*.lnk", SearchOption.AllDirectories)
                .Where(path => !IsInExcludedRoot(installRoot, path, excludedRoots))
                .Where(path => !IsInRedistFolder(installRoot, path))
                .ToList();
            if (shortcuts.Count == 0)
            {
                return null;
            }

            string bestTarget = null;
            int bestDistance = int.MaxValue;
            int bestPathLength = int.MaxValue;

            foreach (var shortcut in shortcuts)
            {
                var shortcutName = NormalizeName(Path.GetFileNameWithoutExtension(shortcut));
                var distance = string.IsNullOrWhiteSpace(shortcutName)
                    ? int.MaxValue
                    : ComputeLevenshteinDistance(normalizedGameName, shortcutName);

                var target = TryGetShortcutTarget(shortcut);
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                target = ExpandEnvironmentPath(target, installRoot);
                if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Path.IsPathRooted(target))
                {
                    target = Path.GetFullPath(Path.Combine(installRoot, target));
                }

                if (!File.Exists(target))
                {
                    continue;
                }

                if (!IsUnderRoot(installRoot, target))
                {
                    continue;
                }

                if (IsInRedistFolder(installRoot, target) || IsUnityCrashExecutable(target))
                {
                    continue;
                }

                var pathLength = target.Length;
                if (distance < bestDistance || (distance == bestDistance && pathLength < bestPathLength))
                {
                    bestDistance = distance;
                    bestPathLength = pathLength;
                    bestTarget = target;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestTarget))
            {
                _logger?.Info($"Executable resolver shortcut hint selected '{bestTarget}' from .lnk targets.");
            }

            return bestTarget;
        }

        private static string TryGetShortcutTarget(string shortcutPath)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return null;
                }

                var shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return null;
                }

                try
                {
                    dynamic shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    return shortcut?.TargetPath as string;
                }
                finally
                {
                    if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    {
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ExpandEnvironmentPath(string path, string installRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var expanded = Environment.ExpandEnvironmentVariables(path);
            return expanded.Replace("%GAME_DIR%", installRoot ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderRoot(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var buffer = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    buffer.Append(char.ToLowerInvariant(ch));
                }
            }

            return buffer.ToString();
        }

        private static int ComputeLevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return string.IsNullOrEmpty(b) ? 0 : b.Length;
            }

            if (string.IsNullOrEmpty(b))
            {
                return a.Length;
            }

            var costs = new int[b.Length + 1];
            for (var j = 0; j < costs.Length; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                costs[0] = i;
                var prev = i - 1;
                for (var j = 1; j <= b.Length; j++)
                {
                    var current = costs[j];
                    var add = costs[j] + 1;
                    var delete = costs[j - 1] + 1;
                    var replace = prev + (a[i - 1] == b[j - 1] ? 0 : 1);
                    costs[j] = Math.Min(add, Math.Min(delete, replace));
                    prev = current;
                }
            }

            return costs[b.Length];
        }

        private sealed class ExecutableScore
        {
            public string Path { get; set; }
            public bool IsRoot { get; set; }
            public int RootPenalty { get; set; }
            public int NameDistance { get; set; }
            public int SetupPenalty { get; set; }
            public int PathLength { get; set; }
            public ExecutableArchitecture Architecture { get; set; }
            public int ArchitecturePenalty { get; set; }
            public bool ShortcutMatch { get; set; }
            public int ShortcutPenalty { get; set; }
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
        /// Returns a new result with the executable updated after user selection.
        /// </summary>
        /// <param name="path">The selected executable path.</param>
        /// <returns>The updated resolution result.</returns>
        public ExecutableResolutionResult WithExecutable(string path)
        {
            return new ExecutableResolutionResult
            {
                Success = Success,
                RequiresConfirmation = false,
                ExecutablePath = path,
                Arguments = Arguments,
                Candidates = Candidates,
                Message = Message
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
