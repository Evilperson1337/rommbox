#nullable enable
using System;
using System.Windows;
using System.Windows.Input;

namespace RomMbox.UI.Infrastructure;

/// <summary>
/// Basic <see cref="ICommand"/> implementation for parameterless actions.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Creates a new relay command.
    /// </summary>
    /// <param name="execute">The action to execute.</param>
    /// <param name="canExecute">Optional predicate to determine if the command can run.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Event raised when the command's ability to execute changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Determines whether the command can execute.
    /// </summary>
    /// <param name="parameter">Unused command parameter.</param>
    /// <returns><c>true</c> if the command can execute.</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <summary>
    /// Executes the command action.
    /// </summary>
    /// <param name="parameter">Unused command parameter.</param>
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Raises <see cref="CanExecuteChanged"/> on the UI thread when needed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Generic <see cref="ICommand"/> implementation for actions with a parameter.
/// </summary>
/// <typeparam name="T">The expected parameter type.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    /// <summary>
    /// Creates a new relay command that accepts a parameter.
    /// </summary>
    /// <param name="execute">The action to execute.</param>
    /// <param name="canExecute">Optional predicate to determine if the command can run.</param>
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Event raised when the command's ability to execute changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Determines whether the command can execute for the given parameter.
    /// </summary>
    /// <param name="parameter">The command parameter.</param>
    /// <returns><c>true</c> if the command can execute.</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    /// <summary>
    /// Executes the command action using the provided parameter.
    /// </summary>
    /// <param name="parameter">The command parameter.</param>
    public void Execute(object? parameter) => _execute((T?)parameter);

    /// <summary>
    /// Raises <see cref="CanExecuteChanged"/> on the UI thread when needed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
