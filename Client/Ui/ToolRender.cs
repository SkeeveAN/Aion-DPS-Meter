using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>
/// Test mode <c>render-tool &lt;png&gt; &lt;tools|dummy|timetable|stream|hotkeys&gt;</c>: draws the tools window (with its list of tools and the checklist), the page of a tool inside that window, or the hotkey help into a picture, never showing a window.
/// Set AIONDPS_DATA_DIR to a throw-away folder; AIONDPS_LANG picks the language, AIONDPS_RENDER_SCALE=2 makes the picture sharp.
/// </summary>
public static class ToolRender
{
    public static void Run(string path, string what)
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Ui/Styles/Shared.xaml", UriKind.Relative)));
        MeterSettings settings = MeterSettings.Load();
        string? forced = Environment.GetEnvironmentVariable("AIONDPS_LANG");
        LocalizationManager.Instance.Language = !string.IsNullOrEmpty(forced) ? forced : settings.Language.Length > 0 ? settings.Language : "en";
        ThemeManager.Apply(app, settings);
        MainWindow.Headless = true;
        int scale = int.TryParse(Environment.GetEnvironmentVariable("AIONDPS_RENDER_SCALE"), out int sc) && sc is >= 1 and <= 4 ? sc : 1;
        // the styles the running meter takes from its main window (buttons, check boxes, combo boxes) are made to come from there, as the craft picture does
        var hostWindow = new MainWindow();
        app.MainWindow = hostWindow;
        foreach (var type in new[] { typeof(Button), typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox) })
        {
            if (hostWindow.Resources[type] is Style style)
            {
                app.Resources[type] = style;
            }
        }

        var loc = LocalizationManager.Instance;
        Action none = () => { };
        Action<MeterSettings> applied = _ => { };
        Brush back = (Brush)app.Resources["Brush.Window"] ?? Brushes.Black;
        FrameworkElement shown;
        int width, height;
        switch (what)
        {
            case "hotkeys":
            {
                var overlay = new HotkeyOverlayWindow();
                overlay.Render(HotkeyOverlayWindow.RowsOf(new MeterSettings()));
                var body = overlay.DetachBody()!;
                // drawn on a game-like ground: the overlay is see-through in the game
                shown = new Border
                {
                    Padding = new Thickness(24), Background = new LinearGradientBrush(Color.FromRgb(0x3a, 0x4a, 0x3a), Color.FromRgb(0x24, 0x30, 0x40), 45),
                    Child = new Border { Child = body, HorizontalAlignment = HorizontalAlignment.Left },
                };
                width = 0;
                height = 0;
                break;
            }

            default:
            {
                Func<string, ToolPage?> build = id => id switch
                {
                    "dummy" => DummyPage(),
                    "timetable" => TimetablePage(loc, none, applied),
                    "stream" => StreamPage(loc, none, applied),
                    _ => ChecklistPage(),
                };
                string tool = what is "dummy" or "timetable" or "stream" ? what : "checklist";
                var toolsWindow = new ToolsWindow(ToolsWindow.Groups(), build, _ => { });
                toolsWindow.Select(tool);
                shown = (FrameworkElement)toolsWindow.Content;
                toolsWindow.Content = null;
                width = (int)toolsWindow.Width;
                height = (int)toolsWindow.Height;
                shown = new Border { Width = width, Height = height, Background = back, Child = shown };
                break;
            }
        }

        if (width == 0)
        {
            shown.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = (int)Math.Ceiling(shown.DesiredSize.Width);
            height = (int)Math.Ceiling(shown.DesiredSize.Height);
        }

        for (int pass = 0; pass < 3; pass++)
        {
            shown.Measure(new Size(width, height));
            shown.Arrange(new Rect(0, 0, width, height));
            shown.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        var bitmap = new RenderTargetBitmap(width * scale, height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(shown);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(path))
        {
            encoder.Save(file);
        }

        Console.WriteLine($"render-tool: wrote {path} ({what}, {width}x{height} at x{scale})");
        Environment.Exit(0);
    }

    /// <summary>The checklist with made-up tickets of two characters, the way the render-checklist picture has it.</summary>
    private static ToolPage ChecklistPage()
    {
        Aion2.Aion2Tickets.Reset();
        Aion2.Aion2Tickets.TryParseList(Convert.FromHexString(Combat.SelfCheckTickets.SampleLogin), out var entries);
        var t0 = DateTime.Now;
        Aion2.Aion2Tickets.ReceiveList(entries, t0);
        Aion2.Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(4));
        return new ToolPage { Content = ThemedChrome.TakeContent(new ChecklistWindow()) ?? new Grid() };
    }

    private static ToolPage DummyPage()
    {
        var controller = new DummyTestController(() => null);
        var window = new DummyTestWindow(controller, () => { });
        return new ToolPage { Content = ThemedChrome.TakeContent(window) ?? new Grid(), CurrentTab = () => window.Tab };
    }

    private static ToolPage TimetablePage(LocalizationManager loc, Action hotkeys, Action<MeterSettings> applied)
    {
        var view = new TimetableWindow();
        UIElement body = view.DetachView();
        view.Refresh();
        var page = new ModulePage(loc["Settings.Nav.Timetable"], body, s => s.ShowTimetable, (s, v) => s.ShowTimetable = v, s => s.HotkeyTimetable, hotkeys, hotkeys, applied, view.Refresh);
        return new ToolPage { Content = page };
    }

    /// <summary>The stream page with the stream running and a made-up link (the key in it is not a real one).</summary>
    private static ToolPage StreamPage(LocalizationManager loc, Action hotkeys, Action<MeterSettings> applied)
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Text = "http://127.0.0.1:8920/overlay?key=0123456789abcdef\n" + string.Format(loc["Stream.Clients"], 1) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var copy = new Button { Content = loc["Settings.Stream.Copy"], HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(status);
        stack.Children.Add(copy);
        var page = new ModulePage(loc["Settings.Nav.Stream"], stack, s => s.StreamOverlayOn, (s, v) => s.StreamOverlayOn = v, null, hotkeys, hotkeys, applied);
        return new ToolPage { Content = page };
    }
}
