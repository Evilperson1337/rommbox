using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;

namespace RomMbox.UI.ViewModels
{
    /// <summary>
    /// View model that drives the executable selection dialog.
    /// </summary>
    public sealed class ExecutableSelectionViewModel : ObservableObject
    {
        private ExecutableCandidateRow _selectedCandidate;

        /// <summary>
        /// Initializes the view model.
        /// </summary>
        public ExecutableSelectionViewModel(string title, string message, IEnumerable<ExecutableCandidateRow> candidates, ExecutableCandidateRow recommended)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Executable Selection" : title;
            Message = message ?? string.Empty;
            Candidates = new ObservableCollection<ExecutableCandidateRow>((candidates ?? Enumerable.Empty<ExecutableCandidateRow>()).ToList());
            _selectedCandidate = recommended ?? Candidates.FirstOrDefault();

            ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true), () => SelectedCandidate != null);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            ConfirmCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Gets the window title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the message shown to the user.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the candidate rows displayed for selection.
        /// </summary>
        public ObservableCollection<ExecutableCandidateRow> Candidates { get; }

        /// <summary>
        /// Gets or sets the selected candidate.
        /// </summary>
        public ExecutableCandidateRow SelectedCandidate
        {
            get => _selectedCandidate;
            set
            {
                if (SetProperty(ref _selectedCandidate, value))
                {
                    ConfirmCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Command that confirms the selection.
        /// </summary>
        public RelayCommand ConfirmCommand { get; }

        /// <summary>
        /// Command that cancels the dialog.
        /// </summary>
        public RelayCommand CancelCommand { get; }

        /// <summary>
        /// Event raised when the dialog should close; parameter indicates confirm or cancel.
        /// </summary>
        public event System.Action<bool> RequestClose;
    }
}
