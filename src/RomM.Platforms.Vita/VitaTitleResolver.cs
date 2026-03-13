using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Vita.Inspection;

namespace RomM.Platforms.Vita
{
    internal static class VitaTitleResolver
    {
        public static VitaTitleResolution ResolveForInstall(VitaGameInspectionResult inspection, string installRoot, string? fallbackGameName, IPlatformLogger? logger)
        {
            if (inspection == null)
            {
                return VitaTitleResolution.Invalid("Vita inspection result missing.");
            }

            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return VitaTitleResolution.Invalid("Vita install root missing.");
            }

            var selectedBase = inspection.OrderedInstallCandidates
                .FirstOrDefault(candidate => candidate.Role == VitaContentRole.BaseGame || candidate.Role == VitaContentRole.Unknown)
                ?? inspection.OrderedInstallCandidates.FirstOrDefault();

            if (selectedBase == null)
            {
                logger?.Write(PlatformLogLevel.Warning, "Rejected Vita title resolution: no installable base candidate was selected.");
                return VitaTitleResolution.Invalid("No Vita base-game candidate was resolved.");
            }

            var titleId = NormalizeTitleId(inspection.TitleId);
            if (string.IsNullOrWhiteSpace(titleId))
            {
                titleId = NormalizeTitleId(selectedBase.TitleId);
            }

            if (string.IsNullOrWhiteSpace(titleId))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Rejected Vita title resolution for '{selectedBase.Path}': title id could not be determined.");
                return VitaTitleResolution.Invalid("Unable to determine PlayStation Vita title id safely.");
            }

            var titleName = ResolveTitleName(inspection, selectedBase, fallbackGameName);
            var canonicalDirectory = Path.Combine(installRoot, NormalizePathSegment(titleName, titleId));
            var layoutDescription = DescribeLayout(selectedBase);

            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita title path: {canonicalDirectory}");
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita title id: {titleId}");
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita install layout: {layoutDescription}");
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita base artifact: {selectedBase.Path}");

            return VitaTitleResolution.Valid(
                canonicalDirectory,
                titleId,
                titleName,
                inspection.Version,
                launchMode: "title-id",
                launchTarget: titleId,
                selectedBase.Path,
                layoutDescription);
        }

        private static string ResolveTitleName(VitaGameInspectionResult inspection, VitaContentCandidate selectedBase, string? fallbackGameName)
        {
            var candidates = new[]
            {
                inspection.TitleName,
                selectedBase.TitleName,
                fallbackGameName,
                selectedBase.FileName,
                inspection.TitleId,
                selectedBase.TitleId
            };

            return candidates.FirstOrDefault(value => IsUsableTitle(value)) ?? "Vita Game";
        }

        private static bool IsUsableTitle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            return !string.Equals(trimmed, "TITLE", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(trimmed, "TITLE_ID", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(trimmed, "APP_VER", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeLayout(VitaContentCandidate candidate)
        {
            return $"Role={candidate.Role}, Format={candidate.Format}, Path='{candidate.Path}'";
        }

        private static string NormalizeTitleId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static string NormalizePathSegment(string? value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalid, '_');
            }

            normalized = normalized.Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }
    }

    internal sealed class VitaTitleResolution
    {
        private VitaTitleResolution()
        {
        }

        public bool IsValid { get; private set; }

        public string ValidationError { get; private set; } = string.Empty;

        public string CanonicalGameDirectory { get; private set; } = string.Empty;

        public string TitleId { get; private set; } = string.Empty;

        public string TitleName { get; private set; } = string.Empty;

        public string Version { get; private set; } = string.Empty;

        public string LaunchMode { get; private set; } = string.Empty;

        public string LaunchTarget { get; private set; } = string.Empty;

        public string SelectedBaseArtifactPath { get; private set; } = string.Empty;

        public string LayoutDescription { get; private set; } = string.Empty;

        public static VitaTitleResolution Invalid(string message)
        {
            return new VitaTitleResolution
            {
                IsValid = false,
                ValidationError = message ?? string.Empty
            };
        }

        public static VitaTitleResolution Valid(string canonicalGameDirectory, string titleId, string titleName, string version, string launchMode, string launchTarget, string selectedBaseArtifactPath, string layoutDescription)
        {
            return new VitaTitleResolution
            {
                IsValid = true,
                CanonicalGameDirectory = canonicalGameDirectory ?? string.Empty,
                TitleId = titleId ?? string.Empty,
                TitleName = titleName ?? string.Empty,
                Version = version ?? string.Empty,
                LaunchMode = launchMode ?? string.Empty,
                LaunchTarget = launchTarget ?? string.Empty,
                SelectedBaseArtifactPath = selectedBaseArtifactPath ?? string.Empty,
                LayoutDescription = layoutDescription ?? string.Empty
            };
        }
    }
}
