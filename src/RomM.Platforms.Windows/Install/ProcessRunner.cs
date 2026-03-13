using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Install;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Windows.Install
{
    internal static class ProcessRunner
    {
        public static async Task RunAsync(string fileName, string? arguments, CancellationToken cancellationToken, bool useShellExecute = false)
        {
            await Task.Run(async () =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = useShellExecute,
                    CreateNoWindow = !useShellExecute,
                    RedirectStandardOutput = !useShellExecute,
                    RedirectStandardError = !useShellExecute
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start installer process.");
                }

                if (!useShellExecute)
                {
                    _ = process.StandardOutput.ReadToEnd();
                    _ = process.StandardError.ReadToEnd();
                }

                process.WaitForExit();
            }, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<int> RunElevatedBatchAsync(
            IEnumerable<(string Label, string Path)> installers,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            IPlatformLogger? logger = null,
            string? installerLogPath = null,
            string? platformInstallRoot = null)
        {
            if (installers == null)
            {
                throw new ArgumentNullException(nameof(installers));
            }

            var installerList = installers
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .ToList();
            if (installerList.Count == 0)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var batchRoot = !string.IsNullOrWhiteSpace(platformInstallRoot)
                ? Path.Combine(InstallStagingPathHelper.ResolvePlatformStagingRoot(platformInstallRoot), "installer-batch")
                : Path.Combine(InstallStagingPathHelper.ResolvePlatformStagingRoot(Environment.CurrentDirectory), "installer-batch");
            Directory.CreateDirectory(batchRoot);
            var batchId = Guid.NewGuid().ToString("N");
            var batchLogPath = Path.Combine(batchRoot, $"installer-batch-{batchId}.log");
            var batchScriptPath = Path.Combine(batchRoot, $"installer-batch-{batchId}.ps1");

            var scriptContent = BuildInstallerBatchScript(installerList, arguments, batchLogPath);
            File.WriteAllText(batchScriptPath, scriptContent, System.Text.Encoding.UTF8);

            logger?.Write(PlatformLogLevel.Info, $"Installer batch script path: {batchScriptPath}");
            logger?.Write(PlatformLogLevel.Info, $"Installer batch log path: {batchLogPath}");
            if (arguments != null && arguments.Count > 0)
            {
                logger?.Write(PlatformLogLevel.Info, $"Installer batch argument list: {string.Join(" ", arguments)}");
                foreach (var arg in arguments)
                {
                    logger?.Write(PlatformLogLevel.Debug, $"Installer batch argument: {arg}");
                }
            }
            foreach (var installer in installerList)
            {
                logger?.Write(PlatformLogLevel.Info, $"Installer batch item: {installer.Label} -> {installer.Path}");
            }

            var exitCode = await Task.Run(() =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{batchScriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start elevated installer batch.");
                }

                process.WaitForExit();
                logger?.Write(PlatformLogLevel.Info, $"Installer batch process exit code: {process.ExitCode}.");
                try
                {
                    if (File.Exists(batchLogPath))
                    {
                        var lines = File.ReadAllLines(batchLogPath);
                        foreach (var line in lines)
                        {
                            logger?.Write(PlatformLogLevel.Info, $"Installer batch result: {line}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Failed to read installer batch log '{batchLogPath}'.", ex);
                }
                return process.ExitCode;
            }, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                var detail = MapNtStatusCode(exitCode);
                var logHint = string.IsNullOrWhiteSpace(installerLogPath) ? string.Empty : $" Installer log: {installerLogPath}.";
                logger?.Write(PlatformLogLevel.Warning, $"Elevated installer batch exit code: {exitCode} ({detail}).{logHint}");
            }

            return exitCode;
        }

        internal static string MapNtStatusCode(int exitCode)
        {
            unchecked
            {
                return ((uint)exitCode) switch
                {
                    0xC000041D => "STATUS_FATAL_USER_CALLBACK_EXCEPTION",
                    0xC0000005 => "STATUS_ACCESS_VIOLATION",
                    0xC0000135 => "STATUS_DLL_NOT_FOUND",
                    0xC0000142 => "STATUS_DLL_INIT_FAILED",
                    _ => "UNKNOWN_NTSTATUS"
                };
            }
        }

        private static string BuildInstallerBatchScript(
            IReadOnlyList<(string Label, string Path)> installers,
            IReadOnlyList<string> arguments,
            string batchLogPath)
        {
            var installerEntries = installers
                .Select(installer =>
                {
                    var safePath = EscapePowerShellSingleQuoted(installer.Path);
                    var safeLabel = EscapePowerShellSingleQuoted(installer.Label ?? "Installer");
                    return $"@{{ Label = '{safeLabel}'; Path = '{safePath}' }}";
                });

            var argsList = arguments == null
                ? Array.Empty<string>()
                : arguments.ToArray();
            var safeArgs = argsList.Select(arg => $"'{EscapePowerShellSingleQuoted(arg)}'");
            var argumentsArray = string.Join(", ", safeArgs);

            var safeLogPath = EscapePowerShellSingleQuoted(batchLogPath);
            var installersArray = string.Join(", ", installerEntries);

            var script = $@"$ErrorActionPreference = 'Stop'
$batchExit = 0
$logPath = '{safeLogPath}'
if (Test-Path $logPath) {{ Remove-Item -Path $logPath -Force }}
$arguments = @({argumentsArray})
$installers = @({installersArray})
foreach ($installer in $installers) {{
    if ([string]::IsNullOrWhiteSpace($installer.Path)) {{ continue }}
    Add-Content -Path $logPath -Value (""Installer "" + $installer.Label + "" Starting="" + $installer.Path)
    $p = Start-Process -FilePath $installer.Path -ArgumentList $arguments -Wait -PassThru
    Add-Content -Path $logPath -Value (""Installer "" + $installer.Label + "" Completed ExitCode="" + $p.ExitCode)
    if ($p.ExitCode -ne 0 -and $batchExit -eq 0) {{ $batchExit = $p.ExitCode }}
}}
exit $batchExit
";

            return script;
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }
    }
}
