using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomM.Platforms.RomBase
{
    internal sealed class RomFileFinder
    {
        private readonly IReadOnlyList<string> _extensions;

        public RomFileFinder(IReadOnlyList<string> extensions)
        {
            _extensions = NormalizeExtensions(extensions);
        }

        public IReadOnlyList<string> FindCandidates(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<string>();
            }

            if (File.Exists(path))
            {
                return IsRomCandidate(path) ? new List<string> { path } : Array.Empty<string>();
            }

            if (!Directory.Exists(path))
            {
                return Array.Empty<string>();
            }

            var results = new List<string>();
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (IsRomCandidate(file))
                {
                    results.Add(file);
                }
            }

            return results;
        }

        public bool IsRomCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return _extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<string> NormalizeExtensions(IReadOnlyList<string> extensions)
        {
            if (extensions == null || extensions.Count == 0)
            {
                return Array.Empty<string>();
            }

            return extensions
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.StartsWith(".", StringComparison.Ordinal) ? value.Trim() : "." + value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
