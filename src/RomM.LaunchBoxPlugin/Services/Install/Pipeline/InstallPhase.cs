namespace RomMbox.Services.Install.Pipeline
{
    internal enum InstallPhase
    {
        Pending,
        ResolvingMetadata,
        ResolvingDestination,
        Downloading,
        Extracting,
        Installing,
        PostProcessing,
        Completed,
        Failed,
        Cancelled
    }
}
