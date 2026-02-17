using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// Lightweight view model used for test or mock UI bindings.
/// </summary>
public sealed class TestViewModel : ObservableObject
{
    /// <summary>
    /// Initializes mock commands for testing the UI.
    /// </summary>
    public TestViewModel()
    {
        TestConnectionCommand = new RelayCommand(() => { /* mock */ });
        TestRomPathCommand = new RelayCommand(() => { /* mock */ });
        TestSavesPathCommand = new RelayCommand(() => { /* mock */ });
        RomMSmokeTestCommand = new RelayCommand(() => { /* mock */ });
    }

    /// <summary>
    /// Mock command for testing connection flows.
    /// </summary>
    public RelayCommand TestConnectionCommand { get; }
    /// <summary>
    /// Mock command for testing ROM path flows.
    /// </summary>
    public RelayCommand TestRomPathCommand { get; }
    /// <summary>
    /// Mock command for testing saves path flows.
    /// </summary>
    public RelayCommand TestSavesPathCommand { get; }
    /// <summary>
    /// Mock command for running a RomM smoke test.
    /// </summary>
    public RelayCommand RomMSmokeTestCommand { get; }
}
