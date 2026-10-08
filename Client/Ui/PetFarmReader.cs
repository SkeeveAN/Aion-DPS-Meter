using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using AionDPS.Aion2.Protocol;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;

namespace AionDPS.Ui;

/// <summary>
/// Reads the name on the game's target plate from a picture of the game window: the plate of the targeted monster has a red hit-point
/// bar (255,55,84) with the name in the same red above it. The bar is found by its colour, the text above it is cut out, reduced to
/// the red lettering (black on white, enlarged) and handed to the Windows text recognition. The picture is only ever held in memory.
/// </summary>
public sealed class PetFarmReader
{
    private OcrEngine? _engine;
    private string? _engineLanguage;

    /// <summary>The name read from the target plate, or null when no plate is on screen.</summary>
    public async Task<string?> ReadTargetNameAsync(Bitmap frame, string language)
    {
        foreach (Rectangle bar in FindBars(frame).Take(2))
        {
            using Bitmap text = PrepareText(frame, bar);
            string? line = await RecognizeAsync(text, language);
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>
    /// The pet monster a recognised line stands for: the monster names the pet table knows (in any language), compared without case,
    /// accents or punctuation; one wrong letter per eight is tolerated, because the recognition is not perfect on small red lettering.
    /// </summary>
    public static (string Name, IReadOnlyCollection<int> Pets)? Match(string recognised)
    {
        string folded = Aion2Pets.Fold(recognised);
        if (folded.Length < 4)
        {
            return null;
        }

        var exact = Aion2Pets.PetsOfMonsterName(recognised);
        if (exact.Count > 0)
        {
            return (recognised, exact);
        }

        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string known in Aion2Pets.KnownMonsterNames)
        {
            if (Math.Abs(known.Length - folded.Length) > 2)
            {
                continue;
            }

            int allowed = Math.Max(1, known.Length / 8);
            int d = Distance(folded, known, allowed);
            if (d <= allowed && d < bestDistance)
            {
                best = known;
                bestDistance = d;
            }
        }

        return best is null ? null : (best, Aion2Pets.PetsOfMonsterName(best));
    }

    private static int Distance(string a, string b, int limit)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                rowMin = Math.Min(rowMin, cur[j]);
            }

            if (rowMin > limit)
            {
                return limit + 1;
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }

    private static bool IsBarRed(byte r, byte g, byte b) => r >= 235 && g is >= 35 and <= 80 && b is >= 60 and <= 110;

    private static bool IsTextRed(byte r, byte g, byte b) => r > 190 && g < 150 && b < 150 && r - g > 70;

    /// <summary>The red bars of target plates in the picture, widest first: runs of the bar colour at least 40 pixels wide and 4 rows tall.</summary>
    public static List<Rectangle> FindBars(Bitmap frame)
    {
        int w = frame.Width, h = frame.Height;
        var data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        int stride = data.Stride;
        frame.UnlockBits(data);

        var open = new List<Rectangle>(); // runs continued from the row above
        var done = new List<Rectangle>();
        for (int y = 0; y < h; y++)
        {
            var runs = new List<(int X0, int X1)>();
            int start = -1;
            for (int x = 0; x <= w; x++)
            {
                bool red = x < w && IsBarRed(px[y * stride + x * 4 + 2], px[y * stride + x * 4 + 1], px[y * stride + x * 4]);
                if (red && start < 0)
                {
                    start = x;
                }
                else if (!red && start >= 0)
                {
                    if (x - start >= 40)
                    {
                        runs.Add((start, x));
                    }

                    start = -1;
                }
            }

            var next = new List<Rectangle>();
            foreach ((int x0, int x1) in runs)
            {
                int match = open.FindIndex(r => Math.Abs(r.Left - x0) <= 3 && Math.Abs(r.Right - x1) <= 3 && r.Bottom == y);
                if (match >= 0)
                {
                    Rectangle r = open[match];
                    open.RemoveAt(match);
                    next.Add(new Rectangle(r.X, r.Y, r.Width, r.Height + 1));
                }
                else
                {
                    next.Add(new Rectangle(x0, y, x1 - x0, 1));
                }
            }

            done.AddRange(open); // not continued: finished
            open = next;
        }

        done.AddRange(open);
        return done.Where(r => r.Height >= 4 && r.Height <= 24).OrderByDescending(r => r.Width).ToList();
    }

    /// <summary>The text above a bar as black lettering on white, enlarged three times (the recognition does better on large, clean text).</summary>
    public static Bitmap PrepareText(Bitmap frame, Rectangle bar)
    {
        int margin = Math.Max(40, bar.Width / 2);
        int x0 = Math.Max(0, bar.Left - margin), x1 = Math.Min(frame.Width, bar.Right + margin);
        int y1 = Math.Max(0, bar.Top - 1), y0 = Math.Max(0, bar.Top - 44);
        int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        const int scale = 3;
        var src = frame.LockBits(new Rectangle(x0, y0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] from = new byte[src.Stride * h];
        Marshal.Copy(src.Scan0, from, 0, from.Length);
        int fromStride = src.Stride;
        frame.UnlockBits(src);

        var result = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb);
        var dst = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] to = new byte[dst.Stride * result.Height];
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                int i = y / scale * fromStride + x / scale * 4;
                byte v = IsTextRed(from[i + 2], from[i + 1], from[i]) ? (byte)0 : (byte)255;
                int o = y * dst.Stride + x * 4;
                to[o] = to[o + 1] = to[o + 2] = v;
                to[o + 3] = 255;
            }
        }

        Marshal.Copy(to, 0, dst.Scan0, to.Length);
        result.UnlockBits(dst);
        return result;
    }

    private OcrEngine? Engine(string language)
    {
        if (_engine is not null && _engineLanguage == language)
        {
            return _engine;
        }

        string tag = language switch { "de" => "de-DE", "fr" => "fr-FR", "es" => "es-ES", "ru" => "ru-RU", _ => "en-US" };
        _engine = OcrEngine.TryCreateFromLanguage(new Language(tag)) ?? OcrEngine.TryCreateFromUserProfileLanguages();
        _engineLanguage = language;
        return _engine;
    }

    private async Task<string?> RecognizeAsync(Bitmap picture, string language)
    {
        OcrEngine? engine = Engine(language);
        if (engine is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        picture.Save(stream, ImageFormat.Bmp);
        stream.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).OrderByDescending(t => t.Length).FirstOrDefault();
    }
}
