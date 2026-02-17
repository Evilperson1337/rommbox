using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace RomMbox.UI.Infrastructure
{
    /// <summary>
    /// Applies custom chrome and title bar styling to WPF windows.
    /// </summary>
    public static class WindowChromeService
    {
        /// <summary>
        /// Rebuilds the window content to include a custom title bar and chrome settings.
        /// </summary>
        /// <param name="window">The window to decorate.</param>
        /// <param name="title">The title text displayed in the custom bar.</param>
        public static void Apply(Window window, string title)
        {
            if (window == null)
            {
                return;
            }

            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = window.ResizeMode == ResizeMode.NoResize ? ResizeMode.NoResize : ResizeMode.CanResize;
            window.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x22));
            window.AllowsTransparency = false;

            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);

            var root = window.Content as UIElement;
            window.Content = null;

            var grid = new Grid
            {
                Background = window.Background
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = BuildTitleBar(window, title);
            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);

            if (root != null)
            {
                Grid.SetRow(root, 1);
                grid.Children.Add(root);
            }

            window.Content = grid;
        }

        /// <summary>
        /// Builds the title bar with window controls and drag behavior.
        /// </summary>
        /// <param name="window">The owning window.</param>
        /// <param name="title">The title text.</param>
        /// <returns>A UI element representing the title bar.</returns>
        private static UIElement BuildTitleBar(Window window, string title)
        {
            var bar = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x30)),
                Height = 32
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

            var titleBlock = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF5)),
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 12
            };
            bar.Children.Add(titleBlock);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(buttons, 1);

            buttons.Children.Add(BuildTitleButton("—", () => window.WindowState = WindowState.Minimized));
            buttons.Children.Add(BuildTitleButton("□", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            buttons.Children.Add(BuildTitleButton("✕", () => window.Close(), isClose: true));

            bar.Children.Add(buttons);

            bar.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount == 2)
                {
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
                else
                {
                    window.DragMove();
                }
            };

            return bar;
        }

        /// <summary>
        /// Creates a title bar button for minimize, maximize, or close actions.
        /// </summary>
        /// <param name="glyph">The glyph to display.</param>
        /// <param name="onClick">Action invoked when the button is clicked.</param>
        /// <param name="isClose">Whether the button represents the close action.</param>
        /// <returns>A configured button.</returns>
        private static Button BuildTitleButton(string glyph, Action onClick, bool isClose = false)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 32,
                Height = 32,
                FontSize = 12,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand
            };

            if (isClose)
            {
                button.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x90, 0x90));
            }

            button.Click += (_, __) => onClick();
            return button;
        }
    }
}
