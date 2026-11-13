using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AionDPS.Aion2.Protocol;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The time line of a character: time along the bottom, gear score and combat power up the left side. Every jump of 50 (gear score) or 100 (combat power) since the last
/// mark is a dot; the tooltip of a dot tells what changed since the mark before ("Item … from 12 to 15", "Board … from 5 to 20").
/// </summary>
public static class TimelineChart
{
    private const double W = 1060, H = 280, Left = 86, Right = 24, Top = 16, Bottom = 34;

    private static readonly Color GearColor = Color.FromRgb(0xff, 0xba, 0x42), PowerColor = Color.FromRgb(0x4e, 0xa0, 0xff);

    /// <summary>One line of the chart: a character with its looks over time.</summary>
    public sealed record Series(string Name, Color Color, IReadOnlyList<GameLog.Snapshot> Points);

    public static readonly Color[] Palette =
    {
        Color.FromRgb(0xff, 0xba, 0x42), Color.FromRgb(0x4e, 0xa0, 0xff), Color.FromRgb(0x5f, 0xd3, 0x9a), Color.FromRgb(0xc0, 0x8a, 0xf5), Color.FromRgb(0xff, 0x6b, 0x6b), Color.FromRgb(0x7e, 0xe0, 0xe0),
    };

    /// <summary>One character: gear score and combat power.</summary>
    public static UIElement Build(IReadOnlyList<GameLog.Snapshot> snapshots) => Build(new[] { new Series("", GearColor, snapshots) }, withPower: true);

    /// <summary>Several characters: one gear score line each.</summary>
    public static UIElement Build(IReadOnlyList<Series> series, bool withPower = false)
    {
        var snapshots = series.SelectMany(s => s.Points).OrderBy(p => p.At).ToList();
        bool single = series.Count == 1;
        withPower &= single && snapshots.Any(p => p.CombatPower > 0);
        var loc = LocalizationManager.Instance;
        var canvas = new Canvas { Width = W, Height = H };
        if (snapshots.Count == 0)
        {
            return canvas;
        }

        DateTime first = snapshots[0].At, last = snapshots[^1].At;
        double span = Math.Max(1, (last - first).TotalSeconds);
        if (span < 3600)
        {
            span = 3600;
        }

        double X(DateTime t) => Left + (t - first).TotalSeconds / span * (W - Left - Right);
        (double Min, double Max) Range(IEnumerable<int> values)
        {
            int lo = values.Min(), hi = values.Max();
            double pad = Math.Max(10, (hi - lo) * 0.12);
            return (lo - pad, hi + pad);
        }

        var gear = Range(snapshots.Select(s => s.ItemLevel).Where(v => v > 0));
        var power = withPower ? Range(snapshots.Select(s => s.CombatPower).Where(v => v > 0)) : (0, 1);
        double YGear(double v) => Top + (1 - (v - gear.Min) / (gear.Max - gear.Min)) * (H - Top - Bottom);
        double YPower(double v) => Top + (1 - (v - power.Item1) / (power.Item2 - power.Item1)) * (H - Top - Bottom);

        Brush Muted() => (Brush)(Application.Current?.TryFindResource("Brush.TextMuted") ?? Brushes.Gray);
        Brush Line() => (Brush)(Application.Current?.TryFindResource("Brush.Border") ?? Brushes.DimGray);
        Color gearColor = single ? series[0].Color : (Application.Current?.TryFindResource("Brush.Text") as SolidColorBrush)?.Color ?? Colors.White;

        // the grid: four lines with the gear score on the left (the combat power beside it for one character)
        for (int i = 0; i <= 4; i++)
        {
            double y = Top + i * (H - Top - Bottom) / 4;
            canvas.Children.Add(new Line { X1 = Left, X2 = W - Right, Y1 = y, Y2 = y, Stroke = Line(), StrokeThickness = 1, Opacity = 0.6 });
            double g = gear.Max - i * (gear.Max - gear.Min) / 4;
            canvas.Children.Add(Label(Math.Round(g).ToString("N0"), 2, y - 8, single ? new SolidColorBrush(gearColor) : Muted()));
            if (withPower)
            {
                double c = power.Item2 - i * (power.Item2 - power.Item1) / 4;
                canvas.Children.Add(Label(Math.Round(c).ToString("N0"), 2 + 40, y - 8, new SolidColorBrush(PowerColor), 11));
            }
        }

        // the time: a label every fifth of the span
        for (int i = 0; i <= 5; i++)
        {
            DateTime t = first.AddSeconds(span * i / 5);
            double x = Left + i * (W - Left - Right) / 5;
            canvas.Children.Add(Label(span > 3 * 86400 ? t.ToString("dd.MM.") : t.ToString("dd.MM. HH:mm"), x - 24, H - Bottom + 8, Muted(), 11));
        }

        void Dot(double x, double y, Color color, string tip)
        {
            var dot = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1.2, Cursor = System.Windows.Input.Cursors.Hand };
            ToolTipService.SetInitialShowDelay(dot, 80);
            ToolTipService.SetShowDuration(dot, 30000);
            dot.ToolTip = new TextBlock { Text = tip, FontSize = 12, Margin = new Thickness(2) };
            Canvas.SetLeft(dot, x - 5.5);
            Canvas.SetTop(dot, y - 5.5);
            canvas.Children.Add(dot);
        }

