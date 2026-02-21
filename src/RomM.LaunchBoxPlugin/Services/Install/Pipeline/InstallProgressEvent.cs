using System;

namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallProgressEvent
    {
        public InstallProgressEvent(InstallPhase phase, string message, double? percent = null)
        {
            Phase = phase;
            Message = message ?? string.Empty;
            Percent = percent;
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public InstallPhase Phase { get; }
        public string Message { get; }
        public double? Percent { get; }
        public DateTimeOffset TimestampUtc { get; }
    }
}
