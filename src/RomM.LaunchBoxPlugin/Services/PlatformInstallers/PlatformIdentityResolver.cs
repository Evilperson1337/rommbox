using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Platforms.Abstractions;

namespace RomMbox.Services.PlatformInstallers
{
    internal static class PlatformIdentityResolver
    {
        private static readonly string[] VendorPrefixes =
        {
            "sony",
            "nintendo",
            "microsoft",
            "sega"
        };

        private static readonly Dictionary<string, HashSet<string>> SupportedExtensionsByInstallerKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["arcade"] = NewExtensions(".zip", ".7z"),
            ["gamecube"] = NewExtensions(".iso", ".gcm", ".rvz", ".nkit.iso", ".nkit.gcz", ".gcz", ".ciso"),
            ["n64"] = NewExtensions(".z64", ".n64", ".v64", ".zip"),
            ["ps1"] = NewExtensions(".chd", ".iso", ".pbp", ".cue", ".ccd", ".m3u"),
            ["ps2"] = NewExtensions(".iso", ".chd", ".cso", ".zso", ".bin"),
            ["psp"] = NewExtensions(".iso", ".cso", ".chd"),
            ["snes"] = NewExtensions(".zip", ".sfc", ".smc"),
            ["switch"] = NewExtensions(".xci", ".nsp", ".nsz"),
            ["flashplayer"] = NewExtensions(".swf"),
            ["wii"] = NewExtensions(".iso", ".wbfs", ".gcz", ".ciso", ".wia", ".rvz"),
            ["wiiu"] = NewExtensions(".wud", ".wux", ".wua", ".rpx"),
            ["xbox"] = NewExtensions(".iso", ".xbe"),
            ["xbox360"] = NewExtensions(".iso", ".xex", ".zar", ".zip")
        };

        public static PlatformResolutionResult Resolve(PlatformInstallerRegistry registry, PlatformResolutionEvidence evidence)
        {
            if (registry == null)
            {
                return new PlatformResolutionResult();
            }

            var installers = registry.GetAll().Values
                .Where(installer => installer != null)
                .Distinct()
                .ToArray();

            var directKey = ResolveDirectKeyMatch(registry, evidence.PlatformKey);
            if (!string.IsNullOrWhiteSpace(directKey))
            {
                return new PlatformResolutionResult
                {
                    ResolvedPlatformKey = directKey,
                    ResolutionReason = $"direct-key:{directKey}",
                    CandidateDiagnostics = new[] { $"{directKey}: direct key match" }
                };
            }

            var normalizedDisplayCandidates = BuildNameCandidates(evidence.PlatformDisplayName);
            var normalizedLaunchBoxCandidates = BuildNameCandidates(evidence.LaunchBoxPlatformName);
            var normalizedAllCandidates = normalizedDisplayCandidates
                .Concat(normalizedLaunchBoxCandidates)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedPlatformKey = NormalizePlatformToken(evidence.PlatformKey);
            var normalizedExtension = NormalizeExtension(evidence.FileExtension);
            var hasMeaningfulNameEvidence = evidence.HasMeaningfulNameEvidence();
            var hasExtensionEvidence = !string.IsNullOrWhiteSpace(normalizedExtension);

            var scoredCandidates = installers
                .Select(installer => ScoreCandidate(
                    installer,
                    evidence.PlatformKey,
                    normalizedPlatformKey,
                    normalizedDisplayCandidates,
                    normalizedLaunchBoxCandidates,
                    normalizedAllCandidates,
                    normalizedExtension,
                    hasMeaningfulNameEvidence,
                    hasExtensionEvidence))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.NameEvidenceScore)
                .ThenByDescending(candidate => candidate.ExtensionEvidenceScore)
                .ThenBy(candidate => candidate.PlatformKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scoredCandidates.Length == 0)
            {
                return new PlatformResolutionResult
                {
                    CandidateDiagnostics = Array.Empty<string>(),
                    ResolutionReason = "unresolved:no-candidates"
                };
            }

            var diagnostics = scoredCandidates
                .Select(candidate => candidate.Diagnostic)
                .ToArray();

            var best = scoredCandidates[0];
            var second = scoredCandidates.Length > 1 ? scoredCandidates[1] : null;
            var margin = second == null ? best.Score : best.Score - second.Score;
            var resolvedConfidently = best.IsConfident && margin >= 15;

            if (!resolvedConfidently)
            {
                return new PlatformResolutionResult
                {
                    IsAmbiguous = true,
                    CandidateDiagnostics = diagnostics,
                    ResolutionReason = second == null
                        ? $"ambiguous:low-confidence:{best.PlatformKey}"
                        : $"ambiguous:{best.PlatformKey}-vs-{second.PlatformKey}"
                };
            }

