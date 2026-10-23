using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>Test mode <c>render-privacy &lt;png&gt; [dialog|wizard]</c>: draws the privacy question or the first-start wizard to a picture, never showing a window.</summary>
public static class PrivacyRender
{
    public static void Run(string path, string kind)
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        MeterSettings settings = MeterSettings.Load();
        string? forced = Environment.GetEnvironmentVariable("AIONDPS_LANG");
        if (!string.IsNullOrEmpty(forced))
        {
            LocalizationManager.Instance.Language = forced;
        }
        else if (settings.Language.Length > 0)
        {
            LocalizationManager.Instance.Language = settings.Language;
        }

        ThemeManager.Apply(app, settings);
        MainWindow.Headless = true;
        // the button and radio styles the running meter takes from its main window come from the settings window here (the same look)
        if (kind == "replayicon")
        {
            // the replay upload button's icon, enlarged: a play triangle in an almost closed circle with an arrowhead (same paths as MainWindow.xaml)
            var canvas = (System.Windows.Controls.Canvas)System.Windows.Markup.XamlReader.Parse(
                "<Canvas xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20'>" +
                "<Path Data='M10,3 A7,7 0 1 0 15.6,5.6 M10,0.6 L10,5.4 L14.2,3 Z' Stroke='#f2f5f7' StrokeThickness='1.6' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>" +
                "<Path Data='M8,7 L13.2,10 L8,13 Z' Fill='#f2f5f7'/></Canvas>");
            var host = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x18, 0x20)), Width = 40, Height = 40, Child = new System.Windows.Controls.Viewbox { Child = canvas, Margin = new Thickness(6) } };
            host.Measure(new Size(40, 40));
            host.Arrange(new Rect(0, 0, 40, 40));
            host.UpdateLayout();
            var small = new RenderTargetBitmap(160, 160, 384, 384, System.Windows.Media.PixelFormats.Pbgra32);
            small.Render(host);
            var iconEncoder = new PngBitmapEncoder();
            iconEncoder.Frames.Add(BitmapFrame.Create(small));
            using (FileStream file = File.Create(path))
            {
                iconEncoder.Save(file);
            }

            Console.WriteLine("render-privacy: wrote " + path);
            Environment.Exit(0);
        }

        var styled = new SettingsWindow(settings);
        Window window = kind == "wizard" ? new WizardWindow(new MeterSettings()) : new PrivacyWindow(null);
        foreach (Type type in new[] { typeof(System.Windows.Controls.Button), typeof(System.Windows.Controls.ComboBox), typeof(System.Windows.Controls.ComboBoxItem) })
        {
            if (styled.Resources[type] is Style style)
            {
                window.Resources[type] = style;
            }
        }

        var content = (UIElement)window.Content;
        double width = kind == "wizard" ? 500 : 660;
        content.Measure(new Size(width, double.PositiveInfinity));
        int w = (int)Math.Ceiling(width), h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Measure(new Size(w, h));
        content.Arrange(new Rect(0, 0, w, h));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine("render-privacy: wrote " + path);
        Environment.Exit(0);
    }
}
