using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RomMbox.UI.Converters;

/// <summary>
/// Converts <see cref="bool"/> values into <see cref="Visibility"/> with inverted semantics.
/// </summary>
/// <remarks>
/// A value of <c>true</c> collapses the element; <c>false</c> shows it.
/// </remarks>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Returns <see cref="Visibility.Collapsed"/> when the value is <c>true</c>, otherwise <see cref="Visibility.Visible"/>.
    /// </summary>
    /// <param name="value">The source value expected to be a boolean.</param>
    /// <param name="targetType">The target binding type (unused).</param>
    /// <param name="parameter">Optional converter parameter (unused).</param>
    /// <param name="culture">The culture to use in the conversion.</param>
    /// <returns>The inverted visibility value.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Conversion back is not supported; prevents updating the binding source.
    /// </summary>
    /// <param name="value">The value produced by the binding target (unused).</param>
    /// <param name="targetType">The target binding type (unused).</param>
    /// <param name="parameter">Optional converter parameter (unused).</param>
    /// <param name="culture">The culture to use in the conversion.</param>
    /// <returns><see cref="Binding.DoNothing"/> to stop source updates.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
