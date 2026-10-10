using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// Map cut-outs for the Rare tab of the pet settings: the places where a mob stands, each as a zoomed piece of the world map with a red pin on every
/// spawn point, the Kibelisks (blue diamonds) around, the other mobs of the same pet as grey dots and the whole map small in the corner with a frame
/// where the cut-out lies. Spawn points far apart get one cut-out each (up to four); the nearest Kibelisk is named under the picture.
/// </summary>
internal static class MapSnippets
{
    private const int Pixels = 360;
    private const int MaxPlaces = 4;
    private const double JoinDistance = 0.06;  // spawn points closer than this (a fraction of the whole map) belong to one place
    private const double KibeliskReach = 0.12; // a Kibelisk this near is shown in the cut-out, the view grows to hold it
    private static readonly Dictionary<string, BitmapSource?> Tiles = new();
    private static readonly List<string> TileOrder = new();
    private static readonly Dictionary<string, BitmapSource?> Overviews = new();

    private sealed record Place(List<(double X, double Y)> Points, double CenterX, double CenterY, double Side);

    private static (double X, double Y) Norm(Aion2MapInfo map, (float X, float Y) p)
    {
        var (nx, ny) = Aion2Maps.ToNative(map, p.X, p.Y);
        double whole = map.Tile * map.Grid;
        return (nx / whole, ny / whole);
    }

    /// <summary>How many places (groups of spawn points close together) the spawn points make, at most four.</summary>
    public static int PlaceCount(Aion2MapInfo map, IReadOnlyList<(float X, float Y)> spots) => Places(spots.Select(p => Norm(map, p)).ToList()).Count;

    /// <summary>One cut-out per place, the biggest group first.</summary>
    public static FrameworkElement Build(Aion2MapInfo map, IReadOnlyList<(float X, float Y)> spots, IReadOnlyList<(float X, float Y)> others, string language)
    {
        var own = spots.Select(p => Norm(map, p)).ToList();
        var other = others.Select(p => Norm(map, p)).ToList();
        var kibs = Aion2Kibelisks.Of(map.Key).Select(k => (K: k, P: Norm(map, (k.X, k.Y)))).ToList();
        var places = Places(own);
        var grid = new UniformGrid { Columns = places.Count == 1 ? 1 : 2, Rows = places.Count <= 2 ? 1 : 2 };
        for (int i = 0; i < places.Count; i++)
        {
            grid.Children.Add(Cutout(map, places[i], own, other, kibs, language, places.Count > 1 ? i + 1 : 0));
        }

        return grid;
    }

