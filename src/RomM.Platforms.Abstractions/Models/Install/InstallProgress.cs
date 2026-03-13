namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class InstallProgress
    {
        public InstallProgress(string stage, string message, double? percent = null, bool isIndeterminate = false)
        {
            Stage = stage;
            Message = message;
            Percent = percent;
            IsIndeterminate = isIndeterminate;
        }

        public string Stage { get; }
        public string Message { get; }
        public double? Percent { get; }
        public bool IsIndeterminate { get; }
    }
}