        foreach (Series line in series)
        {
            var pts = line.Points.Where(p => p.ItemLevel > 0).ToList();
            if (pts.Count == 0)
            {
                continue;
            }

            Color color = single ? GearColor : line.Color;
            if (pts.Count > 1)
            {
                canvas.Children.Add(new Polyline { Points = new PointCollection(pts.Select(p => new Point(X(p.At), YGear(p.ItemLevel)))), Stroke = new SolidColorBrush(color), StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round });
            }

            if (withPower && line.Points.Count > 1)
            {
                canvas.Children.Add(new Polyline { Points = new PointCollection(line.Points.Where(p => p.CombatPower > 0).Select(p => new Point(X(p.At), YPower(p.CombatPower)))), Stroke = new SolidColorBrush(PowerColor), StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round });
            }

            // the marks: the first look, then every jump of 50 (gear score) / 100 (combat power) since the last mark
            GameLog.Snapshot mark = pts[0];
            for (int i = 0; i < pts.Count; i++)
            {
                GameLog.Snapshot p = pts[i];
                bool jump = i == 0 || Math.Abs(p.ItemLevel - mark.ItemLevel) >= 50 || Math.Abs(p.CombatPower - mark.CombatPower) >= 100;
                if (!jump)
                {
                    continue;
                }

                string who = line.Name.Length > 0 ? line.Name + "\n" : "";
                string tip = who + (i == 0 ? loc["Hub.Stats.Start"] + "  " + p.At.ToString("dd.MM.yyyy HH:mm") : p.At.ToString("dd.MM.yyyy HH:mm") + "\n" + string.Join("\n", Changes(mark, p, loc)));
                Dot(X(p.At), YGear(p.ItemLevel), color, tip);
                if (withPower && p.CombatPower > 0)
                {
                    Dot(X(p.At), YPower(p.CombatPower), PowerColor, tip);
                }

                mark = p;
            }
        }

        // the legend
        if (single)
        {
            canvas.Children.Add(Label("■ " + loc["Hub.Stats.GearScore"], Left + 8, 0, new SolidColorBrush(GearColor), 12));
            if (withPower)
            {
                canvas.Children.Add(Label("■ " + loc["Hub.Stats.CombatPower"], Left + 180, 0, new SolidColorBrush(PowerColor), 12));
            }
        }
        else
        {
            double x = Left + 8;
            foreach (Series line in series.Where(l => l.Points.Count > 0))
            {
                canvas.Children.Add(Label("■ " + line.Name, x, 0, new SolidColorBrush(line.Color), 12));
                x += 22 + line.Name.Length * 8;
            }
        }

