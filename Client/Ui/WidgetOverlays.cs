using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>Where a widget stands as an overlay (see <see cref="MeterSettings.WidgetOverlays"/>), by the widget's id.</summary>
public sealed class WidgetOverlayPlace
{
    public bool On { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double Scale { get; set; } = 1;
}

/// <summary>
/// What a widget is: one thing that can stand on a dashboard, as an overlay over the game or in a small window of its own. The built-in widgets of the dashboard
/// (<see cref="HubDashboard.Widgets"/>) and every page of the app ("page:" and its id) are widgets; this builds the content of one for the places other than the dashboard.
/// </summary>
public static class WidgetKind
{
    public const string OverlayPrefix = "Aion DPS Widget ";

    /// <summary>The pages of the app by id, set once the new interface exists (the names of the page widgets).</summary>
    public static Func<IReadOnlyList<HubPageInfo>>? Pages { get; set; }

    /// <summary>The mode of the live meter (0 damage, 1 healing, 2 damage taken, 3 all in one, 4 all in one +): read and set by the main window; the designer's panel of the meter widget offers it.</summary>
    public static Func<int>? MeterMode { get; set; }

    public static Action<int>? SetMeterMode { get; set; }

    /// <summary>Whether the live meter has any row to show; an empty meter overlay is not drawn at all (set by the main window).</summary>
    public static Func<bool>? MeterHasRows { get; set; }

    /// <summary>The meter's mode was changed (by the designer's buttons or by the window's): every place that shows the mode follows.</summary>
    public static event Action? MeterModeChanged;

    public static void RaiseMeterModeChanged() => MeterModeChanged?.Invoke();

    /// <summary>The overlays are hidden (the window mode: the app is up and no overlay stands over the game); the widget overlays follow it like the others, except while the overlay designer is on.</summary>
    public static bool OverlaysHidden { get; set; }

    public static bool IsOverlayTitle(string title) => title.StartsWith(OverlayPrefix, StringComparison.Ordinal);

    public static string IdOfTitle(string title) => title[OverlayPrefix.Length..];

    /// <summary>The name of a widget in the language of the program; null when there is no such widget (a page that is gone).</summary>
    public static string? TitleOf(string id)
    {
        if (id.StartsWith("page:", StringComparison.Ordinal))
        {
            return Pages?.Invoke().FirstOrDefault(p => "page:" + p.Id == id)?.Title;
        }

        HubDashboard.Widget? widget = HubDashboard.Widgets.FirstOrDefault(w => w.Id == id);
        return widget is null ? null : LocalizationManager.Instance[widget.TitleKey];
    }

    /// <summary>Every widget there is: the built-in ones and the pages of the app.</summary>
    public static IEnumerable<string> AllIds()
    {
        foreach (HubDashboard.Widget widget in HubDashboard.Widgets)
        {
            yield return widget.Id;
        }

        foreach (HubPageInfo page in Pages?.Invoke() ?? Array.Empty<HubPageInfo>())
        {
            yield return "page:" + page.Id;
        }
    }

    /// <summary>The first size of a widget as an overlay or a window.</summary>
    public static (double Width, double Height) SizeOf(string id)
    {
        if (id.StartsWith("page:", StringComparison.Ordinal))
        {
            return (520, 400);
        }

        HubDashboard.Widget? widget = HubDashboard.Widgets.FirstOrDefault(w => w.Id == id);
        return widget is null ? (420, 260) : (widget.W, widget.H);
    }

    /// <summary>Fills a place with a widget's content and keeps it up to date: a page of the app and the widgets that ask the network are built once, the others every second.</summary>
    public sealed class Host
    {
        private readonly string _id;
        private readonly DashboardSources _sources;
        private readonly Border _place;
        private UIElement? _kept;
        private ToolPage? _page;

        public Host(string id, DashboardSources sources, Border place)
        {
            _id = id;
            _sources = sources;
            _place = place;
        }

