using System;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI
{
    /// <summary>
    /// View model for tracking and displaying install progress in the UI.
    /// </summary>
    internal sealed class InstallProgressViewModel : ObservableObject
    {
        private string _headerText = "Working...";
        /// <summary>
        /// Gets or sets the header text shown at the top of the progress window.
        /// </summary>
        public string HeaderText { get => _headerText; set => SetProperty(ref _headerText, value); }

        private string _statusText = "Working...";
        /// <summary>
        /// Gets or sets the status detail text shown beneath the header.
        /// </summary>
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private double _progressValue;
        /// <summary>
        /// Gets or sets the raw progress value for the progress bar.
        /// </summary>
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                if (SetProperty(ref _progressValue, value))
                {
                    RaisePropertyChanged(nameof(RoundedProgressValue));
                }
            }
        }

        /// <summary>
        /// Gets a rounded progress value for display labels.
        /// </summary>
        public double RoundedProgressValue => Math.Round(ProgressValue);

        private bool _isIndeterminate;
        /// <summary>
        /// Gets or sets whether the progress bar should display an indeterminate state.
        /// </summary>
        public bool IsIndeterminate { get => _isIndeterminate; set => SetProperty(ref _isIndeterminate, value); }
    }
}
