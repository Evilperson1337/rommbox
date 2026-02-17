using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RomMbox.UI.Behaviors;

/// <summary>
/// Redirects mouse wheel events from a closed <see cref="ComboBox"/> to its parent scroll container.
/// </summary>
/// <remarks>
/// This prevents accidental selection changes while still allowing users to scroll the surrounding UI.
/// </remarks>
public static class ComboBoxMouseWheelBehavior
{
    /// <summary>
    /// Attached property that enables mouse wheel redirection behavior.
    /// </summary>
    public static readonly DependencyProperty EnableRedirectProperty = DependencyProperty.RegisterAttached(
        "EnableRedirect",
        typeof(bool),
        typeof(ComboBoxMouseWheelBehavior),
        new PropertyMetadata(false, OnEnableRedirectChanged));

    /// <summary>
    /// Gets whether redirection is enabled for the provided dependency object.
    /// </summary>
    /// <param name="obj">The object hosting the attached property.</param>
    /// <returns><c>true</c> when redirection is enabled.</returns>
    public static bool GetEnableRedirect(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableRedirectProperty);
    }

    /// <summary>
    /// Enables or disables mouse wheel redirection on the provided dependency object.
    /// </summary>
    /// <param name="obj">The object hosting the attached property.</param>
    /// <param name="value">Whether to enable redirection.</param>
    public static void SetEnableRedirect(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableRedirectProperty, value);
    }

    /// <summary>
    /// Subscribes or unsubscribes to mouse wheel events when the attached property changes.
    /// </summary>
    /// <param name="d">The dependency object.</param>
    /// <param name="e">Property change arguments.</param>
    private static void OnEnableRedirectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            if ((bool)e.NewValue)
            {
                comboBox.PreviewMouseWheel += OnPreviewMouseWheel;
                comboBox.MouseWheel += OnMouseWheel;
            }
            else
            {
                comboBox.PreviewMouseWheel -= OnPreviewMouseWheel;
                comboBox.MouseWheel -= OnMouseWheel;
            }
        }
    }

    /// <summary>
    /// Suppresses the mouse wheel when the combo box is closed to avoid changing selection.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Mouse wheel event arguments.</param>
    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.IsDropDownOpen)
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Forwards the mouse wheel event to a parent scroll container when the combo box is closed.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Mouse wheel event arguments.</param>
    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.IsDropDownOpen)
        {
            return;
        }

        var target = FindAncestor<ScrollViewer>(comboBox) as UIElement
            ?? FindAncestor<DataGrid>(comboBox) as UIElement
            ?? FindAncestor<ItemsControl>(comboBox) as UIElement;

        if (target == null)
        {
            return;
        }

        e.Handled = true;
        var routedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        target.RaiseEvent(routedEvent);
    }

    /// <summary>
    /// Walks the visual tree upwards to find the nearest ancestor of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The ancestor type to locate.</typeparam>
    /// <param name="start">The starting dependency object.</param>
    /// <returns>The first ancestor of type <typeparamref name="T"/>, or <c>null</c>.</returns>
    private static DependencyObject FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current != null)
        {
            if (current is T)
            {
                return current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
