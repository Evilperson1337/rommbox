namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class ConfirmationRequest
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Detail { get; set; }
    }
}