        public void Refresh()
        {
            bool page = _id.StartsWith("page:", StringComparison.Ordinal);
            HubDashboard.Widget? builtIn = page ? null : HubDashboard.Widgets.FirstOrDefault(w => w.Id == _id);
            if (page || builtIn is { Static: true })
            {
                if (_kept is null)
                {
                    if (page)
                    {
                        _page = _sources.BuildPage(_id["page:".Length..]);
                        _kept = _page?.Content ?? new TextBlock { Text = "–" };
                    }
                    else
                    {
                        _kept = HubDashboard.WidgetBody(_id, _sources, DateTime.Now);
                    }

                    _place.Child = _kept;
                }

                return;
            }

            _place.Child = HubDashboard.WidgetBody(_id, _sources, DateTime.Now);
        }

        public void Stop() => _page?.Cleanup?.Invoke();
    }
}

/// <summary>A widget over the game: placed, scaled, docked and made clear like every overlay (<see cref="MovableOverlay"/>); its content is the widget's, drawn again every second.</summary>
public sealed class WidgetOverlayWindow : MovableOverlay
{
    private readonly WidgetKind.Host _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public string WidgetId { get; }

    public WidgetOverlayWindow(string id, DashboardSources sources)
        : base(WidgetKind.OverlayPrefix + id, new Border(), () => Load(id), (l, t, s) => Save(id, l, t, s), SystemParameters.WorkArea.Left + 80, 140,
            () => LoadSize(id), (w, h) => SaveSize(id, w, h), OverlayEdgeMode.Crop)
    {
        WidgetId = id;
        // the content fills the frame: pulling the overlay smaller or larger makes the widget itself smaller or larger (it is laid out for the new size, not cut off)
        var content = new Border { MinWidth = 80, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var body = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Child = content };
        // the live meter keeps the look it always had over the game (the overlay's dark colour at its own opacity); the other widgets sit on the panel colour
        bool meter = id == "meter";
        Color panel = Application.Current?.TryFindResource(meter ? "Brush.OverlayBg" : "Brush.Panel") is SolidColorBrush found ? found.Color : Color.FromRgb(0x0E, 0x18, 0x20);
        body.Background = new SolidColorBrush(Color.FromArgb(meter ? (byte)0x99 : (byte)0xE6, panel.R, panel.G, panel.B));
        if (meter)
        {
            content.MinWidth = 200; // the numbers need their room; narrower than this the names are tiny, which tells the player not to pull it smaller
            body.Padding = new Thickness(6, 4, 6, 4);
            body.CornerRadius = new CornerRadius(6);
        }
        Body = body;
        _host = new WidgetKind.Host(id, sources, content);
        _timer.Tick += (_, _) => _host.Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _host.Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _host.Stop();
        };
        _host.Refresh();
    }

    private static WidgetOverlayPlace PlaceOf(MeterSettings s, string id)
    {
        if (!s.WidgetOverlays.TryGetValue(id, out WidgetOverlayPlace? place))
        {
            s.WidgetOverlays[id] = place = new WidgetOverlayPlace();
        }

        return place;
    }

    private static (double?, double?, double) Load(string id)
    {
        WidgetOverlayPlace? p = MeterSettings.Load().WidgetOverlays.GetValueOrDefault(id);
        return (p?.Left, p?.Top, p?.Scale > 0 ? p.Scale : 1);
    }

    private static void Save(string id, double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        WidgetOverlayPlace p = PlaceOf(s, id);
        p.Left = left;
        p.Top = top;
        p.Scale = scale;
        s.Save();
    }

    private static (double?, double?) LoadSize(string id)
    {
        WidgetOverlayPlace? p = MeterSettings.Load().WidgetOverlays.GetValueOrDefault(id);
        (double width, double height) = WidgetKind.SizeOf(id);
        return (p?.Width ?? width, p?.Height ?? height); // before anyone pulled it: the widget's first size
    }

    private static void SaveSize(string id, double? width, double? height)
    {
        var s = MeterSettings.Load();
        WidgetOverlayPlace p = PlaceOf(s, id);
        p.Width = width;
        p.Height = height;
        s.Save();
    }

    /// <summary>Puts a widget on the screen as an overlay (or takes it off); the controller shows it within a second.</summary>
    public static void SetOn(string id, bool on)
    {
        var s = MeterSettings.Load();
        PlaceOf(s, id).On = on;
        s.Save();
    }

