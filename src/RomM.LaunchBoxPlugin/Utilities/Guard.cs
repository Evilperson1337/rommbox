using System;

namespace RomMbox.Utilities
{
    /// <summary>
    /// Provides guard clause helpers for argument validation.
    /// </summary>
    internal static class Guard
    {
        /// <summary>
        /// Throws <see cref="ArgumentNullException"/> when the value is null.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <param name="name">The argument name.</param>
        public static void NotNull(object value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }
    }
}
