using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RomMbox.UI.Converters;

/// <summary>
/// Converts a boolean into a WPF <see cref="Visibility"/> value.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Returns Visible when true; otherwise Collapsed.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Not supported for two-way binding; returns <see cref="Binding.DoNothing"/>.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
