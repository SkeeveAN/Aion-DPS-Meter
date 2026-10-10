using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The picture tiles of a world map (assets/aion2/maps/&lt;key&gt;/&lt;col&gt;_&lt;row&gt;.jpg with the mask &lt;col&gt;_&lt;row&gt;_a.png, which makes the sea transparent):
/// loaded, combined into one bitmap with an alpha channel, for the pet map overlay and the map cut-outs of the pet settings.
/// </summary>
internal static class MapTiles
{
    /// <summary>One tile as a bitmap with the mask as its alpha, decoded at <paramref name="decodeWidth"/> pixels; null when the tile does not exist.</summary>
    public static BitmapSource? Load(Aion2MapInfo map, int col, int row, int decodeWidth)
    {
        try
        {
            string folder = Aion2Maps.TileFolder(map);
            string jpg = Path.Combine(folder, $"{col}_{row}.jpg");
            string mask = Path.Combine(folder, $"{col}_{row}_a.png");
            if (!File.Exists(jpg) || !File.Exists(mask))
            {
                return null;
            }

            return Combine(Decode(jpg, decodeWidth), Decode(mask, decodeWidth));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    private static BitmapImage Decode(string path, int width)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = width;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>The whole map in one small picture (every tile at 64 pixels), for the overview corner of a cut-out.</summary>
    public static BitmapSource? Overview(Aion2MapInfo map)
    {
        const int cell = 64;
        var drawing = new DrawingVisual();
        bool any = false;
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 22, 30)), null, new System.Windows.Rect(0, 0, map.Grid * cell, map.Grid * cell));
            for (int col = 0; col < map.Grid; col++)
            {
                for (int row = 0; row < map.Grid; row++)
                {
                    if (Load(map, col, row, cell) is { } tile)
                    {
                        dc.DrawImage(tile, new System.Windows.Rect(col * cell, row * cell, cell, cell));
                        any = true;
                    }
                }
            }
        }

        if (!any)
        {
            return null;
        }

        var target = new RenderTargetBitmap(map.Grid * cell, map.Grid * cell, 96, 96, PixelFormats.Pbgra32);
        target.Render(drawing);
        target.Freeze();
        return target;
    }

    /// <summary>The colour picture with the mask as its alpha (the mask is stored at half size and stretched to the colour picture).</summary>
    private static BitmapSource Combine(BitmapSource color, BitmapSource mask)
    {
        var c = new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0);
        var m = new FormatConvertedBitmap(mask, PixelFormats.Gray8, null, 0);
        int w = c.PixelWidth, h = c.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        c.CopyPixels(pixels, w * 4, 0);
        byte[] alpha = new byte[m.PixelWidth * m.PixelHeight];
        m.CopyPixels(alpha, m.PixelWidth, 0);
        for (int y = 0; y < h; y++)
        {
            int my = Math.Min(m.PixelHeight - 1, y * m.PixelHeight / h);
            for (int x = 0; x < w; x++)
            {
                int mx = Math.Min(m.PixelWidth - 1, x * m.PixelWidth / w);
                int i = (y * w + x) * 4;
                byte a = alpha[my * m.PixelWidth + mx];
                pixels[i] = (byte)(pixels[i] * a / 255); // premultiplied alpha
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                pixels[i + 3] = a;
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        result.Freeze();
        return result;
    }
}
