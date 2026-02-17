using System;
using System.Globalization;
using System.Windows.Data;

namespace RomMbox.UI.Converters
{
    /// <summary>
    /// Converts between an enum value and a boolean by comparing it to a string parameter.
    /// </summary>
    /// <remarks>
    /// Commonly used to bind radio buttons to enum values in XAML.
    /// </remarks>
    public sealed class EnumEqualsToBoolConverter : IValueConverter
    {
        /// <summary>
        /// Returns <c>true</c> when the enum's string value matches the provided parameter.
        /// </summary>
        /// <param name="value">The current enum value.</param>
        /// <param name="targetType">The target binding type (unused).</param>
        /// <param name="parameter">The enum value name as a string.</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns><c>true</c> when <paramref name="value"/> equals the parameter; otherwise <c>false</c>.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return false;
            }

            if (value is Enum enumValue)
            {
                return string.Equals(enumValue.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts a checked state back to the enum value specified by the parameter.
        /// </summary>
        /// <param name="value">The bound boolean value from the UI.</param>
        /// <param name="targetType">The enum type to parse into (nullable supported).</param>
        /// <param name="parameter">The enum value name as a string.</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns>The parsed enum value, or <see cref="Binding.DoNothing"/> when conversion is not possible.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null || value is not bool isChecked || !isChecked)
            {
                return Binding.DoNothing;
            }

            var paramText = parameter.ToString();
            if (string.IsNullOrWhiteSpace(paramText))
            {
                return Binding.DoNothing;
            }

            try
            {
                var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                return Enum.Parse(enumType, paramText);
            }
            catch
            {
                return Binding.DoNothing;
            }
        }
    }
}
