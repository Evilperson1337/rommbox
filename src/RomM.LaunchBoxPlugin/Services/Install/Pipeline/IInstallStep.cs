using System;
using System.Threading;
using System.Threading.Tasks;

namespace RomMbox.Services.Install.Pipeline
{
    internal interface IInstallStep
    {
        InstallPhase Phase { get; }
        Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken);
    }
}
