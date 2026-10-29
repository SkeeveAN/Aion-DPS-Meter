using System.Windows;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// A tool in a window of its own ("pop out"): the page of the tools window, put anywhere on any monitor. Its place and whether it is open are kept, so it is back
/// where it was after a restart; closing it with its X ends it (it stays closed at the next start). A place on a monitor that is not there goes to the first
/// monitor (see <see cref="WindowPlacement"/>).
/// </summary>
public sealed class PopOutWindow : Window
{
    private static readonly Dictionary<string, PopOutWindow> Opened = new();

    /// <summary>The meter is ending: its windows close with it, which is not the player closing them (they come back at the next start).</summary>
    public static bool Exiting { get; set; }

    private readonly string _id;
    private readonly ToolPage _page;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private PopOutWindow(string id, string title, ToolPage page, Rect place)
    {
        _id = id;
        _page = page;
        Title = title;
        MinWidth = 320;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = place.X;
        Top = place.Y;
        Width = place.Width;
        Height = place.Height;
        Content = page.Content;
        ThemedChrome.Apply(this);
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Persist(open: true);
        };
        SizeChanged += (_, _) => Changed();
        LocationChanged += (_, _) => Changed();
        Closing += (_, _) =>
        {
            _save.Stop();
            // the X of the window ends the pop-out; the meter closing does not
            Persist(open: Exiting || Application.Current?.Dispatcher.HasShutdownStarted == true);
        };
        Closed += (_, _) =>
        {
            Opened.Remove(_id);
            _page.Cleanup?.Invoke();
            WindowPlacement.Overwrote -= OnOverwrote;
        };
        WindowPlacement.Overwrote += OnOverwrote;
    }

    /// <summary>
    /// Shows the tool <paramref name="id"/> in a window of its own (brings it up when it is already there). <paramref name="restore"/>: at the start of the meter, where the
    /// window is put back without taking the focus from the game.
    /// </summary>
    public static void Open(string id, string title, Func<string, ToolPage?> build, bool restore)
    {
        if (Opened.TryGetValue(id, out PopOutWindow? shown))
        {
            if (shown.WindowState == WindowState.Minimized)
            {
                shown.WindowState = WindowState.Normal;
            }

            shown.Activate();
            return;
        }

        ToolPage? page = build(id);
        if (page is null)
        {
            return;
        }

        MeterSettings settings = MeterSettings.Load();
        Rect saved;
        if (settings.PopOuts.TryGetValue(id, out PopOutPlace? place) && place.Width > 0 && place.Height > 0)
        {
            saved = new Rect(place.Left, place.Top, place.Width, place.Height);
        }
        else
        {
            // the first time: on the monitor of the meter, a little off its corner
            Rect main = Application.Current?.MainWindow is { IsLoaded: true } m ? new Rect(m.Left, m.Top, m.ActualWidth, m.ActualHeight) : WindowPlacement.Primary();
            Rect area = WindowPlacement.WorkAreaOf(main);
            double width = Math.Min(ToolsWindow.DefaultWidth(id), area.Width);
            double height = Math.Min(720, area.Height);
            saved = new Rect(Math.Max(area.Left, area.Left + 60 + 30 * Opened.Count), Math.Max(area.Top, area.Top + 60 + 30 * Opened.Count), width, height);
        }

        Rect rect = WindowPlacement.Resolve(saved, out _);
        Rect room = WindowPlacement.WorkAreaOf(rect);
        rect = new Rect(rect.X, rect.Y, Math.Min(rect.Width, room.Width), Math.Min(rect.Height, room.Height));
        var window = new PopOutWindow(id, title, page, rect) { ShowActivated = !restore };
        Opened[id] = window;
        window.Show();
        window.Persist(open: true);
    }

    private void Changed()
    {
        _save.Stop();
        _save.Start();
    }

    private void OnOverwrote() => Persist(open: true);

    /// <summary>Keeps place, size and whether the window is open; nothing while the saved places are being kept for the next start.</summary>
    private void Persist(bool open)
    {
        if (!WindowPlacement.CanSave)
        {
            return;
        }

        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height) : RestoreBounds;
        MeterSettings s = MeterSettings.Load();
        s.PopOuts[_id] = new PopOutPlace { Left = bounds.X, Top = bounds.Y, Width = bounds.Width, Height = bounds.Height, Open = open };
        s.Save();
    }
}