            return new PlatformResolutionResult
            {
                ResolvedPlatformKey = best.PlatformKey,
                ResolutionReason = best.Reason,
                UsedExtensionEvidence = best.ExtensionEvidenceScore > 0,
                CandidateDiagnostics = diagnostics
            };
        }

        private static string ResolveDirectKeyMatch(PlatformInstallerRegistry registry, string platformKey)
        {
            if (string.IsNullOrWhiteSpace(platformKey))
            {
                return string.Empty;
            }

            return registry.TryGetInstaller(platformKey, out var installer) && installer != null
                ? installer.PlatformKey ?? platformKey
                : string.Empty;
        }

        private static CandidateScore ScoreCandidate(
            IPlatformInstaller installer,
            string rawPlatformKey,
            string normalizedPlatformKey,
            IReadOnlyCollection<string> normalizedDisplayCandidates,
            IReadOnlyCollection<string> normalizedLaunchBoxCandidates,
            IReadOnlyCollection<string> normalizedAllCandidates,
            string normalizedExtension,
            bool hasMeaningfulNameEvidence,
            bool hasExtensionEvidence)
        {
            var supportedIds = Array.Empty<string>();
            var supportedAliases = Array.Empty<string>();
            if (installer is RomM.Platforms.Abstractions.IPlatformInstallerIdentityMetadata identity)
            {
                supportedIds = (identity.SupportedPlatformIds ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray();

                supportedAliases = (identity.SupportedPlatformAliases ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(NormalizePlatformToken)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var normalizedDisplayName = NormalizePlatformToken(installer.DisplayName ?? string.Empty);
            var normalizedInstallerKey = NormalizePlatformToken(installer.PlatformKey ?? string.Empty);

            var nameEvidenceScore = 0;
            var reasonParts = new List<string>();

            if (normalizedDisplayCandidates.Contains(normalizedDisplayName, StringComparer.OrdinalIgnoreCase)
                || normalizedDisplayCandidates.Contains(normalizedInstallerKey, StringComparer.OrdinalIgnoreCase)
                || normalizedDisplayCandidates.Any(candidate => supportedAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
            {
                nameEvidenceScore = 120;
                reasonParts.Add("display-name");
            }

            if (normalizedLaunchBoxCandidates.Contains(normalizedDisplayName, StringComparer.OrdinalIgnoreCase)
                || normalizedLaunchBoxCandidates.Contains(normalizedInstallerKey, StringComparer.OrdinalIgnoreCase)
                || normalizedLaunchBoxCandidates.Any(candidate => supportedAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
            {
                nameEvidenceScore = Math.Max(nameEvidenceScore, 110);
                reasonParts.Add("launchbox-name");
            }

            if (normalizedAllCandidates.Contains(normalizedDisplayName, StringComparer.OrdinalIgnoreCase)
                || normalizedAllCandidates.Contains(normalizedInstallerKey, StringComparer.OrdinalIgnoreCase)
                || normalizedAllCandidates.Any(candidate => supportedAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
            {
                nameEvidenceScore = Math.Max(nameEvidenceScore, 100);
                if (!reasonParts.Contains("alias"))
                {
                    reasonParts.Add("alias");
                }
            }

            var idEvidenceScore = 0;
            if (!string.IsNullOrWhiteSpace(rawPlatformKey)
                && supportedIds.Any(id => string.Equals(id, rawPlatformKey.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                if (!hasMeaningfulNameEvidence || nameEvidenceScore >= 100)
                {
                    idEvidenceScore = 70;
                    reasonParts.Add("platform-id");
                }
            }
            else if (!string.IsNullOrWhiteSpace(normalizedPlatformKey)
                && supportedIds.Any(id => string.Equals(NormalizePlatformToken(id), normalizedPlatformKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (!hasMeaningfulNameEvidence || nameEvidenceScore >= 100)
                {
                    idEvidenceScore = 65;
                    reasonParts.Add("platform-id");
                }
            }

            var extensionEvidenceScore = 0;
            if (hasExtensionEvidence && SupportedExtensionsByInstallerKey.TryGetValue(installer.PlatformKey ?? string.Empty, out var supportedExtensions))
            {
                if (supportedExtensions.Contains(normalizedExtension))
                {
                    extensionEvidenceScore = 25;
                    reasonParts.Add($"extension:{normalizedExtension}");
                }
                else
                {
                    extensionEvidenceScore = -40;
                    reasonParts.Add($"extension-mismatch:{normalizedExtension}");
                }
            }

            var totalScore = nameEvidenceScore + idEvidenceScore + extensionEvidenceScore;
            var isConfident = nameEvidenceScore >= 100
                || (!hasMeaningfulNameEvidence && idEvidenceScore >= 65)
                || (idEvidenceScore >= 65 && extensionEvidenceScore > 0)
                || (nameEvidenceScore >= 70 && extensionEvidenceScore > 0);

            return new CandidateScore(
                installer.PlatformKey ?? string.Empty,
                totalScore,
                nameEvidenceScore,
                extensionEvidenceScore,
                isConfident,
                string.Join("+", reasonParts),
                $"{installer.PlatformKey}: score={totalScore}, name={nameEvidenceScore}, id={idEvidenceScore}, extension={extensionEvidenceScore}, reasons=[{string.Join(", ", reasonParts)}]");
        }

        private static string NormalizePlatformToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static string NormalizeExtension(string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                return string.Empty;
            }

            var extension = fileExtension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = Path.GetExtension(extension);
            }

            return extension?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static string[] BuildNameCandidates(string value)
        {
            var normalized = NormalizePlatformToken(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Array.Empty<string>();
            }

            return ExpandNormalizedPlatformCandidates(normalized)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<string> ExpandNormalizedPlatformCandidates(string normalized)
        {
            yield return normalized;

            foreach (var prefix in VendorPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && normalized.Length > prefix.Length)
                {
                    yield return normalized.Substring(prefix.Length);
                }
            }
        }

        private static HashSet<string> NewExtensions(params string[] extensions)
        {
            return new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        private sealed record CandidateScore(
            string PlatformKey,
            int Score,
            int NameEvidenceScore,
            int ExtensionEvidenceScore,
            bool IsConfident,
            string Reason,
            string Diagnostic);
    }
}
