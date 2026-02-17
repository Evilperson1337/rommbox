using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Data;

namespace RomMbox.UI.Converters
{
    /// <summary>
    /// Converts an enum value to a friendly string by using <see cref="DescriptionAttribute"/> if present.
    /// </summary>
    /// <remarks>
    /// Falls back to the enum name (or <c>ToString()</c>) when no description is available.
    /// </remarks>
    public sealed class EnumDescriptionConverter : IValueConverter
    {
        /// <summary>
        /// Converts an enum value into its description for display in the UI.
        /// </summary>
        /// <param name="value">The enum value to convert.</param>
        /// <param name="targetType">The target binding type (unused).</param>
        /// <param name="parameter">Optional converter parameter (unused).</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns>The description string, enum name, or an empty string when <paramref name="value"/> is null.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var type = value.GetType();
            if (!type.IsEnum)
            {
                return value.ToString();
            }

            var name = Enum.GetName(type, value);
            if (string.IsNullOrWhiteSpace(name))
            {
                return value.ToString();
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            var description = field?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                .OfType<DescriptionAttribute>()
                .FirstOrDefault();
            return description?.Description ?? name;
        }

        /// <summary>
        /// Conversion back is not supported; keeps the binding source unchanged.
        /// </summary>
        /// <param name="value">The value produced by the binding target (unused).</param>
        /// <param name="targetType">The target binding type (unused).</param>
        /// <param name="parameter">Optional converter parameter (unused).</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns><see cref="Binding.DoNothing"/> to prevent updates to the source.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
