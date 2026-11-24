using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>Draws the picture of one farm map: the game's own map (tiles), the places of the missing pets, the dangers, the numbered spots and, beside it, the list of the spots.</summary>
public static class FarmMapRenderer
{
    private const int LegendWidth = 760, SampleSize = 256;
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD7, 0x00), Back = Color.FromRgb(0x15, 0x1A, 0x22), PanelBack = Color.FromRgb(0x18, 0x20, 0x29);

    /// <summary>The name of a map in the player's language: the world maps from the tool's texts (the client's own names), an instance from its own names.</summary>
    public static string NameOf(Aion2MapInfo map)
    {
        var loc = LocalizationManager.Instance;
        string? key = map.Key switch
        {
            "verteron" => "Settings.FarmMap.Verteron",
            "altgard" => "Settings.FarmMap.Altgard",
            "abyss" => "Settings.FarmMap.AbyssLower",
            "abyss_outer" => "Settings.FarmMap.AbyssOuter",
            _ => null,
        };
        return key is not null ? loc[key] : map.NameIn(loc.Language);
    }

    /// <summary>Tells whether a world place lies on drawn land: the game's map has dark holes where its picture shows nothing, and a spawn point there is no place to go to.</summary>
    public static Func<float, float, bool> LandTest(Aion2MapInfo map)
    {
        var samples = new Dictionary<(int, int), byte[]?>();
        return (x, y) =>
        {
            (double px, double py) = Aion2Maps.ToNative(map, x, y);
            int col = (int)Math.Floor(px / map.Tile), row = (int)Math.Floor(py / map.Tile);
            if (col < 0 || row < 0 || col >= map.Grid || row >= map.Grid)
            {
                return false;
            }

            if (!samples.TryGetValue((col, row), out byte[]? data))
            {
                data = Sample(map, col, row);
                samples[(col, row)] = data;
            }

            if (data is null)
            {
                return false;
            }

            int sx = Math.Clamp((int)((px - col * map.Tile) * SampleSize / map.Tile), 0, SampleSize - 1);
            int sy = Math.Clamp((int)((py - row * map.Tile) * SampleSize / map.Tile), 0, SampleSize - 1);
            int i = (sy * SampleSize + sx) * 4;
            return Math.Max(data[i], Math.Max(data[i + 1], data[i + 2])) >= 75;
        };
    }

    private static byte[]? Sample(Aion2MapInfo map, int col, int row)
    {
        string file = Path.Combine(Aion2Maps.TileFolder(map), $"{col}_{row}.jpg");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var image = Decode(file, SampleSize);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            var data = new byte[SampleSize * SampleSize * 4];
            if (w != SampleSize || h != SampleSize)
            {
                var scaled = new TransformedBitmap(converted, new ScaleTransform((double)SampleSize / w, (double)SampleSize / h));
                scaled.CopyPixels(data, SampleSize * 4, 0);
            }
            else
            {
                converted.CopyPixels(data, SampleSize * 4, 0);
            }

            return data;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource Decode(string file, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(file);
        bmp.DecodePixelWidth = Math.Max(1, width);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The picture of a plan as PNG bytes.</summary>
    public static byte[] Render(FarmPlan plan, string title, string subtitle, bool withLegend = true)
    {
        var map = plan.Map;
        var loc = LocalizationManager.Instance;
        double s = map.Scale;

        // the part of the map that is drawn: the places of the missing pets and the spots, with a margin
        var native = plan.Points.Select(p => Aion2Maps.ToNative(map, p.X, p.Y)).ToList();
        foreach (var spot in plan.Spots)
        {
            native.Add(Aion2Maps.ToNative(map, spot.X, spot.Y));
        }

        double margin = 160 + (plan.Spots.Count == 0 ? 0 : plan.Spots.Max(sp => sp.Radius) * s);
        double x0 = Math.Max(0, native.Min(p => p.X) - margin), x1 = Math.Min(map.Grid * map.Tile, native.Max(p => p.X) + margin);
        double y0 = Math.Max(0, native.Min(p => p.Y) - margin), y1 = Math.Min(map.Grid * map.Tile, native.Max(p => p.Y) + margin);
        double f = Math.Min(1, Math.Min(1900 / Math.Max(1, x1 - x0), 2300 / Math.Max(1, y1 - y0)));
        int mapW = (int)Math.Ceiling((x1 - x0) * f), mapH = (int)Math.Ceiling((y1 - y0) * f);
        Point At(double wx, double wy)
        {
            (double px, double py) = Aion2Maps.ToNative(map, wx, wy);
            return new Point((px - x0) * f, (py - y0) * f);
        }

        // the badges of the spots: pushed apart where they would lie on each other, with a line to the real place
        var places = plan.Spots.Select(sp => At(sp.X, sp.Y)).ToArray();
        var badges = places.ToArray();
        double BadgeR = withLegend ? 26 : 46; // the map alone is shown small (overlay, widget): bigger numbers
        for (int pass = 0; pass < 300; pass++)
        {
            bool moved = false;
            for (int a = 0; a < badges.Length; a++)
            {
                for (int b = a + 1; b < badges.Length; b++)
                {
                    double dx = badges[b].X - badges[a].X, dy = badges[b].Y - badges[a].Y, d = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
                    if (d < BadgeR * 2 + 6)
                    {
                        double k = (BadgeR * 2 + 6 - d) / 2 / d;
                        badges[a] = new Point(badges[a].X - dx * k, badges[a].Y - dy * k);
                        badges[b] = new Point(badges[b].X + dx * k, badges[b].Y + dy * k);
                        moved = true;
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }

        var face = new Typeface("Segoe UI");
        var faceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Text(string text, double size, Brush brush, Typeface? t = null, double maxWidth = 0)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, t ?? face, size, brush, 1.0);
            if (maxWidth > 0)
            {
                ft.MaxTextWidth = maxWidth;
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }

            return ft;
        }

        // the list of the spots, to know its height
        const double RowGap = 14, LineH = 25;
        double legendY = 118;
        foreach (var sp in plan.Spots)
        {
            legendY += sp.Lines.Count * LineH + RowGap + (sp.Unsafe ? LineH - 6 : 0);
        }

        legendY += 120;
        int height = withLegend ? Math.Max(Math.Max(mapH, (int)legendY), 520) : mapH;
        int width = mapW + (withLegend ? LegendWidth : 0);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Back), null, new Rect(0, 0, width, height));

            // the game's map
            int c0 = (int)Math.Floor(x0 / map.Tile), c1 = (int)Math.Floor((x1 - 1) / map.Tile), r0 = (int)Math.Floor(y0 / map.Tile), r1 = (int)Math.Floor((y1 - 1) / map.Tile);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, mapW, mapH)));
            for (int col = c0; col <= c1; col++)
            {
                for (int row = r0; row <= r1; row++)
                {
                    string file = Path.Combine(Aion2Maps.TileFolder(map), $"{col}_{row}.jpg");
                    if (File.Exists(file))
                    {
                        var tile = Decode(file, (int)Math.Round(map.Tile * f));
                        dc.DrawImage(tile, new Rect((col * map.Tile - x0) * f, (row * map.Tile - y0) * f, map.Tile * f + 0.5, map.Tile * f + 0.5));
                    }
                }
            }

            // the dangers: the zone of a guard, the guards, the elite and hero monsters
            var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x4A, 0x4A));
            var orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x2E));
            var zonePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0xFF, 0x4A, 0x4A)), 1.5) { DashStyle = DashStyles.Dash };
            double zone = Aion2.Aion2Guards.ZoneMetres * 100 * s * f;
            foreach (var g in plan.Guards)
            {
                var p = At(g.X, g.Y);
                if (p.X >= -zone && p.Y >= -zone && p.X <= mapW + zone && p.Y <= mapH + zone)
                {
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(28, 0xFF, 0x4A, 0x4A)), zonePen, p, zone, zone);
                }
            }

            var crossRed = new Pen(red, 2.4);
            var crossOrange = new Pen(orange, 2);
            foreach (var g in plan.Guards)
            {
                Cross(dc, At(g.X, g.Y), 6, crossRed);
            }

            foreach (var e in plan.Elites)
            {
                Cross(dc, At(e.X, e.Y), 4.5, crossOrange);
            }

            // the places of the missing pets
            var dot = new SolidColorBrush(Color.FromArgb(215, 0x6E, 0xC8, 0xFF));
            var dotPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 0.8);
            foreach (var p in plan.Points)
            {
                dc.DrawEllipse(dot, dotPen, At(p.X, p.Y), 3.2, 3.2);
            }

            // the spots
            var goldBrush = new SolidColorBrush(Gold);
            for (int i = 0; i < plan.Spots.Count; i++)
            {
                var sp = plan.Spots[i];
                double r = Math.Max(34, sp.Radius * s * f);
                var ring = sp.Unsafe ? new Pen(red, 3) { DashStyle = DashStyles.Dash } : new Pen(goldBrush, 3);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(48, Gold.R, Gold.G, Gold.B)), ring, places[i], r, r);
            }

            for (int i = 0; i < plan.Spots.Count; i++)
            {
                if ((badges[i] - places[i]).Length > 2)
                {
                    dc.DrawLine(new Pen(Brushes.Black, 2.5), places[i], badges[i]);
                }

                Badge(dc, badges[i], BadgeR, plan.Spots[i].Number.ToString(CultureInfo.InvariantCulture), plan.Spots[i].Unsafe, faceBold);
            }

            dc.Pop();

            if (withLegend)
            {
                // the list beside the map
                dc.DrawRectangle(new SolidColorBrush(PanelBack), null, new Rect(mapW, 0, LegendWidth, height));
                double lx = mapW + 28, y = 22;
                dc.DrawText(Text(title, 34, goldBrush, faceBold, LegendWidth - 56), new Point(lx, y));
                y += 48;
                dc.DrawText(Text(subtitle, 17, new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), face, LegendWidth - 56), new Point(lx, y));
                y += 40;
                var white = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6));
                var grey = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2));
                foreach (var sp in plan.Spots)
                {
                    Badge(dc, new Point(lx + 17, y + 17), 17, sp.Number.ToString(CultureInfo.InvariantCulture), sp.Unsafe, faceBold, 20);
                    double ty = y;
                    foreach (var line in sp.Lines)
                    {
                        string status = line.Missing ? loc["PetShop.Missing"] : string.Format(loc["PetFarm.Level"], line.Level) + $" · {line.Progress}/{Aion2Pets.ProgressNeeded(line.Level)}";
                        var right = Text(status, 17, line.Missing ? red : grey);
                        dc.DrawText(Text(string.Equals(line.Mob, line.Pet, StringComparison.OrdinalIgnoreCase) ? $"{line.Mob}  ×{line.Count}" : $"{line.Mob}  ({line.Pet})  ×{line.Count}", 19, white, faceBold, LegendWidth - 56 - 44 - right.Width - 12), new Point(lx + 44, ty));
                        dc.DrawText(right, new Point(mapW + LegendWidth - 28 - right.Width, ty + 2));
                        ty += LineH;
                    }

                    if (sp.Unsafe)
                    {
                        dc.DrawText(Text(loc["Settings.FarmMap.Img.Unsafe"], 15, red, face, LegendWidth - 100), new Point(lx + 44, ty - 4));
                        ty += LineH - 6;
                    }

                    y = ty + RowGap;
                }

                // what the signs mean
                y = Math.Max(y + 6, height - 92);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x3C, 0x4A)), 1), new Point(lx, y), new Point(mapW + LegendWidth - 28, y));
                y += 12;
                dc.DrawEllipse(dot, dotPen, new Point(lx + 8, y + 12), 5, 5);
                dc.DrawText(Text(loc["Settings.FarmMap.Img.Pets"], 16, grey), new Point(lx + 26, y));
                y += 26;
                Cross(dc, new Point(lx + 8, y + 12), 6, new Pen(red, 2.4));
                dc.DrawText(Text(plan.Guards.Count > 0 ? loc["Settings.FarmMap.Img.Guards"] : loc["Settings.FarmMap.Img.NoGuards"], 16, grey), new Point(lx + 26, y));
                y += 26;
                Cross(dc, new Point(lx + 8, y + 12), 5, new Pen(orange, 2));
                dc.DrawText(Text(loc["Settings.FarmMap.Img.Elite"], 16, grey), new Point(lx + 26, y));
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Cross(DrawingContext dc, Point p, double size, Pen pen)
    {
        dc.DrawLine(pen, new Point(p.X - size, p.Y - size), new Point(p.X + size, p.Y + size));
        dc.DrawLine(pen, new Point(p.X - size, p.Y + size), new Point(p.X + size, p.Y - size));
    }

    private static void Badge(DrawingContext dc, Point center, double radius, string number, bool isUnsafe, Typeface face, double fontSize = 0)
    {
        dc.DrawEllipse(new SolidColorBrush(Gold), new Pen(isUnsafe ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4A, 0x4A)) : Brushes.Black, 2.5), center, radius, radius);
        var ft = new FormattedText(number, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fontSize > 0 ? fontSize : radius * 1.25, Brushes.Black, 1.0);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}
