using System.Windows;
using System.Windows.Controls;

namespace RomMbox.UI.Behaviors
{
    /// <summary>
    /// Enables two-way binding for <see cref="PasswordBox"/> password values.
    /// </summary>
    public static class PasswordBoxBindingBehavior
    {
        /// <summary>
        /// Attached property that enables password binding.
        /// </summary>
        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxBindingBehavior),
                new PropertyMetadata(false, OnBindPasswordChanged));

        /// <summary>
        /// Attached property that stores the bound password value.
        /// </summary>
        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.RegisterAttached(
                "Password",
                typeof(string),
                typeof(PasswordBoxBindingBehavior),
                new FrameworkPropertyMetadata(string.Empty, OnPasswordPropertyChanged));

        /// <summary>
        /// Gets the bound password value.
        /// </summary>
        /// <param name="obj">The dependency object.</param>
        /// <returns>The bound password string.</returns>
        public static string GetPassword(DependencyObject obj)
            => (string)obj.GetValue(PasswordProperty);

        /// <summary>
        /// Sets the bound password value.
        /// </summary>
        /// <param name="obj">The dependency object.</param>
        /// <param name="value">The password string to set.</param>
        public static void SetPassword(DependencyObject obj, string value)
            => obj.SetValue(PasswordProperty, value);

        /// <summary>
        /// Gets whether password binding is enabled.
        /// </summary>
        /// <param name="obj">The dependency object.</param>
        /// <returns><c>true</c> when binding is enabled.</returns>
        public static bool GetBindPassword(DependencyObject obj)
            => (bool)obj.GetValue(BindPasswordProperty);

        /// <summary>
        /// Enables or disables password binding.
        /// </summary>
        /// <param name="obj">The dependency object.</param>
        /// <param name="value">Whether to enable binding.</param>
        public static void SetBindPassword(DependencyObject obj, bool value)
            => obj.SetValue(BindPasswordProperty, value);

        /// <summary>
        /// Internal flag to prevent recursive password updates.
        /// </summary>
        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating",
                typeof(bool),
                typeof(PasswordBoxBindingBehavior));

        /// <summary>
        /// Updates the <see cref="PasswordBox"/> when the bound password changes.
        /// </summary>
        /// <param name="d">The dependency object.</param>
        /// <param name="e">Property change arguments.</param>
        private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb)
                return;

            pb.PasswordChanged -= PasswordChanged;

            if (!(bool)pb.GetValue(IsUpdatingProperty))
            {
                pb.Password = e.NewValue as string ?? string.Empty;
            }

            pb.PasswordChanged += PasswordChanged;
        }

        /// <summary>
        /// Hooks or unhooks the password changed event when binding is toggled.
        /// </summary>
        /// <param name="d">The dependency object.</param>
        /// <param name="e">Property change arguments.</param>
        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb)
            {
                return;
            }

            pb.PasswordChanged -= PasswordChanged;
            if (e.NewValue is bool enabled && enabled)
            {
                pb.PasswordChanged += PasswordChanged;
            }
        }

        /// <summary>
        /// Writes the current password back to the attached property.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private static void PasswordChanged(object sender, RoutedEventArgs e)
        {
            var pb = (PasswordBox)sender;
            pb.SetValue(IsUpdatingProperty, true);
            SetPassword(pb, pb.Password);
            pb.SetValue(IsUpdatingProperty, false);
        }
    }
}
