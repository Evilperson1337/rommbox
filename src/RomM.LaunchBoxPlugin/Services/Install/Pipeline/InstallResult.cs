namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public InstallPhase Phase { get; private set; }

        public static InstallResult Successful(string message = "Install completed.")
        {
            return new InstallResult { Success = true, Message = message, Phase = InstallPhase.Completed };
        }

        public static InstallResult Failed(InstallPhase phase, string message)
        {
            return new InstallResult { Success = false, Message = message ?? "Install failed.", Phase = phase };
        }

        public static InstallResult Cancelled(string message = "Install cancelled.")
        {
            return new InstallResult { Success = false, Message = message, Phase = InstallPhase.Cancelled };
        }
    }
}
