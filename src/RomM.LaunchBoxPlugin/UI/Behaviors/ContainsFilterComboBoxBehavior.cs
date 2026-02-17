#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using RomMbox.Services.Logging;

namespace RomMbox.UI.Behaviors
{
    /// <summary>
    /// Enables "contains" filtering for an editable <see cref="ComboBox"/>.
    /// </summary>
    /// <remarks>
    /// Creates a per-combo box <see cref="ListCollectionView"/> so filtering doesn't affect other controls,
    /// filters by text typed into the editable text box (case-insensitive contains), and keeps the dropdown open.
    /// </remarks>
    public static class ContainsFilterComboBoxBehavior
    {
        private static readonly LoggingService Logger = LoggingServiceFactory.Create();
        /// <summary>
        /// Attached property that enables the filtering behavior.
        /// </summary>
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(ContainsFilterComboBoxBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        /// <summary>
        /// Sets whether filtering is enabled for the specified element.
        /// </summary>
        /// <param name="element">The element hosting the attached property.</param>
        /// <param name="value">Whether to enable filtering.</param>
        public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);
        /// <summary>
        /// Gets whether filtering is enabled for the specified element.
        /// </summary>
        /// <param name="element">The element hosting the attached property.</param>
        /// <returns><c>true</c> when filtering is enabled.</returns>
        public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

        // Store per-combobox cloned view + original items
        private static readonly DependencyProperty ViewProperty =
            DependencyProperty.RegisterAttached("View", typeof(ListCollectionView), typeof(ContainsFilterComboBoxBehavior));

        private static void SetView(DependencyObject element, ListCollectionView? value) => element.SetValue(ViewProperty, value);
        private static ListCollectionView? GetView(DependencyObject element) => (ListCollectionView?)element.GetValue(ViewProperty);

        private static readonly DependencyProperty SelectionChangedHandlerProperty =
            DependencyProperty.RegisterAttached("SelectionChangedHandler", typeof(SelectionChangedEventHandler), typeof(ContainsFilterComboBoxBehavior));

        private static void SetSelectionChangedHandler(DependencyObject element, SelectionChangedEventHandler? value) =>
            element.SetValue(SelectionChangedHandlerProperty, value);

        private static SelectionChangedEventHandler? GetSelectionChangedHandler(DependencyObject element) =>
            element.GetValue(SelectionChangedHandlerProperty) as SelectionChangedEventHandler;

        private static readonly DependencyProperty DataContextChangedHandlerProperty =
            DependencyProperty.RegisterAttached("DataContextChangedHandler", typeof(DependencyPropertyChangedEventHandler), typeof(ContainsFilterComboBoxBehavior));

        private static void SetDataContextChangedHandler(DependencyObject element, DependencyPropertyChangedEventHandler? value) =>
            element.SetValue(DataContextChangedHandlerProperty, value);

        private static DependencyPropertyChangedEventHandler? GetDataContextChangedHandler(DependencyObject element) =>
            element.GetValue(DataContextChangedHandlerProperty) as DependencyPropertyChangedEventHandler;

        /// <summary>
        /// Hooks or unhooks the behavior when the attached property changes.
        /// </summary>
        /// <param name="d">The dependency object.</param>
        /// <param name="e">Property change arguments.</param>
        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox cb) return;

