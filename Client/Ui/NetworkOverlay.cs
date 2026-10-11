using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Capture;

namespace AionDPS.Ui;

/// <summary>The lines the network overlay can show (the ids are what <see cref="MeterSettings.NetworkOverlayHidden"/> keeps) and how they read.</summary>
public static class NetworkOverlayRows
{
    public static readonly string[] Ids = { "server", "ping", "jitter", "traffic", "loss", "connection", "load" };

    private static readonly Color Good = Color.FromRgb(0x5B, 0xD0, 0x7A);
    private static readonly Color Warn = Color.FromRgb(0xFF, 0xD2, 0x7A);
    private static readonly Color Bad = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Neutral = Color.FromRgb(0xDD, 0xE6, 0xEC);

    private static string Num(double value, string format = "0.#") => value.ToString(format, CultureInfo.CurrentCulture);

    private static Color PingColor(double ms) => ms < 60 ? Good : ms < 120 ? Warn : Bad;

    /// <summary>The lines to show now: label, value and the colour of the value.</summary>
    public static List<(string Label, string Value, Color Color)> Of(NetworkSnapshot s, ICollection<string> hidden)
    {
        var loc = LocalizationManager.Instance;
        var rows = new List<(string, string, Color)>();
        bool Shown(string id) => !hidden.Contains(id);
        string Label(string id) => loc["Network.Row." + id];

        if (Shown("server"))
        {
            rows.Add((Label("server"), s.Server ?? loc["Network.Waiting"], Neutral));
        }

        if (!s.Connected)
        {
            if (!Shown("server"))
            {
                rows.Add((Label("connection"), loc["Network.Waiting"], Warn));
            }

            return rows;
        }

        if (Shown("ping"))
        {
            rows.Add(s.PingMs is { } ping
                ? (Label("ping"), $"{Num(ping, "0")} ms  ({string.Format(loc["Network.Ping.Detail"], Num(s.PingAverageMs ?? ping, "0"), Num(s.PingMinMs ?? ping, "0"), Num(s.PingMaxMs ?? ping, "0"))})", PingColor(ping))
                : (Label("ping"), "…", Neutral));
        }

        if (Shown("jitter"))
        {
            rows.Add(s.JitterMs is { } jitter
                ? (Label("jitter"), $"{Num(jitter)} ms", jitter < 10 ? Good : jitter < 30 ? Warn : Bad)
                : (Label("jitter"), "…", Neutral));
        }

        if (Shown("traffic"))
        {
            rows.Add((Label("traffic"), $"↓ {Num(s.DownKBps)}  ↑ {Num(s.UpKBps)} kB/s  ({string.Format(loc["Network.Traffic.Peak"], Num(s.DownPeakKBps, "0"))})", Neutral));
        }

        if (Shown("loss"))
        {
            int bad = s.Retransmissions + s.Gaps;
            rows.Add((Label("loss"), string.Format(loc["Network.Loss.Value"], s.Retransmissions, s.Gaps), bad == 0 ? Good : bad <= 5 ? Warn : Bad));
        }

        if (Shown("connection"))
        {
            bool stalled = s.SinceLastData is { } since && since.TotalSeconds >= 5;
            string text = stalled
                ? string.Format(loc["Network.Connection.Stalled"], (int)s.SinceLastData!.Value.TotalSeconds)
                : string.Format(loc["Network.Connection.Online"], WorldBossText.Duration(s.Uptime ?? TimeSpan.Zero));
            if (s.Switches > 0)
            {
                text += " · " + string.Format(loc["Network.Connection.Switches"], s.Switches);
            }

            if (s.Reconnects > 0)
            {
                text += " · " + string.Format(loc["Network.Connection.Reconnects"], s.Reconnects);
            }

            rows.Add((Label("connection"), text, stalled ? Bad : s.Reconnects > 0 ? Warn : Good));
        }

        if (Shown("load"))
        {
            rows.Add((Label("load"), string.Format(loc["Network.Load.Value"], s.Queued, s.Dropped), s.Dropped > 0 ? Bad : s.Queued > 1000 ? Warn : Good));
        }

        return rows;
    }
}

/// <summary>
/// The network overlay: ping, jitter, traffic, packet loss and connection of the game connection, measured from the traffic the capture already sees.
/// Placed, scaled and locked like the world boss overlay (<see cref="MovableOverlay"/>); <see cref="NetworkOverlayController"/> shows it only while the game is in front.
/// </summary>
public sealed class NetworkOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 260, MinHeight = 40 };

    public NetworkOverlayWindow() : base("Aion DPS Network", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 520, LoadSize, SaveSize)
    {
        Body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = _rows,
        };
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.NetworkOverlayWidth, s.NetworkOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.NetworkOverlayWidth = width;
        s.NetworkOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.NetworkOverlayLeft, s.NetworkOverlayTop, s.NetworkOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.NetworkOverlayLeft = left;
        s.NetworkOverlayTop = top;
        s.NetworkOverlayScale = scale;
        s.Save();
    }

    public void Render(IReadOnlyList<(string Label, string Value, Color Color)> rows)
    {
        _rows.Children.Clear();
        foreach (var (label, value, color) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock
            {
                Text = value, FontSize = 11.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color),
            };
            Grid.SetColumn(t, 1);
            grid.Children.Add(n);
            grid.Children.Add(t);
            _rows.Children.Add(grid);
        }
    }
}

/// <summary>Runs the network overlay: once a second it reads the settings and the measurements, and shows the overlay only while the game is in front
/// (or while the overlays are unlocked so it can be placed).</summary>
public sealed class NetworkOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private NetworkOverlayWindow? _overlay;

    public NetworkOverlayController()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        var settings = MeterSettings.Load();
        if (!settings.ShowNetworkOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = settings.NetworkOverlayLocked;
        _overlay ??= new NetworkOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (locked && GameWindow.ForegroundClientArea() is null)
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.Render(NetworkOverlayRows.Of(NetworkStats.Snapshot(), settings.NetworkOverlayHidden));
        _overlay.ShowOverlay(true);
    }
}
