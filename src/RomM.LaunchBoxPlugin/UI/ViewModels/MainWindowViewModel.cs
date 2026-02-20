using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// Shell view-model that hosts the main navigation pages and global UI state.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    // Shell state shared across pages.
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaisePropertyChanged(nameof(ConnectionStatusText));
                RaisePropertyChanged(nameof(ConnectionStatusBrush));
                RaisePropertyChanged(nameof(ConnectionDotBrush));

                // If user is on a locked page and we disconnect, snap back
                if (!value && SelectedNavIndex != 0)
                    SelectedNavIndex = 0;

                UpdateFooter();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isServerConfigured;
    private bool _isAutoRefreshRunning;
    public bool IsServerConfigured
    {
        get => _isServerConfigured;
        set
        {
            if (SetProperty(ref _isServerConfigured, value))
            {
                // If server becomes configured and user is on connection page, allow navigation
                if (value && SelectedNavIndex == 0)
                {
                    UpdateFooter();
                }

                SaveCommand.RaiseCanExecuteChanged();
                if (value)
                {
                    // Skip background auto-refresh; tabs will lazy-load when activated.
                }
            }
        }
    }

    private async Task TriggerAutoRefreshAsync()
    {
        if (_isAutoRefreshRunning)
        {
            return;
        }

        _isAutoRefreshRunning = true;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await Platforms.ReloadMappingsAsync().ConfigureAwait(false);
                await Import.ReloadPlatformsAsync().ConfigureAwait(false);
            });
        }
        finally
        {
            _isAutoRefreshRunning = false;
        }
    }

    private string _selectedPlatform = "NES";
    public string SelectedPlatform { get => _selectedPlatform; set => SetProperty(ref _selectedPlatform, value); }

    // Pages
    public ConnectionViewModel Connection { get; }
    public PlatformsViewModel Platforms { get; }
    public ImportViewModel Import { get; }
    public TestViewModel Test { get; }

    private object _currentPage;
    public object CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    private int _selectedNavIndex;
    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            if (SetProperty(ref _selectedNavIndex, value))
            {
                NavigateByIndex(value);
                UpdateFooter();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConnectionStatusText => IsConnected ? "Connected" : "Not Connected";
    public Brush ConnectionStatusBrush => IsConnected
        ? new SolidColorBrush(Color.FromRgb(70, 220, 140))
        : new SolidColorBrush(Color.FromRgb(255, 86, 86));
    public Brush ConnectionDotBrush => ConnectionStatusBrush;

    // Footer binding
    private string _secondaryButtonText = "Import";
    public string SecondaryButtonText { get => _secondaryButtonText; private set => SetProperty(ref _secondaryButtonText, value); }

    private Visibility _secondaryVisible = Visibility.Collapsed;
    public Visibility SecondaryVisible { get => _secondaryVisible; private set => SetProperty(ref _secondaryVisible, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand SecondaryCommand { get; }

    /// <summary>
    /// Initializes child view-models and default navigation state.
    /// </summary>
    public MainWindowViewModel()
    {
        // init pages
        Connection = new ConnectionViewModel(this);
        Platforms = new PlatformsViewModel(this);
        Import = new ImportViewModel(this);
        Test = new TestViewModel();

        _currentPage = Connection;
        _selectedNavIndex = 0;

        SaveCommand = new RelayCommand(SaveAndClose, CanSaveAndClose);
        SecondaryCommand = new RelayCommand(SecondaryAction);

        UpdateFooter();
    }

    private bool CanSaveAndClose()
    {
        if (SelectedNavIndex == 0)
        {
            return Connection?.CanSaveConnection == true;
        }

        return true;
    }

    /// <summary>
    /// Updates the CurrentPage based on the selected navigation index.
    /// </summary>
    private void NavigateByIndex(int idx)
    {
        // Prevent navigation to other tabs if server is not configured
        if (idx != 0 && !IsServerConfigured)
        {
            return;
        }

        CurrentPage = idx switch
        {
            0 => Connection,
            1 => Platforms,
            2 => Import,
            3 => Test,
            _ => Connection
        };

        _ = idx switch
        {
            1 => Platforms.EnsureLoadedAsync(),
            2 => Import.EnsureLoadedAsync(),
            _ => Task.CompletedTask
        };
    }

    /// <summary>
    /// Saves configuration and triggers import (if on the Import page) before closing.
    /// </summary>
    private async void SaveAndClose()
    {
        if (!CanSaveAndClose())
        {
            return;
        }

        if (SelectedNavIndex == 2)
        {
            var importSucceeded = await Import.RunImportAndWaitAsync();
            if (!importSucceeded)
            {
                return;
            }
        }

        _ = Connection.SaveSilentlyAsync();
        if (SelectedNavIndex == 1)
        {
            _ = Platforms.SaveMappingsSilentlyAsync();
        }
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var pluginWindow = Application.Current?.Windows
                ?.OfType<MainWindow>()
                .FirstOrDefault();
            pluginWindow?.Close();
        });
    }

    /// <summary>
    /// Executes the secondary footer action for the active page.
    /// </summary>
    private void SecondaryAction()
    {
        // Import page uses "Import"
        if (SelectedNavIndex == 2)
        {
            Import.ImportCommand.Execute(null);
        }
    }

    /// <summary>
    /// Updates footer visibility/labels based on current page selection.
    /// </summary>
    private void UpdateFooter()
    {
        if (SelectedNavIndex == 2)
        {
            SecondaryButtonText = "Import";
            SecondaryVisible = Visibility.Visible;
            return;
        }

        SecondaryVisible = Visibility.Collapsed;
    }
}
