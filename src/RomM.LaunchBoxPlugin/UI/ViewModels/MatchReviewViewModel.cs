using System.Collections.ObjectModel;
using System.Linq;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;

namespace RomMbox.UI.ViewModels
{
    /// <summary>
    /// View model that drives the match review dialog.
    /// </summary>
    public sealed class MatchReviewViewModel : ObservableObject
    {
        /// <summary>
        /// Gets the match rows displayed for user decisions.
        /// </summary>
        public ObservableCollection<MatchReviewRow> Matches { get; } = new ObservableCollection<MatchReviewRow>();

        /// <summary>
        /// Command that applies decisions and closes the dialog.
        /// </summary>
        public RelayCommand ApplyCommand { get; }
        /// <summary>
        /// Command that cancels the dialog without applying changes.
        /// </summary>
        public RelayCommand CancelCommand { get; }

        /// <summary>
        /// Initializes the view model and its commands.
        /// </summary>
        public MatchReviewViewModel()
        {
            ApplyCommand = new RelayCommand(() => RequestClose?.Invoke(true), CanApply);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        /// <summary>
        /// Event raised when the dialog should close; parameter indicates apply or cancel.
        /// </summary>
        public event System.Action<bool> RequestClose;

        /// <summary>
        /// Gets whether any decisions have been made.
        /// </summary>
        public bool HasDecisions => Matches.Any(row => row.Decision.HasValue);

        /// <summary>
        /// Determines whether apply is allowed (all rows have a decision).
        /// </summary>
        public bool CanApply() => Matches.Count > 0 && Matches.All(row => row.Decision.HasValue);
    }
}
