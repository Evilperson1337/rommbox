#nullable enable
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RomMbox.UI.Infrastructure;

/// <summary>
/// Base class that implements <see cref="INotifyPropertyChanged"/> for view models.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>
    /// Raised when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets the backing field and raises <see cref="PropertyChanged"/> when the value changes.
    /// </summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property name (auto-filled by compiler).</param>
    /// <returns><c>true</c> if the value changed; otherwise <c>false</c>.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// Raises a property changed notification for the given property name.
    /// </summary>
    /// <param name="propertyName">The property name (auto-filled by compiler).</param>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
