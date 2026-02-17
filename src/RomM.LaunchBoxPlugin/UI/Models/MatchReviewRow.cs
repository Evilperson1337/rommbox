using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Models
{
    /// <summary>
    /// Represents a row in the match review UI with user approval state.
    /// </summary>
    public sealed class MatchReviewRow : ObservableObject
    {
        /// <summary>
        /// Gets the RomM platform identifier.
        /// </summary>
        public string PlatformId { get; init; }
        /// <summary>
        /// Gets the RomM game identifier.
        /// </summary>
        public string RommId { get; init; }
        /// <summary>
        /// Gets the RomM game title.
        /// </summary>
        public string RommTitle { get; init; }
        /// <summary>
        /// Gets the LaunchBox game identifier.
        /// </summary>
        public string LaunchBoxGameId { get; init; }
        /// <summary>
        /// Gets the LaunchBox game title.
        /// </summary>
        public string LaunchBoxTitle { get; init; }
        /// <summary>
        /// Gets the matching strategy applied by the importer.
        /// </summary>
        public string Strategy { get; init; }
        /// <summary>
        /// Gets the confidence label for the match.
        /// </summary>
        public string Confidence { get; init; }

        private bool? _decision;
        /// <summary>
        /// Gets or sets the user's decision for the match (<c>true</c>, <c>false</c>, or <c>null</c>).
        /// </summary>
        public bool? Decision
        {
            get => _decision;
            set => SetProperty(ref _decision, value);
        }
    }
}
