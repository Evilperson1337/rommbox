using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Install;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.PS4.Inspection;

namespace RomM.Platforms.PS4
{
    public sealed class Ps4PlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        public string PlatformKey => "ps4";
        public string DisplayName => "PlayStation 4";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "ps4", "sony-playstation-4" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "ps4",
            "playstation4",
            "playstation 4",
            "sony playstation 4",
            "sonyplaystation4"
        };

        public PlatformInstallerCapabilities Capabilities => new PlatformInstallerCapabilities
        {
            SupportsArchives = true,
            SupportsDirectFiles = true,
            RequiresStagingInspection = true,
            SupportsAutoFormatDetection = true,
            SupportsInstaller = false,
            SupportsSilentInstaller = false,
            SupportsUninstall = true,
            SupportsInstallStateDetection = true,
            SupportsApplicationPathDiscovery = true,
            SupportsDlc = true,
            SupportsUpdates = true,
            SupportsRaps = false,
            RequiresEmulatorPath = false
        };

        public PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "ShadPs4ExecutablePath",
                        Label = "ShadPS4 Executable",
                        Description = "Optional ShadPS4 executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4GamesDirectory",
                        Label = "PS4 Games Directory",
                        Description = "Optional PS4 install directory override used for serial-based layout.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4ExternalPkgExtractorPath",
                        Label = "External PKG Extractor",
                        Description = "Optional external extractor used for direct .pkg downloads.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = true
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4FailIfDirectPkgExtractorMissing",
                        Label = "Fail if direct PKG extractor is missing",
                        Description = "If enabled, direct PKG download fails immediately when extractor path is missing.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "false"
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "SupportedFileTypes",
                        Label = "Supported File Formats",
                        Description = "Comma-separated extensions used for PS4 content discovery.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = false,
                        DefaultValue = ".pkg,.zip,.7z"
                    }
                }
            };
        }

        public Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError> { new() { Code = "context_missing", Message = "Platform context missing." } };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "missing_installed_path", Message = "Installed path missing." } };
                return Task.FromResult(result);
            }

            if (Directory.Exists(installedPath))
            {
                result.RecommendedExecutablePath = installedPath;
                result.CandidateExecutablePaths = new List<string> { installedPath };
                return Task.FromResult(result);
            }

            if (File.Exists(installedPath))
            {
                var directory = Path.GetDirectoryName(installedPath) ?? string.Empty;
                result.RecommendedExecutablePath = !string.IsNullOrWhiteSpace(directory) ? directory : installedPath;
                result.CandidateExecutablePaths = new List<string> { result.RecommendedExecutablePath };
                return Task.FromResult(result);
            }

            result.IsInstalled = false;
            result.Warnings = new List<DetectionWarning> { new() { Code = "installed_missing", Message = "Installed PS4 content not found on disk." } };
            return Task.FromResult(result);
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var basePath = ResolveBasePath(ctx?.InstalledPath, ctx?.InstallRootPath);
            var valid = !string.IsNullOrWhiteSpace(basePath) && Directory.Exists(basePath);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PS4 install verified." : "PS4 install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation 4 install pipeline...", 0));

            var platformInstallRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.Settings?.Ps4GamesDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(platformInstallRoot))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "PS4 install directory missing." });
            }

            Directory.CreateDirectory(platformInstallRoot);

            var gameInstallRoot = ResolveGameInstallRoot(ctx, platformInstallRoot);
            if (string.IsNullOrWhiteSpace(gameInstallRoot))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "PS4 game install directory could not be resolved." });
            }

            Directory.CreateDirectory(gameInstallRoot);

            var extractorPath = ctx.Settings?.Ps4ExternalPkgExtractorPath ?? string.Empty;
            var extractorConfigured = !string.IsNullOrWhiteSpace(extractorPath) && File.Exists(extractorPath);
            var stageRoot = ResolveStageRoot(ctx, platformInstallRoot);
            var pipeline = new Ps4InstallPipelineContext(ctx, platformInstallRoot, gameInstallRoot, stageRoot, extractorPath, extractorConfigured);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS4 install pipeline started. PlatformRoot='{platformInstallRoot}', GameRoot='{gameInstallRoot}', StagingRoot='{stageRoot}', ArchivePath='{ctx.ArchivePath ?? string.Empty}', ExtractedPath='{ctx.ExtractedPath ?? string.Empty}'.");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS4 PKG extractor configured: {extractorConfigured}. Path='{extractorPath}'.");

            try
            {
                RunStage(pipeline, Ps4InstallStage.AcquireSource, "Acquiring downloaded source.", () => AcquireSource(pipeline));
                progress?.Report(new InstallProgress("Installing", "Classifying PS4 source...", 10));
                RunStage(pipeline, Ps4InstallStage.ClassifySource, "Classifying PS4 input.", () => ClassifySource(pipeline));

                if (pipeline.SourceForm == Ps4ContentFormat.Archive)
                {
                    progress?.Report(new InstallProgress("Installing", "Expanding PS4 archive into staging...", 20));
                    RunStage(pipeline, Ps4InstallStage.ExpandArchiveToStaging, "Using extracted archive staging.", () => ExpandArchiveToStaging(pipeline));
                }
                else if (pipeline.SourceForm == Ps4ContentFormat.Folder)
                {
                    progress?.Report(new InstallProgress("Installing", "Copying PS4 folder content into staging...", 20));
                    RunStage(pipeline, Ps4InstallStage.ExpandArchiveToStaging, "Copying folder source into staging.", () => StageFolderSource(pipeline));
                }

                progress?.Report(new InstallProgress("Installing", "Scanning staged PS4 content...", 30));
                RunStage(pipeline, Ps4InstallStage.ClassifyStagedContent, "Scanning staged content.", () => ClassifyStagedContent(pipeline));

                progress?.Report(new InstallProgress("Installing", "Transforming PKG content...", 45));
                RunStage(pipeline, Ps4InstallStage.TransformPkgToFolder, "Expanding PKG content into staging.", () => TransformPkgContent(pipeline));

                progress?.Report(new InstallProgress("Installing", "Normalizing PS4 content layout...", 60));
                RunStage(pipeline, Ps4InstallStage.NormalizeContentLayout, "Normalizing staged content layout.", () => NormalizeContentLayout(pipeline));

                progress?.Report(new InstallProgress("Installing", "Moving normalized PS4 content into final install folders...", 75));
                RunStage(pipeline, Ps4InstallStage.InstallToFinalLocation, "Installing normalized content.", () => InstallToFinalLocation(pipeline));

                progress?.Report(new InstallProgress("Installing", "Resolving PS4 playable binary...", 85));
                RunStage(pipeline, Ps4InstallStage.ResolvePlayableBinary, "Resolving base-game eboot.bin.", () => ResolvePlayableBinary(pipeline));

                progress?.Report(new InstallProgress("Installing", "Finalizing PS4 install...", 95));
                RunStage(pipeline, Ps4InstallStage.FinalizeInstall, "Finalizing PS4 install metadata.", () => FinalizeInstall(pipeline));
                RunStage(pipeline, Ps4InstallStage.Cleanup, "Cleaning PS4 staging.", () => CleanupStaging(pipeline));
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, $"PS4 install aborted during stage '{pipeline.CurrentStage}': {ex.Message}");
                return Task.FromResult(new InstallResult
                {
                    Success = false,
                    Message = $"PS4 install failed during stage '{pipeline.CurrentStage}': {ex.Message}"
                });
            }

            progress?.Report(new InstallProgress("Installing", "PS4 install completed.", 100));
            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "PlayStation 4 install completed.",
                ExecutablePath = pipeline.ResolvedPlayablePath,
                Arguments = Array.Empty<string>(),
                InstallType = InstallType.Portable,
                InstallRootPath = gameInstallRoot
            });
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "PS4 uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PS4 uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var basePath = ResolveBasePath(ctx.InstalledPath, ctx.InstallRootPath);
            var titleId = ExtractTitleId(Path.GetFileName(basePath ?? string.Empty));
            if (string.IsNullOrWhiteSpace(titleId))
            {
                notes.Add("Unable to determine PS4 title id for uninstall scope.");
            }
            else
            {
                var gameRoot = ResolveGameRootForUninstall(basePath, ctx.InstallRootPath, ctx.GameName);
                var platformRoot = Path.GetDirectoryName(gameRoot ?? string.Empty) ?? ctx.InstallRootPath ?? string.Empty;
                var safeGameRoot = gameRoot ?? string.Empty;
                var targets = new[]
                {
                    Path.Combine(safeGameRoot, titleId),
                    Path.Combine(safeGameRoot, titleId + "-patch"),
                    Path.Combine(safeGameRoot, titleId + "-dlc"),
                    Path.Combine(safeGameRoot, titleId + "-bonus")
                };

                var bonusTargets = Directory.Exists(safeGameRoot)
                    ? Directory.EnumerateDirectories(safeGameRoot)
                        .Where(path => !string.IsNullOrWhiteSpace(ExtractTitleId(Path.GetFileName(path) ?? string.Empty))
                            && !string.Equals(path, Path.Combine(safeGameRoot, titleId), StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(path, Path.Combine(safeGameRoot, titleId + "-patch"), StringComparison.OrdinalIgnoreCase))
                    : Enumerable.Empty<string>();

                foreach (var target in targets.Concat(bonusTargets).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(target))
                    {
                        continue;
                    }

                    try
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Removing installed serial folder: {target}");
                        Directory.Delete(target, true);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        var message = $"Failed to remove '{target}': {ex.Message}";
                        notes.Add(message);
                        ctx.Logger?.Write(PlatformLogLevel.Warning, message);
                    }
                }

                var cleanupRoot = gameRoot ?? string.Empty;
                TryDeleteDirectoryIfEmpty(cleanupRoot, ctx.Logger);
                TryDeleteDirectoryIfEmpty(Path.Combine(cleanupRoot, ".staging"), ctx.Logger);
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Cleaning install state");

            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PlayStation 4 uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static void RunStage(Ps4InstallPipelineContext pipeline, Ps4InstallStage stage, string description, Action action)
        {
            pipeline.CurrentStage = stage;
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 stage '{stage}' started. {description}");
            action();
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 stage '{stage}' completed.");
        }

        private static void AcquireSource(Ps4InstallPipelineContext pipeline)
        {
            pipeline.SourcePath = ResolveSourcePath(pipeline.Context.ArchivePath, pipeline.Context.ExtractedPath);
            if (string.IsNullOrWhiteSpace(pipeline.SourcePath))
            {
                throw new InvalidOperationException("No PS4 source artifact was provided.");
            }

            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 source acquired: '{pipeline.SourcePath}'.");
        }

        private static void ClassifySource(Ps4InstallPipelineContext pipeline)
        {
            pipeline.SourceForm = ClassifyPath(pipeline.Context.ArchivePath, pipeline.SourcePath);
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 source type detected: {pipeline.SourceForm}.");

            if (pipeline.SourceForm == Ps4ContentFormat.Unknown)
            {
                throw new InvalidOperationException($"Unsupported PS4 source '{pipeline.SourcePath}'.");
            }
        }

        private static void ExpandArchiveToStaging(Ps4InstallPipelineContext pipeline)
        {
            if (string.IsNullOrWhiteSpace(pipeline.Context.ExtractedPath) || !Directory.Exists(pipeline.Context.ExtractedPath))
            {
                throw new InvalidOperationException("PS4 archive extraction output is missing. Archive content must be extracted into staging before install.");
            }

            pipeline.ActiveStagingRoot = Path.GetFullPath(pipeline.Context.ExtractedPath);
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 archive extracted to staging: '{pipeline.ActiveStagingRoot}'.");
        }

        private static void StageFolderSource(Ps4InstallPipelineContext pipeline)
        {
            if (string.IsNullOrWhiteSpace(pipeline.SourcePath) || !Directory.Exists(pipeline.SourcePath))
            {
                throw new InvalidOperationException("PS4 folder-format source is missing.");
            }

            pipeline.ActiveStagingRoot = pipeline.SourcePath;
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 folder-format source will be normalized in place from: '{pipeline.ActiveStagingRoot}'.");
        }

        private static void ClassifyStagedContent(Ps4InstallPipelineContext pipeline)
        {
            pipeline.DiscoveredItems.Clear();

            if (pipeline.SourceForm == Ps4ContentFormat.Pkg)
            {
                pipeline.DiscoveredItems.Add(new Ps4ContentCandidate
                {
                    Path = pipeline.SourcePath,
                    RelativePath = Path.GetFileName(pipeline.SourcePath) ?? string.Empty,
                    ContentRole = Ps4ContentRole.BaseGame,
                    ContentFormat = Ps4ContentFormat.Pkg,
                    TitleId = ExtractTitleId(Path.GetFileName(pipeline.SourcePath) ?? string.Empty),
                    TitleName = Path.GetFileNameWithoutExtension(pipeline.SourcePath) ?? string.Empty,
                    RequiresExternalExtractor = true,
                    IsSupportedForInstall = true
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(pipeline.ActiveStagingRoot) || !Directory.Exists(pipeline.ActiveStagingRoot))
                {
                    throw new InvalidOperationException("PS4 staging root is missing before content classification.");
                }

                var inspector = new Ps4GameInspector();
                var inspection = inspector.Inspect(
                    archivePath: null,
                    extractedPath: pipeline.ActiveStagingRoot,
                    gameName: pipeline.Context.GameName,
                    options: new Ps4InspectorOptions
                    {
                        ArchiveExtractionEnabled = true,
                        DirectPkgSupportEnabled = true,
                        AllowExtractedPkgInstall = true
                    },
                    logger: pipeline.Context.Logger);

                foreach (var warning in inspection.Warnings)
                {
                    pipeline.Context.Logger?.Write(PlatformLogLevel.Warning, warning);
                }

                pipeline.DiscoveredItems.AddRange(inspection.DetectedItems);
            }

            if (pipeline.DiscoveredItems.Count == 0)
            {
                throw new InvalidOperationException("No PS4 content was discovered in staging.");
            }

            foreach (var candidate in pipeline.DiscoveredItems.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 staged content discovered: Role='{candidate.ContentRole}', Form='{candidate.ContentFormat}', Path='{candidate.Path}'.");
            }
        }

        private static void TransformPkgContent(Ps4InstallPipelineContext pipeline)
        {
            var folderItems = pipeline.DiscoveredItems
                .Where(item => item.ContentFormat == Ps4ContentFormat.Folder)
                .ToList();
            var pkgItems = pipeline.DiscoveredItems
                .Where(item => item.ContentFormat == Ps4ContentFormat.Pkg)
                .ToList();

            if (pkgItems.Count == 0)
            {
                pipeline.FolderItems = folderItems;
                pipeline.InstallPlan = BuildInstallPlan(folderItems, pipeline.Context.Logger);
                return;
            }

            if (!pipeline.ExtractorConfigured)
            {
                throw new InvalidOperationException("PKG content detected but the PS4 PKG extractor path is not configured.");
            }

            var pkgExtractionRoot = Path.Combine(pipeline.StageRoot, "pkg-expanded");
            Directory.CreateDirectory(pkgExtractionRoot);

            foreach (var pkgItem in OrderCandidates(pkgItems))
            {
                var roleFolder = pkgItem.ContentRole.ToString().ToLowerInvariant();
                var pkgOutput = Path.Combine(pkgExtractionRoot, roleFolder, SanitizePathSegment(Path.GetFileNameWithoutExtension(pkgItem.Path) ?? "pkg"));
                RecreateDirectory(pkgOutput);
                ExtractPkgToDirectory(pkgItem.Path, pkgOutput, pipeline.ExtractorPath, pipeline.Context.Logger);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 PKG extraction output path: '{pkgOutput}'.");

                var inspector = new Ps4GameInspector();
                var inspection = inspector.Inspect(
                    archivePath: null,
                    extractedPath: pkgOutput,
                    gameName: pipeline.Context.GameName,
                    options: new Ps4InspectorOptions
                    {
                        ArchiveExtractionEnabled = true,
                        DirectPkgSupportEnabled = true,
                        AllowExtractedPkgInstall = true
                    },
                    logger: pipeline.Context.Logger);

                var extractedFolders = inspection.DetectedItems
                    .Where(item => item.ContentFormat == Ps4ContentFormat.Folder)
                    .ToList();

                if (extractedFolders.Count == 0)
                {
                    throw new InvalidOperationException($"PKG '{pkgItem.Path}' extracted successfully but produced no folder-format content.");
                }

                foreach (var folder in extractedFolders)
                {
                    if (folder.ContentRole == Ps4ContentRole.Unknown || folder.ContentRole == Ps4ContentRole.BaseGame)
                    {
                        folder.ContentRole = pkgItem.ContentRole;
                    }

                    pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 content role classification: Role='{folder.ContentRole}', Form='{folder.ContentFormat}', Path='{folder.Path}'.");
                    folderItems.Add(folder);
                }
            }

            pipeline.FolderItems = folderItems;
            pipeline.InstallPlan = BuildInstallPlan(folderItems, pipeline.Context.Logger);
        }

        private static void NormalizeContentLayout(Ps4InstallPipelineContext pipeline)
        {
            if (pipeline.InstallPlan.Count == 0)
            {
                throw new InvalidOperationException("No folder-format PS4 content is available after PKG processing.");
            }

            var baseCandidate = pipeline.InstallPlan.FirstOrDefault(step => step.Role == Ps4ContentRole.BaseGame)?.Candidate;
            if (baseCandidate == null)
            {
                throw new InvalidOperationException("Base game content could not be identified in staged PS4 content.");
            }

            var titleId = !string.IsNullOrWhiteSpace(baseCandidate.TitleId)
                ? baseCandidate.TitleId
                : ExtractTitleId(Path.GetFileName(baseCandidate.Path) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(titleId))
            {
                throw new InvalidOperationException("Unable to resolve the PS4 base-game title id from staged content.");
            }

            pipeline.BaseTitleId = titleId;
            pipeline.NormalizedRoot = Path.Combine(pipeline.StageRoot, "normalized");
            RecreateDirectory(pipeline.NormalizedRoot);

            pipeline.NormalizedBaseRoot = Path.Combine(pipeline.NormalizedRoot, titleId);
            MaterializeNormalizedContent(baseCandidate.Path, pipeline.NormalizedBaseRoot, pipeline, Ps4ContentRole.BaseGame);
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 normalized base content: '{baseCandidate.Path}' -> '{pipeline.NormalizedBaseRoot}'.");

            var updateTarget = Path.Combine(pipeline.NormalizedRoot, titleId + "-patch");
            foreach (var update in pipeline.InstallPlan.Where(step => step.Role == Ps4ContentRole.Update).Select(step => step.Candidate))
            {
                MaterializeNormalizedContent(update.Path, updateTarget, pipeline, Ps4ContentRole.Update);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 normalized update content: '{update.Path}' -> '{updateTarget}'.");
            }

            var dlcTargetRoot = pipeline.NormalizedBaseRoot;
            foreach (var dlc in pipeline.InstallPlan.Where(step => step.Role == Ps4ContentRole.Dlc).Select(step => step.Candidate))
            {
                MaterializeNormalizedContent(dlc.Path, dlcTargetRoot, pipeline, Ps4ContentRole.Dlc);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 normalized DLC content: '{dlc.Path}' -> '{dlcTargetRoot}'.");
            }

            var bonusTargetRoot = Path.Combine(pipeline.NormalizedRoot, ResolveBonusTargetFolderName(pipeline, titleId));
            foreach (var bonus in pipeline.InstallPlan.Where(step => step.Role == Ps4ContentRole.Bonus).Select(step => step.Candidate))
            {
                var bonusFolderName = !string.IsNullOrWhiteSpace(bonus.TitleId)
                    ? bonus.TitleId
                    : (Path.GetFileName(bonus.Path) ?? SanitizePathSegment(bonus.TitleName));
                var target = Path.Combine(pipeline.NormalizedRoot, SanitizePathSegment(bonusFolderName));
                MaterializeNormalizedContent(bonus.Path, target, pipeline, Ps4ContentRole.Bonus);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 normalized bonus content: '{bonus.Path}' -> '{target}'.");
            }

            CleanupSourceWrapperDirectories(pipeline);
        }

        private static void InstallToFinalLocation(Ps4InstallPipelineContext pipeline)
        {
            if (string.IsNullOrWhiteSpace(pipeline.NormalizedRoot) || !Directory.Exists(pipeline.NormalizedRoot))
            {
                throw new InvalidOperationException("Normalized PS4 content layout is missing.");
            }

            foreach (var role in InstallRoleOrder)
            {
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 install plan execution step started: Role='{role}'.");

                switch (role)
                {
                    case Ps4ContentRole.BaseGame:
                        pipeline.FinalBaseRoot = Path.Combine(pipeline.GameInstallRoot, pipeline.BaseTitleId);
                        RecreateDirectory(pipeline.FinalBaseRoot);
                        MoveOrMergeDirectory(pipeline.NormalizedBaseRoot, pipeline.FinalBaseRoot);
                        pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 final install destination (base): '{pipeline.FinalBaseRoot}'.");
                        break;
                    case Ps4ContentRole.Update:
                    {
                        var normalizedUpdateRoot = Path.Combine(pipeline.NormalizedRoot, pipeline.BaseTitleId + "-patch");
                        if (Directory.Exists(normalizedUpdateRoot))
                        {
                            pipeline.FinalPatchRoot = Path.Combine(pipeline.GameInstallRoot, pipeline.BaseTitleId + "-patch");
                            RecreateDirectory(pipeline.FinalPatchRoot);
                            MoveOrMergeDirectory(normalizedUpdateRoot, pipeline.FinalPatchRoot);
                            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 final install destination (update): '{pipeline.FinalPatchRoot}'.");
                        }

                        break;
                    }
                    case Ps4ContentRole.Dlc:
                    {
                        if (Directory.Exists(pipeline.NormalizedBaseRoot))
                        {
                            pipeline.FinalDlcRoot = pipeline.FinalBaseRoot;
                            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 final install destination (dlc): '{pipeline.FinalDlcRoot}'.");
                        }

                        break;
                    }
                    case Ps4ContentRole.Bonus:
                    {
                        var normalizedBonusRoots = pipeline.InstallPlan
                            .Where(step => step.Role == Ps4ContentRole.Bonus)
                            .Select(step => !string.IsNullOrWhiteSpace(step.Candidate.TitleId)
                                ? step.Candidate.TitleId
                                : Path.GetFileName(step.Candidate.Path) ?? string.Empty)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Select(name => Path.Combine(pipeline.NormalizedRoot, SanitizePathSegment(name)))
                            .Where(Directory.Exists)
                            .ToArray();

                        foreach (var normalizedBonusRoot in normalizedBonusRoots)
                        {
                            pipeline.FinalBonusRoot = Path.Combine(pipeline.GameInstallRoot, Path.GetFileName(normalizedBonusRoot));
                            RecreateDirectory(pipeline.FinalBonusRoot);
                            MoveOrMergeDirectory(normalizedBonusRoot, pipeline.FinalBonusRoot);
                            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 final install destination (bonus): '{pipeline.FinalBonusRoot}'.");
                        }

                        break;
                    }
                }

                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 install plan execution step completed: Role='{role}'.");
            }
        }

        private static void ResolvePlayableBinary(Ps4InstallPipelineContext pipeline)
        {
            if (string.IsNullOrWhiteSpace(pipeline.FinalBaseRoot) || !Directory.Exists(pipeline.FinalBaseRoot))
            {
                throw new InvalidOperationException("Installed PS4 base-game directory is missing.");
            }

            var ebootPath = Directory.EnumerateFiles(pipeline.FinalBaseRoot, "eboot.bin", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(ebootPath) || !File.Exists(ebootPath))
            {
                throw new InvalidOperationException("Unable to resolve base-game eboot.bin after install.");
            }

            pipeline.ResolvedPlayablePath = ebootPath;
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 resolved eboot.bin path: '{pipeline.ResolvedPlayablePath}'.");
        }

        private static void FinalizeInstall(Ps4InstallPipelineContext pipeline)
        {
            CleanupSuccessfulPkgArtifacts(pipeline);
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 install finalization complete. TitleId='{pipeline.BaseTitleId}', ApplicationPath='{pipeline.ResolvedPlayablePath}'.");
        }

        private static void CleanupStaging(Ps4InstallPipelineContext pipeline)
        {
            if (string.IsNullOrWhiteSpace(pipeline.StageRoot) || !Directory.Exists(pipeline.StageRoot))
            {
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, "PS4 staging cleanup skipped because staging root no longer exists.");
                return;
            }

            Directory.Delete(pipeline.StageRoot, recursive: true);

            var stagingParent = Path.GetDirectoryName(pipeline.StageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(stagingParent)
                && Directory.Exists(stagingParent)
                && !Directory.EnumerateFileSystemEntries(stagingParent).Any())
            {
                Directory.Delete(stagingParent, recursive: false);
            }

            var platformStagingRoot = Path.GetDirectoryName(stagingParent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(platformStagingRoot)
                && string.Equals(Path.GetFileName(platformStagingRoot), ".staging", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(platformStagingRoot)
                && !Directory.EnumerateFileSystemEntries(platformStagingRoot).Any())
            {
                Directory.Delete(platformStagingRoot, recursive: false);
            }

            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 cleanup completed for staging root '{pipeline.StageRoot}'.");
        }

        private static string ResolveInstallRoot(string? installDirectory, string? ps4GamesDirectory, string? romRootPath)
        {
            if (!string.IsNullOrWhiteSpace(ps4GamesDirectory))
            {
                return ps4GamesDirectory;
            }

            if (!string.IsNullOrWhiteSpace(romRootPath))
            {
                return romRootPath;
            }

            return installDirectory ?? string.Empty;
        }

        private static string ResolveGameInstallRoot(InstallContext ctx, string platformInstallRoot)
        {
            if (!string.IsNullOrWhiteSpace(ctx.ExtractedPath)
                && Directory.Exists(ctx.ExtractedPath)
                && GameInstallPathHelper.IsPathUnderDirectory(ctx.ExtractedPath, platformInstallRoot))
            {
                return Path.GetFullPath(ctx.ExtractedPath);
            }

            return GameInstallPathHelper.ResolveGameDirectory(platformInstallRoot, ctx.GameName);
        }

        private static string ResolveStageRoot(InstallContext ctx, string installRoot)
        {
            if (!string.IsNullOrWhiteSpace(ctx.StagingDirectory))
            {
                if (!string.IsNullOrWhiteSpace(ctx.ExtractedPath)
                    && Directory.Exists(ctx.ExtractedPath)
                    && (IsPathUnderDirectory(ctx.StagingDirectory, ctx.ExtractedPath)
                        || IsPathUnderDirectory(ctx.ExtractedPath, ctx.StagingDirectory)
                        || string.Equals(
                            Path.GetFullPath(ctx.StagingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            Path.GetFullPath(ctx.ExtractedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase)))
                {
                    var safeParent = Path.GetDirectoryName(Path.GetFullPath(ctx.ExtractedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                        ?? installRoot;
                    return Path.Combine(safeParent, ".ps4-staging", Guid.NewGuid().ToString("N"));
                }

                return ctx.StagingDirectory;
            }

            var parent = !string.IsNullOrWhiteSpace(ctx.ExtractedPath)
                ? Path.GetDirectoryName(Path.GetFullPath(ctx.ExtractedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                : null;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }

            return Path.Combine(installRoot, ".staging", Guid.NewGuid().ToString("N"));
        }

        private static string ResolveSourcePath(string? archivePath, string? extractedPath)
        {
            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                return archivePath;
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && (Directory.Exists(extractedPath) || File.Exists(extractedPath)))
            {
                return extractedPath;
            }

            return string.Empty;
        }

        private static Ps4ContentFormat ClassifyPath(string? archivePath, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return Ps4ContentFormat.Unknown;
            }

            if (Directory.Exists(sourcePath))
            {
                return Ps4ContentFormat.Folder;
            }

            if (!File.Exists(sourcePath))
            {
                return Ps4ContentFormat.Unknown;
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (string.Equals(extension, ".pkg", StringComparison.OrdinalIgnoreCase))
            {
                return Ps4ContentFormat.Pkg;
            }

            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase))
            {
                return Ps4ContentFormat.Archive;
            }

            if (!string.IsNullOrWhiteSpace(archivePath)
                && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            {
                return Ps4ContentFormat.Unknown;
            }

            return Ps4ContentFormat.Unknown;
        }

        private static bool IsDirectPkg(string? archivePath)
        {
            return !string.IsNullOrWhiteSpace(archivePath)
                   && File.Exists(archivePath)
                   && string.Equals(Path.GetExtension(archivePath), ".pkg", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveBasePath(string? installedPath, string? installRootPath)
        {
            if (!string.IsNullOrWhiteSpace(installedPath) && Directory.Exists(installedPath))
            {
                return installedPath;
            }

            if (!string.IsNullOrWhiteSpace(installedPath) && File.Exists(installedPath))
            {
                return Path.GetDirectoryName(installedPath) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(installRootPath) && Directory.Exists(installRootPath))
            {
                return installRootPath;
            }

            return string.Empty;
        }

        private static string ResolveGameRootForUninstall(string? basePath, string? installRootPath, string? gameName)
        {
            if (!string.IsNullOrWhiteSpace(installRootPath) && Directory.Exists(installRootPath))
            {
                var installRootName = Path.GetFileName(installRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
                var normalizedGameFolder = NormalizeGameFolderName(gameName, string.Empty);
                if (!string.IsNullOrWhiteSpace(normalizedGameFolder)
                    && string.Equals(installRootName, normalizedGameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return installRootPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(basePath) && Directory.Exists(basePath))
            {
                var parent = Path.GetDirectoryName(basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.IsNullOrWhiteSpace(parent) ? basePath : parent;
            }

            return basePath ?? string.Empty;
        }

        private static string ResolveInstalledContentPath(string targetDirectory, string titleId)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return targetDirectory ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(titleId))
            {
                var nestedTitleDirectory = Path.Combine(targetDirectory, titleId);
                if (Directory.Exists(nestedTitleDirectory))
                {
                    return nestedTitleDirectory;
                }
            }

            var childDirectories = Directory.EnumerateDirectories(targetDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (childDirectories.Length == 1)
            {
                var childTitleId = ExtractTitleId(Path.GetFileName(childDirectories[0]) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(childTitleId))
                {
                    return childDirectories[0];
                }
            }

            return targetDirectory;
        }

        private static string ResolveBonusTargetFolderName(Ps4InstallPipelineContext pipeline, string baseTitleId)
        {
            var bonusTitleId = pipeline.InstallPlan
                .Where(step => step.Role == Ps4ContentRole.Bonus)
                .Select(step => step.Candidate.TitleId)
                .FirstOrDefault(titleId => !string.IsNullOrWhiteSpace(titleId));

            return !string.IsNullOrWhiteSpace(bonusTitleId)
                ? bonusTitleId
                : baseTitleId + "-bonus";
        }

        private static string NormalizeGameFolderName(string? gameName, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(gameName) ? fallback : gameName;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Game";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Game" : cleaned;
        }

        private static Ps4ContentCandidate? SelectBaseCandidate(IEnumerable<Ps4ContentCandidate> items)
        {
            return items
                .Where(item => item.ContentRole == Ps4ContentRole.BaseGame && item.ContentFormat == Ps4ContentFormat.Folder)
                .OrderByDescending(item => item.HasPlayableBinary)
                .ThenBy(item => item.Path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static IReadOnlyList<Ps4InstallPlanStep> BuildInstallPlan(IEnumerable<Ps4ContentCandidate> folderItems, IPlatformLogger? logger)
        {
            var orderedCandidates = OrderCandidates(folderItems.Where(item => item.ContentFormat == Ps4ContentFormat.Folder)).ToList();
            var baseCandidate = SelectBaseCandidate(orderedCandidates);
            if (baseCandidate == null)
            {
                return Array.Empty<Ps4InstallPlanStep>();
            }

            var steps = new List<Ps4InstallPlanStep>
            {
                new(baseCandidate.ContentRole, baseCandidate)
            };

            foreach (var candidate in orderedCandidates)
            {
                if (ReferenceEquals(candidate, baseCandidate))
                {
                    continue;
                }

                if (candidate.ContentRole == Ps4ContentRole.BaseGame)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Ignoring additional PS4 base-game candidate after plan selection: '{candidate.Path}'.");
                    continue;
                }

                steps.Add(new Ps4InstallPlanStep(candidate.ContentRole, candidate));
            }

            logger?.Write(PlatformLogLevel.Info, $"PS4 final install plan order: {FormatInstallPlan(steps)}.");
            return steps;
        }

        private static IEnumerable<Ps4ContentCandidate> OrderCandidates(IEnumerable<Ps4ContentCandidate> candidates)
        {
            return candidates
                .OrderBy(candidate => GetRoleOrder(candidate.ContentRole))
                .ThenByDescending(candidate => candidate.HasPlayableBinary)
                .ThenBy(candidate => candidate.Path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);
        }

        private static int GetRoleOrder(Ps4ContentRole role)
        {
            return role switch
            {
                Ps4ContentRole.BaseGame => 0,
                Ps4ContentRole.Update => 1,
                Ps4ContentRole.Dlc => 2,
                Ps4ContentRole.Bonus => 3,
                _ => 4
            };
        }

        private static string FormatInstallPlan(IEnumerable<Ps4InstallPlanStep> steps)
        {
            var builder = new StringBuilder();
            foreach (var step in steps)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(step.Role);
                builder.Append('(');
                builder.Append(Path.GetFileName(step.Candidate.Path));
                builder.Append(')');
            }

            return builder.ToString();
        }

        private static string ExtractTitleId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var upper = text.ToUpperInvariant();
            var markers = new[] { "CUSA", "CUSB", "CUSC", "PCAS", "PCJS", "PPSA", "PPSH" };
            foreach (var marker in markers)
            {
                var index = upper.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0 || index + 9 > upper.Length)
                {
                    continue;
                }

                var candidate = upper.Substring(index, 9);
                if (candidate.Skip(4).All(char.IsDigit))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static bool IsPathUnderDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanupSuccessfulPkgArtifacts(Ps4InstallPipelineContext pipeline)
        {
            var cleanupRoot = pipeline.SourceForm == Ps4ContentFormat.Folder
                ? pipeline.SourcePath
                : pipeline.GameInstallRoot;

            if (string.IsNullOrWhiteSpace(cleanupRoot) || !Directory.Exists(cleanupRoot))
            {
                return;
            }

            var removedCount = 0;
            foreach (var pkgPath in Directory.EnumerateFiles(cleanupRoot, "*.pkg", SearchOption.AllDirectories).ToArray())
            {
                try
                {
                    File.Delete(pkgPath);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    pipeline.Context.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete extracted PS4 PKG artifact '{pkgPath}': {ex.Message}");
                }
            }

            RemoveEmptyDirectories(cleanupRoot, pipeline.Context.Logger);

            if (removedCount > 0)
            {
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"Removed {removedCount} PS4 PKG artifacts after successful extraction from '{cleanupRoot}'.");
            }
        }

        private static void MaterializeNormalizedContent(string sourcePath, string destinationPath, Ps4InstallPipelineContext pipeline, Ps4ContentRole role)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"PS4 {role} source directory '{sourcePath}' not found.");
            }

            if (CanMoveIntoNormalizedRoot(sourcePath, destinationPath, pipeline))
            {
                MoveOrMergeDirectory(sourcePath, destinationPath);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 {role} content moved into normalized staging without copy.");
                return;
            }

            DirectoryCopy(sourcePath, destinationPath, overwrite: true);
            pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"PS4 {role} content copied into normalized staging.");
        }

        private static bool CanMoveIntoNormalizedRoot(string sourcePath, string destinationPath, Ps4InstallPipelineContext pipeline)
        {
            if (pipeline.SourceForm != Ps4ContentFormat.Folder)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(destinationPath)
                || string.IsNullOrWhiteSpace(pipeline.SourcePath)
                || !Directory.Exists(sourcePath))
            {
                return false;
            }

            if (!GameInstallPathHelper.IsPathUnderDirectory(sourcePath, pipeline.SourcePath))
            {
                return false;
            }

            if (GameInstallPathHelper.IsPathUnderDirectory(destinationPath, sourcePath))
            {
                return false;
            }

            return string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath), StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanupSourceWrapperDirectories(Ps4InstallPipelineContext pipeline)
        {
            if (pipeline.SourceForm != Ps4ContentFormat.Folder
                || string.IsNullOrWhiteSpace(pipeline.SourcePath)
                || !Directory.Exists(pipeline.SourcePath))
            {
                return;
            }

            foreach (var wrapperName in new[] { "Bonus", "BONUS", "DLC", "Update", "UPDATE" })
            {
                var wrapperPath = Path.Combine(pipeline.SourcePath, wrapperName);
                if (!Directory.Exists(wrapperPath))
                {
                    continue;
                }

                if (Directory.EnumerateFileSystemEntries(wrapperPath).Any())
                {
                    continue;
                }

                Directory.Delete(wrapperPath, recursive: false);
                pipeline.Context.Logger?.Write(PlatformLogLevel.Info, $"Removed empty PS4 wrapper directory after normalization: '{wrapperPath}'.");
            }
        }

        private static void ExtractPkgToDirectory(string pkgPath, string targetDirectory, string extractorPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(extractorPath) || !File.Exists(extractorPath))
            {
                throw new InvalidOperationException("External PS4 PKG extractor path is missing.");
            }

            Directory.CreateDirectory(targetDirectory);
            logger?.Write(PlatformLogLevel.Info, $"Invoking PS4 PKG extractor. Extractor='{extractorPath}', Source='{pkgPath}', Target='{targetDirectory}'.");
            var psi = new ProcessStartInfo
            {
                FileName = extractorPath,
                Arguments = $"\"{pkgPath}\" \"{targetDirectory}\"",
                WorkingDirectory = Path.GetDirectoryName(extractorPath) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start PS4 PKG extractor process.");
            }

            process.StandardInput.Close();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            System.Threading.Tasks.Task.WaitAll(standardOutputTask, standardErrorTask);

            var standardOutput = standardOutputTask.Result;
            var standardError = standardErrorTask.Result;

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                logger?.Write(PlatformLogLevel.Info, $"PS4 PKG extractor output: {TrimLoggedOutput(standardOutput)}");
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                logger?.Write(PlatformLogLevel.Warning, $"PS4 PKG extractor error output: {TrimLoggedOutput(standardError)}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"PS4 PKG extractor failed with exit code {process.ExitCode}.");
            }

            if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                throw new InvalidOperationException("PS4 PKG extractor completed but produced no output.");
            }
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "content";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "content" : cleaned;
        }

        private static string TrimLoggedOutput(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            const int maxLength = 4000;
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed.Substring(0, maxLength) + " ...[truncated]";
        }

        private static void MoveOrMergeDirectory(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            Directory.CreateDirectory(destinationDirectory);
            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                MoveOrMergeDirectory(directory, Path.Combine(destinationDirectory, name));
            }

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Move(file, destinationFile, overwrite: true);
            }

            if (!Directory.EnumerateFileSystemEntries(sourceDirectory).Any())
            {
                Directory.Delete(sourceDirectory, recursive: false);
            }
        }

        private static void RemoveEmptyDirectories(string rootPath, IPlatformLogger? logger)
        {
            foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, recursive: false);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Write(PlatformLogLevel.Debug, $"Skipping empty-directory cleanup for '{directory}': {ex.Message}");
                }
            }
        }

        private static void TryDeleteDirectoryIfEmpty(string? directoryPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath, recursive: false);
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Debug, $"Skipping PS4 empty-directory cleanup for '{directoryPath}': {ex.Message}");
            }
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }

        private static void DirectoryCopy(string sourceDir, string destinationDir, bool overwrite, IEnumerable<string>? excludedRoots = null)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"Source directory '{sourceDir}' not found.");
            }

            Directory.CreateDirectory(destinationDir);

            var normalizedExcludedRoots = (excludedRoots ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var normalizedFilePath = Path.GetFullPath(filePath);
                if (normalizedExcludedRoots.Any(excluded => normalizedFilePath.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(sourceDir, filePath);
                var target = Path.Combine(destinationDir, relative);
                var targetDir = Path.GetDirectoryName(target) ?? destinationDir;
                Directory.CreateDirectory(targetDir);
                File.Copy(filePath, target, overwrite);
            }
        }

        private sealed class Ps4InstallPipelineContext
        {
            public Ps4InstallPipelineContext(InstallContext context, string platformInstallRoot, string gameInstallRoot, string stageRoot, string extractorPath, bool extractorConfigured)
            {
                Context = context;
                InstallRoot = platformInstallRoot;
                GameInstallRoot = gameInstallRoot;
                StageRoot = stageRoot;
                ExtractorPath = extractorPath;
                ExtractorConfigured = extractorConfigured;
                Directory.CreateDirectory(StageRoot);
            }

            public InstallContext Context { get; }
            public string InstallRoot { get; }
            public string GameInstallRoot { get; }
            public string StageRoot { get; }
            public string ExtractorPath { get; }
            public bool ExtractorConfigured { get; }
            public Ps4InstallStage CurrentStage { get; set; }
            public string SourcePath { get; set; } = string.Empty;
            public Ps4ContentFormat SourceForm { get; set; }
            public string ActiveStagingRoot { get; set; } = string.Empty;
            public List<Ps4ContentCandidate> DiscoveredItems { get; } = new();
            public List<Ps4ContentCandidate> FolderItems { get; set; } = new();
            public IReadOnlyList<Ps4InstallPlanStep> InstallPlan { get; set; } = Array.Empty<Ps4InstallPlanStep>();
            public string BaseTitleId { get; set; } = string.Empty;
            public string NormalizedRoot { get; set; } = string.Empty;
            public string NormalizedBaseRoot { get; set; } = string.Empty;
            public string FinalBaseRoot { get; set; } = string.Empty;
            public string FinalPatchRoot { get; set; } = string.Empty;
            public string FinalDlcRoot { get; set; } = string.Empty;
            public string FinalBonusRoot { get; set; } = string.Empty;
            public string ResolvedPlayablePath { get; set; } = string.Empty;
        }

        private enum Ps4InstallStage
        {
            None = 0,
            AcquireSource,
            ClassifySource,
            ExpandArchiveToStaging,
            ClassifyStagedContent,
            TransformPkgToFolder,
            NormalizeContentLayout,
            InstallToFinalLocation,
            ResolvePlayableBinary,
            FinalizeInstall,
            Cleanup
        }

        private sealed record Ps4InstallPlanStep(Ps4ContentRole Role, Ps4ContentCandidate Candidate);

        private static readonly Ps4ContentRole[] InstallRoleOrder =
        {
            Ps4ContentRole.BaseGame,
            Ps4ContentRole.Update,
            Ps4ContentRole.Dlc,
            Ps4ContentRole.Bonus
        };
    }
}

