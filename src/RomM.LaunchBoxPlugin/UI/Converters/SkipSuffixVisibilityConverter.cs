using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RomMbox.UI.Converters;

/// <summary>
/// Shows or hides a UI element based on whether a suffix string is present.
/// </summary>
/// <remarks>
/// Intended for optional suffix fields; empty strings collapse the element.
/// </remarks>
public sealed class SkipSuffixVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when the string is non-empty; otherwise collapses it.
    /// </summary>
    /// <param name="value">The string value to test.</param>
    /// <param name="targetType">The target binding type (unused).</param>
    /// <param name="parameter">Optional converter parameter (unused).</param>
    /// <param name="culture">The culture to use in the conversion.</param>
    /// <returns><see cref="Visibility.Visible"/> for non-empty text; otherwise <see cref="Visibility.Collapsed"/>.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Conversion back is not supported for this UI-only visibility converter.
    /// </summary>
    /// <param name="value">The value produced by the binding target (unused).</param>
    /// <param name="targetType">The target binding type (unused).</param>
    /// <param name="parameter">Optional converter parameter (unused).</param>
    /// <param name="culture">The culture to use in the conversion.</param>
    /// <returns>Always throws <see cref="NotSupportedException"/>.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
