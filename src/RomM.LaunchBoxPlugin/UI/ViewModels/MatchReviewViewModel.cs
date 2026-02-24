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
        private bool _requireAcceptance;

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
        /// Gets or sets whether at least one match must be accepted to apply.
        /// </summary>
        public bool RequireAcceptance
        {
            get => _requireAcceptance;
            set => SetProperty(ref _requireAcceptance, value);
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
        public bool CanApply()
        {
            if (Matches.Count == 0)
            {
                return false;
            }

            if (!Matches.All(row => row.Decision.HasValue))
            {
                return false;
            }

            if (RequireAcceptance)
            {
                return Matches.Any(row => row.Decision == true);
            }

            return true;
        }
    }
}
