using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>
/// The meter's own window frame for secondary windows (details, character): no OS title bar, the
/// same dark title strip with the app icon, title and minimize/close as MainWindow and
/// SettingsWindow, inside a bordered surface. Applied in code so windows built in code and windows
/// built in XAML get the identical frame. Call before the window is first shown
/// (AllowsTransparency cannot change afterwards).
/// </summary>
public static class ThemedChrome
{
    public static void Apply(Window window)
    {
        UIElement? content = window.Content as UIElement;
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.ResizeMode = ResizeMode.CanResizeWithGrip;
        window.Background = Brushes.Transparent;

        // The dark scroll bar lives in MainWindow's resources; without it a secondary window
        // shows the system's light one.
        if (Application.Current.MainWindow?.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] is Style scrollBar)
        {
            window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollBar;
        }

        var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        try
        {
            using var appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "app", "aiondps.ico"));
            icon.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // No icon file: the title strip just starts with the text.
        }

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 0, 0) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Window.Title)) { Source = window });

        Button Caption(string glyph, Action action)
        {
            var button = new Button { Content = glyph, Width = 28, Height = 30, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0) };
            button.SetResourceReference(Control.ForegroundProperty, "Brush.TextMuted");
            button.Click += (_, _) => action();
            return button;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Caption("─", () => window.WindowState = WindowState.Minimized));
        buttons.Children.Add(Caption("✕", window.Close));

        var bar = new Grid { Height = 30 };
        bar.SetResourceReference(Panel.BackgroundProperty, "Brush.TitleBar");
        bar.Children.Add(icon);
        bar.Children.Add(title);
        bar.Children.Add(buttons);
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                window.DragMove();
            }
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        if (content is not null)
        {
            dock.Children.Add(content);
        }

        var frame = new Border { BorderThickness = new Thickness(1), Child = dock };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        window.Content = frame;
    }
}