            if ((bool)e.NewValue)
            {
                cb.Loaded += ComboLoaded;
                cb.Unloaded += ComboUnloaded;
            }
            else
            {
                cb.Loaded -= ComboLoaded;
                cb.Unloaded -= ComboUnloaded;
            }
        }

        /// <summary>
        /// Cleans up event handlers and filters when the combo box unloads.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private static void ComboUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            Detach(cb);
        }

        /// <summary>
        /// Initializes filtering behavior when the combo box loads.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private static void ComboLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;

            // Ensure template is applied so PART_EditableTextBox exists
            cb.ApplyTemplate();

            // Clone items into an independent list/view
            var items = ExtractItems(cb.ItemsSource);
            if (items == null) return;

            var list = items.ToList(); // keep object references intact
            var view = new ListCollectionView(list);
            SetView(cb, view);

            // Keep whatever DisplayMemberPath/TextSearch path you already use
            cb.ItemsSource = view;
            Logger.Debug($"ContainsFilterComboBoxBehavior attached. Items={list.Count} Selected='{cb.SelectedItem ?? "<null>"}' DataContext='{cb.DataContext ?? "<null>"}' Combo='{cb.Name}'");

            var selectionHandler = new SelectionChangedEventHandler((_, args) =>
            {
                var removed = args.RemovedItems.Count > 0 ? args.RemovedItems[0] : null;
                var added = args.AddedItems.Count > 0 ? args.AddedItems[0] : null;
                Logger.Debug($"ContainsFilterComboBoxBehavior selection changed. Old='{removed ?? "<null>"}' New='{added ?? "<null>"}' DataContext='{cb.DataContext ?? "<null>"}' Combo='{cb.Name}'");
            });
            cb.SelectionChanged += selectionHandler;
            SetSelectionChangedHandler(cb, selectionHandler);

            DependencyPropertyChangedEventHandler dataContextHandler = (_, args) =>
            {
                Logger.Debug($"ContainsFilterComboBoxBehavior DataContextChanged. Old='{args.OldValue ?? "<null>"}' New='{args.NewValue ?? "<null>"}' Combo='{cb.Name}'");
            };
            cb.DataContextChanged += dataContextHandler;
            SetDataContextChangedHandler(cb, dataContextHandler);

            // Attach text changed from editable textbox
            if (cb.Template.FindName("PART_EditableTextBox", cb) is TextBox tb)
            {
                tb.TextChanged += (_, __) => ApplyFilter(cb, tb.Text);
                tb.PreviewMouseLeftButtonDown += (_, __) =>
                {
                    // On click into text box, show dropdown for discoverability
                    if (!cb.IsDropDownOpen) cb.IsDropDownOpen = true;
                };
            }

            // On dropdown open, re-apply filter (helps if selection changes text)
            cb.DropDownOpened += (_, __) =>
            {
                if (cb.Template.FindName("PART_EditableTextBox", cb) is TextBox tb2)
                    ApplyFilter(cb, tb2.Text);
            };

            cb.DropDownClosed += (_, __) =>
            {
                Logger.Debug($"ContainsFilterComboBoxBehavior dropdown closed. Selected='{cb.SelectedItem ?? "<null>"}' Combo='{cb.Name}'");
                ClearFilter(cb);
            };
        }

        /// <summary>
        /// Removes filtering state and associated event handlers.
        /// </summary>
        /// <param name="cb">The combo box to detach.</param>
        private static void Detach(ComboBox cb)
        {
            var view = GetView(cb);
            if (view != null)
            {
                view.Filter = null;
                SetView(cb, null);
            }

            var selectionHandler = GetSelectionChangedHandler(cb);
            if (selectionHandler != null)
            {
                cb.SelectionChanged -= selectionHandler;
                SetSelectionChangedHandler(cb, null);
            }

            var dataContextHandler = GetDataContextChangedHandler(cb);
            if (dataContextHandler != null)
            {
                cb.DataContextChanged -= dataContextHandler;
                SetDataContextChangedHandler(cb, null);
            }

            Logger.Debug($"ContainsFilterComboBoxBehavior detached. Selected='{cb.SelectedItem ?? "<null>"}' DataContext='{cb.DataContext ?? "<null>"}' Combo='{cb.Name}'");
        }

        /// <summary>
        /// Extracts items from the provided items source into a materialized list.
        /// </summary>
        /// <param name="itemsSource">The combo box items source.</param>
        /// <returns>An enumerable of items or <c>null</c> if no items are available.</returns>
        private static IEnumerable<object>? ExtractItems(object? itemsSource)
        {
            if (itemsSource == null) return null;

            if (itemsSource is IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    if (item != null) list.Add(item);
                }
                return list;
            }

            return null;
        }

        /// <summary>
        /// Applies a case-insensitive "contains" filter to the combo box view.
        /// </summary>
        /// <param name="cb">The combo box to filter.</param>
        /// <param name="text">The current text input.</param>
        private static void ApplyFilter(ComboBox cb, string? text)
        {
            var view = GetView(cb);
            if (view == null) return;

            if (!cb.IsDropDownOpen && !cb.IsKeyboardFocusWithin)
            {
                Logger.Debug($"ContainsFilterComboBoxBehavior skip filter (combo not active). Selected='{cb.SelectedItem ?? "<null>"}' Combo='{cb.Name}'");
                ClearFilter(cb);
                return;
            }

            var needle = (text ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(needle))
            {
                Logger.Debug($"ContainsFilterComboBoxBehavior clear filter (empty text). Selected='{cb.SelectedItem ?? "<null>"}' Combo='{cb.Name}'");
                view.Filter = null;
                view.Refresh();
                return;
            }

            view.Filter = o =>
            {
                if (o == null) return false;

                // Prefer ComboBox.TextSearch.TextPath / DisplayMemberPath if you use objects
                // but for strings and simple objects, ToString() is fine.
                var s = o.ToString() ?? string.Empty;
                return s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            };

            view.Refresh();

            Logger.Debug($"ContainsFilterComboBoxBehavior apply filter. Text='{needle}' Items={view.Count} Selected='{cb.SelectedItem ?? "<null>"}' Combo='{cb.Name}'");

            // Keep dropdown open so results update live while typing
            if (!cb.IsDropDownOpen) cb.IsDropDownOpen = true;
        }

        /// <summary>
        /// Clears any active filter on the combo box view.
        /// </summary>
        /// <param name="cb">The combo box to clear.</param>
        private static void ClearFilter(ComboBox cb)
        {
            var view = GetView(cb);
            if (view == null) return;

            if (view.Filter != null)
            {
                view.Filter = null;
                view.Refresh();
                Logger.Debug($"ContainsFilterComboBoxBehavior filter cleared. Selected='{cb.SelectedItem ?? "<null>"}' Combo='{cb.Name}'");
            }
        }
    }
}