        return canvas;
    }

    private static TextBlock Label(string text, double x, double y, Brush brush, double size = 12)
    {
        var block = new TextBlock { Text = text, Foreground = brush, FontSize = size };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        return block;
    }

    private static Dictionary<int, (int Item, int Enchant)> Parse(string equip) => equip.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split(':')).Where(p => p.Length == 3 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _) && int.TryParse(p[2], out _))
        .ToDictionary(p => int.Parse(p[0]), p => (int.Parse(p[1]), int.Parse(p[2])));

    private static Dictionary<int, int> ParseBoards(string boards) => boards.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split(':')).Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
        .ToDictionary(p => int.Parse(p[0]), p => int.Parse(p[1]));

    /// <summary>What differs between two looks, as lines for the tooltip.</summary>
    public static List<string> Changes(GameLog.Snapshot before, GameLog.Snapshot now, LocalizationManager loc)
    {
        string FromTo(string what, object a, object b) => string.Format(loc["Hub.Stats.FromTo"], what, a, b);
        var lines = new List<string>();
        if (before.ItemLevel != now.ItemLevel)
        {
            lines.Add(FromTo(loc["Hub.Stats.GearScore"], before.ItemLevel.ToString("N0"), now.ItemLevel.ToString("N0")));
        }

        if (before.CombatPower != now.CombatPower)
        {
            lines.Add(FromTo(loc["Hub.Stats.CombatPower"], before.CombatPower.ToString("N0"), now.CombatPower.ToString("N0")));
        }

        var a = Parse(before.Equip);
        var b = before.Equip.Length == 0 || now.Equip.Length == 0 ? new Dictionary<int, (int Item, int Enchant)>() : Parse(now.Equip);
        foreach (var (slot, item) in b.OrderBy(kv => kv.Key))
        {
            string name = Aion2ItemNames.NameOf(item.Item, loc.Language) ?? string.Format(loc["Hub.Stats.Slot"], slot);
            if (!a.TryGetValue(slot, out var old))
            {
                lines.Add(name + " (" + loc["Hub.Stats.New"] + ")");
            }
            else if (old.Item != item.Item)
            {
                lines.Add(FromTo(string.Format(loc["Hub.Stats.Slot"], slot), Aion2ItemNames.NameOf(old.Item, loc.Language) ?? old.Item.ToString(), name));
            }
            else if (old.Enchant != item.Enchant)
            {
                lines.Add(FromTo(name, old.Enchant, item.Enchant));
            }
        }

        var boardsBefore = ParseBoards(before.Boards);
        foreach (var (board, count) in (before.Boards.Length == 0 || now.Boards.Length == 0 ? new Dictionary<int, int>() : ParseBoards(now.Boards)).OrderBy(kv => kv.Key))
        {
            int old = boardsBefore.GetValueOrDefault(board);
            if (old != count)
            {
                lines.Add(FromTo(string.Format(loc["Hub.Stats.Board"], Aion2DaevanionCatalog.BoardName(board)), old, count));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add("–");
        }

        return lines;
    }

    /// <summary>Made-up looks for a character without a log yet: how the line and its tooltips could look.</summary>
    public static List<GameLog.Snapshot> Sample()
    {
        DateTime t = DateTime.Now.AddDays(-28);
        string boards(int a) => $"11:{a};12:{a / 2};";
        return new List<GameLog.Snapshot>
        {
            new(t, 2780, 61_000, "1:110130051:12;2:115030053:12;", boards(5)),
            new(t.AddDays(5), 2840, 66_500, "1:110130051:15;2:115030053:12;", boards(5)),
            new(t.AddDays(11), 2910, 72_400, "1:110130051:15;2:115030053:16;", boards(20)),
            new(t.AddDays(17), 2965, 80_100, "1:110130051:18;2:115030053:16;", boards(20)),
            new(t.AddDays(23), 3011, 89_800, "1:110130051:20;2:115030053:18;", boards(34)),
            new(t.AddDays(28), 3090, 97_300, "1:110130051:20;2:115030053:20;", boards(34)),
        };
    }
}
