using System;
using System.Globalization;
using System.Windows.Data;

namespace RomMbox.UI.Converters
{
    /// <summary>
    /// Converts progress values into a width for a progress overlay or bar.
    /// </summary>
    /// <remarks>
    /// Expects three bindings: the container width, the current value, and the maximum value.
    /// </remarks>
    public class ProgressWidthConverter : IMultiValueConverter
    {
        /// <summary>
        /// Computes the proportional width based on the current value and maximum.
        /// </summary>
        /// <param name="values">Array containing [actualWidth, value, maximum].</param>
        /// <param name="targetType">The target binding type (unused).</param>
        /// <param name="parameter">Optional converter parameter (unused).</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns>The calculated width or <c>0</c> if inputs are invalid.</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
                return 0d;

            if (values[0] is double actualWidth &&
                values[1] is double value &&
                values[2] is double maximum &&
                maximum > 0)
            {
                return actualWidth * (value / maximum);
            }

            return 0d;
        }

        /// <summary>
        /// Conversion back is not supported for multi-binding values.
        /// </summary>
        /// <param name="value">The value produced by the binding target (unused).</param>
        /// <param name="targetTypes">The array of target types (unused).</param>
        /// <param name="parameter">Optional converter parameter (unused).</param>
        /// <param name="culture">The culture to use in the conversion.</param>
        /// <returns>Always throws <see cref="NotSupportedException"/>.</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