    private static List<Place> Places(List<(double X, double Y)> points)
    {
        var groups = points.Select(p => new List<(double X, double Y)> { p }).ToList();
        static double Distance(List<(double X, double Y)> a, List<(double X, double Y)> b) =>
            a.Min(p => b.Min(q => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y))));

        // join the closest two groups as long as they are near each other, then until no more than four places remain
        while (groups.Count > 1)
        {
            (double Dist, int I, int J) best = (double.MaxValue, 0, 0);
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    double d = Distance(groups[i], groups[j]);
                    if (d < best.Dist)
                    {
                        best = (d, i, j);
                    }
                }
            }

            if (best.Dist > JoinDistance && groups.Count <= MaxPlaces)
            {
                break;
            }

            groups[best.I].AddRange(groups[best.J]);
            groups.RemoveAt(best.J);
        }

        return groups.OrderByDescending(g => g.Count).Select(g => PlaceOf(g)).ToList();
    }

    private static Place PlaceOf(List<(double X, double Y)> points)
    {
        double x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
        return new Place(points, (x0 + x1) / 2, (y0 + y1) / 2, Math.Max(0.07, Math.Max(x1 - x0, y1 - y0) * 1.5 + 0.03));
    }

    private static FrameworkElement Cutout(Aion2MapInfo map, Place place, List<(double X, double Y)> own, List<(double X, double Y)> other,
        List<(Aion2Kibelisk K, (double X, double Y) P)> kibs, string language, int number)
    {
        // the nearest Kibelisk; the view grows to show it when it is not too far away
        (Aion2Kibelisk K, (double X, double Y) P, double D)? near = null;
        foreach (var (k, p) in kibs)
        {
            double d = place.Points.Min(q => Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)));
            if (near is null || d < near.Value.D)
            {
                near = (k, p, d);
            }
        }

        if (near is { } n && n.D <= KibeliskReach)
        {
            var all = place.Points.Append(n.P).ToList();
            double x0 = all.Min(p => p.X), x1 = all.Max(p => p.X), y0 = all.Min(p => p.Y), y1 = all.Max(p => p.Y);
            place = place with { CenterX = (x0 + x1) / 2, CenterY = (y0 + y1) / 2, Side = Math.Max(place.Side, Math.Max(x1 - x0, y1 - y0) * 1.4 + 0.03) };
        }

        double side = Math.Min(place.Side, 1);
        double left = Math.Clamp(place.CenterX - side / 2, 0, 1 - side), top = Math.Clamp(place.CenterY - side / 2, 0, 1 - side);
        var picture = new Image { Source = Render(map, left, top, side, own, other, kibs.Select(k => k.P).ToList(), near?.P), Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.Fant);
        var root = new Grid();
        root.Children.Add(picture);
        if (number > 0)
        {
            root.Children.Add(Label(new TextBlock { Text = number.ToString(CultureInfo.InvariantCulture), FontWeight = FontWeights.Bold }, HorizontalAlignment.Left, VerticalAlignment.Top));
        }

        if (near is { } nk)
        {
            double metres = Math.Round(nk.D * map.Tile * map.Grid / map.Scale / 100.0 / 10.0) * 10.0;
            string distance = metres >= 1000 ? (metres / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " km" : metres.ToString("0", CultureInfo.CurrentCulture) + " m";
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, FontSize = 11 };
            text.Inlines.Add(new Run("◆ ") { Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xA7, 0xFF)) });
            text.Inlines.Add(new Run($"{nk.K.NameIn(language)} · {distance}"));
            var caption = Label(text, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            caption.MaxWidth = 150;
            caption.ToolTip = LocalizationManager.Instance["Settings.PetRare.NearestKibelisk"];
            root.Children.Add(caption);
        }

        var frame = new Border { Margin = new Thickness(0, 0, 3, 3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = root, MinHeight = 90,
            Background = new SolidColorBrush(Color.FromRgb(14, 22, 30)) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return frame;
    }

    private static Border Label(TextBlock text, HorizontalAlignment h, VerticalAlignment v)
    {
        text.Foreground = Brushes.White;
        return new Border { Child = text, Margin = new Thickness(4), Padding = new Thickness(5, 2, 5, 2), CornerRadius = new CornerRadius(4), HorizontalAlignment = h, VerticalAlignment = v,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0C, 0x14, 0x1B)) };
    }

    /// <summary>The piece of the map (a square, left/top/side as fractions of the whole map) with everything drawn on it.</summary>
    private static BitmapSource Render(Aion2MapInfo map, double left, double top, double side, List<(double X, double Y)> own, List<(double X, double Y)> other,
        List<(double X, double Y)> kibs, (double X, double Y)? nearest)
    {
        double whole = map.Tile * map.Grid;
        double k = Pixels / (side * whole); // pixels per native map pixel
        double vx = left * whole, vy = top * whole;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 22, 30)), null, new Rect(0, 0, Pixels, Pixels));
            int c0 = Math.Max(0, (int)Math.Floor(vx / map.Tile)), c1 = Math.Min(map.Grid - 1, (int)Math.Floor((vx + side * whole) / map.Tile));
            int r0 = Math.Max(0, (int)Math.Floor(vy / map.Tile)), r1 = Math.Min(map.Grid - 1, (int)Math.Floor((vy + side * whole) / map.Tile));
            for (int col = c0; col <= c1; col++)
            {
                for (int row = r0; row <= r1; row++)
                {
                    if (Tile(map, col, row) is { } tile)
                    {
                        dc.DrawImage(tile, new Rect((col * map.Tile - vx) * k, (row * map.Tile - vy) * k, map.Tile * k, map.Tile * k));
                    }
                }
            }

            Point At((double X, double Y) p) => new((p.X - left) / side * Pixels, (p.Y - top) / side * Pixels);
            bool Inside(Point p, double margin) => p.X > -margin && p.Y > -margin && p.X < Pixels + margin && p.Y < Pixels + margin;
            var grey = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            foreach (var p in other.Select(At).Where(p => Inside(p, 8)))
            {
                dc.DrawEllipse(grey, null, p, 3.5, 3.5);
            }

            var blue = new SolidColorBrush(Color.FromRgb(0x3F, 0xA7, 0xFF));
            var white = new Pen(Brushes.White, 1.6);
            foreach (var kib in kibs)
            {
                var p = At(kib);
                if (!Inside(p, 12))
                {
                    continue;
                }

                bool hot = nearest is { } n && n.X == kib.X && n.Y == kib.Y;
                double r = hot ? 9 : 6.5;
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(p.X, p.Y - r * 1.35), true, true);
                    g.LineTo(new Point(p.X + r, p.Y), true, false);
                    g.LineTo(new Point(p.X, p.Y + r * 1.35), true, false);
                    g.LineTo(new Point(p.X - r, p.Y), true, false);
                }

                diamond.Freeze();
                dc.DrawGeometry(blue, hot ? new Pen(Brushes.White, 2.5) : white, diamond);
            }

            var pin = PinGeometry();
            var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            foreach (var p in own.Select(At).Where(p => Inside(p, 30)))
            {
                dc.PushTransform(new TranslateTransform(p.X + 1.5, p.Y + 2));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), null, pin); // shadow
                dc.Pop();
                dc.PushTransform(new TranslateTransform(p.X, p.Y));
                dc.DrawGeometry(red, new Pen(Brushes.White, 2), pin);
                dc.DrawEllipse(Brushes.White, null, new Point(0, -PinHeight), PinRadius * 0.38, PinRadius * 0.38);
                dc.Pop();
            }

            // the whole map in the corner, with a frame where this cut-out lies
            if (Overview(map) is { } overview)
            {
                const double box = 64, margin = 5;
                var area = new Rect(Pixels - box - margin, Pixels - box - margin, box, box);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 22, 30)), new Pen(Brushes.White, 1), area);
                dc.DrawImage(overview, area);
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), 1.8),
                    new Rect(area.X + left * box, area.Y + top * box, Math.Max(3, side * box), Math.Max(3, side * box)));
            }
        }

        var target = new RenderTargetBitmap(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32);
        target.Render(drawing);
        target.Freeze();
        return target;
    }

    private const double PinRadius = 10, PinHeight = 23;

    /// <summary>A map pin whose needle tip is the origin (so it stands on the spawn point) and whose round head lies above it.</summary>
    private static Geometry PinGeometry()
    {
        double r = PinRadius, h = PinHeight;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true);
            g.BezierTo(new Point(-r * 0.35, -h * 0.45), new Point(-r, -h * 0.62), new Point(-r, -h), true, false);
            g.ArcTo(new Point(r, -h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            g.BezierTo(new Point(r, -h * 0.62), new Point(r * 0.35, -h * 0.45), new Point(0, 0), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static BitmapSource? Tile(Aion2MapInfo map, int col, int row)
    {
        string key = $"{map.Key}/{col}_{row}";
        if (Tiles.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var tile = MapTiles.Load(map, col, row, Math.Min(map.Tile, 1024));
        Tiles[key] = tile;
        TileOrder.Add(key);
        while (TileOrder.Count > 16)
        {
            Tiles.Remove(TileOrder[0]);
            TileOrder.RemoveAt(0);
        }

        return tile;
    }

    private static BitmapSource? Overview(Aion2MapInfo map)
    {
        if (!Overviews.TryGetValue(map.Key, out var overview))
        {
            Overviews[map.Key] = overview = MapTiles.Overview(map);
        }

        return overview;
    }
}
