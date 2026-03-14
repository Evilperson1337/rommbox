using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PS3.Packages
{
    /// <summary>
    /// Wraps RPCS3 package installation commands for PKG batches and single-package installs.
    /// </summary>
    public sealed class Rpcs3PackageInstaller
    {
        private readonly IPlatformLogger? _logger;

        public Rpcs3PackageInstaller(IPlatformLogger? logger)
        {
            _logger = logger;
        }

        public async Task<PackageInstallResult> InstallPackageAsync(string rpcs3ExecutablePath, string packagePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rpcs3ExecutablePath) || !File.Exists(rpcs3ExecutablePath))
            {
                return new PackageInstallResult
                {
                    Success = false,
                    Message = "RPCS3 executable path is missing or invalid."
                };
            }

            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                _logger?.Write(PlatformLogLevel.Warning, $"Package path invalid: '{packagePath}'. Exists={File.Exists(packagePath)}.");
                return new PackageInstallResult
                {
                    Success = false,
                    Message = "Package path is missing or invalid."
                };
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = rpcs3ExecutablePath,
                Arguments = $"--installpkg \"{packagePath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(rpcs3ExecutablePath) ?? string.Empty
            };

            _logger?.Write(PlatformLogLevel.Info, $"RPCS3 installpkg: '{startInfo.FileName}' {startInfo.Arguments}");
            _logger?.Write(PlatformLogLevel.Debug, $"RPCS3 working directory: '{startInfo.WorkingDirectory}'.");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                var stdOut = await stdOutTask.ConfigureAwait(false);
                var stdErr = await stdErrTask.ConfigureAwait(false);
                stopwatch.Stop();

                return new PackageInstallResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    StdOut = stdOut ?? string.Empty,
                    StdErr = stdErr ?? string.Empty,
                    Message = process.ExitCode == 0 ? "Package install completed." : "Package install failed.",
                    Duration = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new PackageInstallResult
                {
                    Success = false,
                    ExitCode = -1,
                    Message = ex.Message,
                    Duration = stopwatch.Elapsed
                };
            }
        }

        public Task<PackageInstallResult> InstallPackagesFromDirectoryAsync(string rpcs3ExecutablePath, string packageDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
            {
                return Task.FromResult(new PackageInstallResult
                {
                    Success = false,
                    Message = "Package directory is missing or invalid."
                });
            }

            return InstallPackageDirectoryAsync(rpcs3ExecutablePath, packageDirectory, cancellationToken);
        }

        private async Task<PackageInstallResult> InstallPackageDirectoryAsync(string rpcs3ExecutablePath, string packageDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rpcs3ExecutablePath) || !File.Exists(rpcs3ExecutablePath))
            {
                return new PackageInstallResult
                {
                    Success = false,
                    Message = "RPCS3 executable path is missing or invalid."
                };
            }

            if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
            {
                _logger?.Write(PlatformLogLevel.Warning, $"Package directory invalid: '{packageDirectory}'. Exists={Directory.Exists(packageDirectory)}.");
                return new PackageInstallResult
                {
                    Success = false,
                    Message = "Package directory is missing or invalid."
                };
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = rpcs3ExecutablePath,
                Arguments = $"--installpkg \"{packageDirectory}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(rpcs3ExecutablePath) ?? string.Empty
            };

            _logger?.Write(PlatformLogLevel.Info, $"RPCS3 installpkg: '{startInfo.FileName}' {startInfo.Arguments}");
            _logger?.Write(PlatformLogLevel.Debug, $"RPCS3 working directory: '{startInfo.WorkingDirectory}'.");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                var stdOut = await stdOutTask.ConfigureAwait(false);
                var stdErr = await stdErrTask.ConfigureAwait(false);
                stopwatch.Stop();

                return new PackageInstallResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    StdOut = stdOut ?? string.Empty,
                    StdErr = stdErr ?? string.Empty,
                    Message = process.ExitCode == 0 ? "Package install completed." : "Package install failed.",
                    Duration = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new PackageInstallResult
                {
                    Success = false,
                    ExitCode = -1,
                    Message = ex.Message,
                    Duration = stopwatch.Elapsed
                };
            }
        }
    }
}