    public static bool IsOn(MeterSettings s, string id) => s.WidgetOverlays.TryGetValue(id, out WidgetOverlayPlace? p) && p.On;
}

/// <summary>Keeps the windows of the widgets that are on as overlays: once a second it reads the settings, makes or closes the windows and shows them (only while the game is in front, or while the overlays are unlocked so they can be placed).</summary>
public sealed class WidgetOverlayController : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<DashboardSources?> _sources;
    private readonly Dictionary<string, WidgetOverlayWindow> _windows = new();

    public WidgetOverlayController(Func<DashboardSources?> sources)
    {
        _sources = sources;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Tick()
    {
        MeterSettings s = MeterSettings.Load();
        DashboardSources? sources = _sources();
        if (sources is null)
        {
            return;
        }

        // the live meter overlay of the first betas was the page "Live-Meter"; it is the meter widget now (same place, same size)
        if (s.WidgetOverlays.TryGetValue("page:live", out WidgetOverlayPlace? oldMeter))
        {
            if (!s.WidgetOverlays.ContainsKey("meter"))
            {
                s.WidgetOverlays["meter"] = oldMeter;
            }

            s.WidgetOverlays.Remove("page:live");
            if (_windows.Remove("page:live", out WidgetOverlayWindow? oldWindow))
            {
                oldWindow.Close();
            }

            s.Save();
        }

        bool locked = s.WidgetOverlaysLocked;
        bool gameInFront = GameWindow.ForegroundClientArea() is not null;
        foreach (var (id, place) in s.WidgetOverlays)
        {
            if (!place.On || WidgetKind.TitleOf(id) is null)
            {
                if (_windows.TryGetValue(id, out WidgetOverlayWindow? off))
                {
                    off.ShowOverlay(false);
                }

                continue;
            }

            if (!_windows.TryGetValue(id, out WidgetOverlayWindow? window))
            {
                window = new WidgetOverlayWindow(id, sources);
                _windows[id] = window;
            }

            window.ApplyLockedIfChanged(locked);
            // an empty live meter is not drawn over the game (only while the overlays are unlocked, to place it)
            bool empty = id == "meter" && locked && WidgetKind.MeterHasRows?.Invoke() == false;
            window.ShowOverlay((!locked || gameInFront) && (!WidgetKind.OverlaysHidden || OverlayDesigner.Active) && !empty);
        }

        foreach (string gone in _windows.Keys.Where(k => !s.WidgetOverlays.ContainsKey(k)).ToList())
        {
            _windows[gone].Close();
            _windows.Remove(gone);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (WidgetOverlayWindow window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }
}

/// <summary>A widget in a small window of its own, to be moved wherever it is wanted: the same content as on the dashboard, drawn again every second.</summary>
public sealed class WidgetPopoutWindow : Window
{
    private static readonly Dictionary<string, WidgetPopoutWindow> Open = new();
    private readonly WidgetKind.Host _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private WidgetPopoutWindow(string id, DashboardSources sources, string title)
    {
        Title = title;
        (double width, double height) = WidgetKind.SizeOf(id);
        Width = width + 28;
        Height = height + 48;
        MinWidth = 220;
        MinHeight = 120;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        var place = new Border { Margin = new Thickness(12, 8, 12, 12) };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = place };
        ThemedChrome.Apply(this);
        _host = new WidgetKind.Host(id, sources, place);
        _timer.Tick += (_, _) => _host.Refresh();
        Loaded += (_, _) =>
        {
            _host.Refresh();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _host.Stop();
            Open.Remove(id);
        };
    }

    /// <summary>Opens the widget's window (a second click brings the one that is open to the front).</summary>
    public static void Show(string id, DashboardSources sources)
    {
        if (Open.TryGetValue(id, out WidgetPopoutWindow? open))
        {
            open.Activate();
            return;
        }

        string title = WidgetKind.TitleOf(id) ?? id;
        var window = new WidgetPopoutWindow(id, sources, title);
        Open[id] = window;
        ((Window)window).Show();
    }
}
