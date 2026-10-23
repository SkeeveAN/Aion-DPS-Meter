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
