using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One circle of the assist: a skill on cooldown, a follow-up skill the game has opened, or an effect on the character.</summary>
/// <param name="Key">See <see cref="AssistKeys"/>.</param>
/// <param name="Seconds">Time left; whole seconds are shown. Not used for an endless effect.</param>
/// <param name="Fraction">How much of the full time is left (1 = just started), for the ring.</param>
/// <param name="Endless">An effect without an end, or a renewed aura.</param>
public sealed record AssistItem(int Key, double Seconds, double Fraction, bool Endless = false)
{
    public int SkillId => AssistKeys.SkillOf(Key);

    public bool Group => AssistKeys.IsGroup(Key);
}

/// <summary>
/// The content of the assist: the local player's skill cooldowns (frame 0x4738, see <see cref="Aion2Cooldowns"/>) and the effects running on the
/// character (frames 0x2a38 / 0x2b38 / 0x2c38, see <see cref="Aion2CombatState.BuffsOn"/>) as a field of circles - the skill's icon, a ring that runs down like a
/// clock and the seconds left at its lower right corner, no names. An effect that another player put on you has a blue ring, your own a ring in the theme's accent.
/// The circles run in rows or columns (<see cref="Vertical"/>) and go on in the next row or column when the room is used up. Used by the overlay, the dashboard
/// widget and the preview in the assist window.
/// </summary>
public sealed class AssistPanel : WrapPanel
{
    /// <summary>Skill ids from here are real skills; the small ids are shared cooldown groups.</summary>
    private const int FirstSkillId = 10_000_000;

    private const double Size = 56;
    private static readonly Color GroupColor = Color.FromRgb(0x5C, 0xC8, 0xFF);
    private static readonly Color FallbackText = Colors.White;
    private static readonly Color FallbackAccent = Color.FromRgb(0xFF, 0xD2, 0x7A);

    private readonly Dictionary<string, BitmapImage?> _images = new();

    /// <summary>The highest number seen per skill since it last was ready: what the ring of a running cooldown is measured against.</summary>
    private readonly Dictionary<int, int> _peak = new();

    /// <summary>How long an effect that is gone stays (so a renewed one does not make its circle jump away and back).</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2.5);

    private sealed class EffectRow(int order)
    {
        public int Order { get; } = order;
        public DateTime LastActive { get; set; }
        public BuffNotice? Buff { get; set; }

        /// <summary>How often the end of the effect moved forward while it was still running (a renewal).</summary>
        public int Renewals { get; set; }

        /// <summary>A short effect that is renewed again and again - a Chanter's mantra lasts 2.4 s and is renewed every second for as long as the Chanter keeps it up. In effect permanent.</summary>
        public bool Aura { get; set; }
    }

    private readonly Dictionary<int, EffectRow> _effects = new();
    private int _order;
    private Point _dragStart;

    /// <summary>Keys the player does not want shown (see <see cref="MeterSettings.AssistOverlayHidden"/>).</summary>
    public HashSet<int> Hidden { get; set; } = new();

    /// <summary>The font colour the player chose in the overlay designer (as "#RRGGBB"); null: the theme's text colour.</summary>
    public string? FontColor { get; set; }

    /// <summary>The player's order (see <see cref="MeterSettings.AssistOrder"/>).</summary>
    public IReadOnlyList<int> Order { get; set; } = Array.Empty<int>();

    /// <summary>The circles with the least time left first (renewed auras, which never run out, last), instead of the player's order.</summary>
    public bool SortByTime { get; set; }

    /// <summary>Columns instead of rows.</summary>
    public bool Vertical
    {
        get => Orientation == Orientation.Vertical;
        set => Orientation = value ? Orientation.Vertical : Orientation.Horizontal;
    }

    /// <summary>Set: the circles can be dragged by hand; called with the dragged key, the key it was dropped at and whether it went behind that one.</summary>
    public Action<int, int, bool>? Reorder { get; set; }

    /// <summary>Called with the keys of everything running, to keep the lists of the assist window complete.</summary>
    public Action<IReadOnlyList<int>>? Seen { get; set; }

    /// <summary>Whether the last <see cref="Refresh"/> had a circle to show.</summary>
    public bool HasContent { get; private set; }

    public AssistPanel()
    {
        Orientation = Orientation.Horizontal;
        ItemHeight = double.NaN;
    }

    public void Refresh(Aion2EntityDirectory? dir) => Show(Collect(dir));

    /// <summary>Shows the circles in the player's order (or by time left), without the hidden ones.</summary>
    public void Show(IReadOnlyList<AssistItem> items)
    {
        Children.Clear();
        IEnumerable<AssistItem> shown = items.Where(i => !Hidden.Contains(i.Key)).GroupBy(i => i.Key).Select(g => g.First());
        if (SortByTime)
        {
            shown = shown.OrderBy(i => i.Endless ? 1 : 0).ThenBy(i => i.Endless ? 0 : i.Seconds);
        }
        else
        {
            var order = AssistKeys.Ordered(shown.Select(i => i.Key), Order);
            shown = shown.OrderBy(i => order.IndexOf(i.Key));
        }

        HasContent = false;
        foreach (AssistItem item in shown)
        {
            Children.Add(Slot(item));
            HasContent = true;
        }
    }

    /// <summary>Everything that is running now: cooldowns, follow-up skills, effects.</summary>
    public IReadOnlyList<AssistItem> Collect(Aion2EntityDirectory? dir)
    {
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        var result = new List<AssistItem>();

        (IReadOnlyList<CooldownEntry> entries, DateTime at) = Aion2Cooldowns.Last;
        foreach (CooldownEntry e in entries)
        {
            if (e.Id < FirstSkillId || Aion2Artwork.IsPassive(e.Id))
            {
                continue;
            }

            // the time left from the speed this skill's own numbers fall at (a stone, a stat or a Daevanion node may change it); still an estimate: the speed varies a little during a cooldown
            (double left, double rate, _) = Aion2Cooldowns.Left(e.Id, e.Remaining, at, now);
            if (e.Remaining > 0 && left > 0)
            {
                _peak[e.Id] = Math.Max(_peak.GetValueOrDefault(e.Id), e.Remaining);
                result.Add(new AssistItem(AssistKeys.Make(e.Id, false), left, Math.Clamp(left * rate / _peak[e.Id], 0, 1)));
            }
            else
            {
                _peak.Remove(e.Id);
            }
        }

        // follow-up skills the game has just opened (chain skills, 3 s): the best moment to press them
        foreach ((int skill, double left) in Aion2Chains.Available(now))
        {
            result.Add(new AssistItem(AssistKeys.Make(skill, false), left, Math.Clamp(left / Aion2Chains.Window.TotalSeconds, 0, 1)));
        }

        // effects on the local player; one that another player put on you is the group's
        if (dir is not null && dir.LocalPlayerId is >= 0 and int self)
        {
            // one circle per effect: a short effect that is renewed all the time (the Chanter's mantras) gets a new instance or a new end every few
            // seconds, so circles are kept in the order they first appeared and stay a moment after their end instead of vanishing and coming back
            foreach (var group in dir.Combat.BuffsOn(self, now).GroupBy(b => AssistKeys.Make(b.SkillId, b.Caster > 0 && b.Caster != self)))
            {
                int skill = AssistKeys.SkillOf(group.Key);
                if (Aion2Artwork.IsPassiveEffect(skill))
                {
                    continue; // a passive skill's standing effect (Identify Weakness, weapon training ...) is not something put on you
                }

                string english = Aion2SkillNames.NameOf(skill);
                if (System.Text.RegularExpressions.Regex.IsMatch(english, @"^Test\d*_"))
                {
                    continue; // an internal test state of the game's data (Test1_Greatsword_Maintarget_MaxLv1), not an effect the player knows
                }

                if (long.TryParse(english, out _) && Aion2SkillIcons.PathFor(skill) is null && Aion2Artwork.EffectIconPath(skill) is null)
                {
                    continue; // a nameless state (rest, speed ...) says nothing to the player
                }

                BuffNotice best = group.OrderBy(b => b.Ends is null ? DateTime.MaxValue : b.Ends.Value).Last();
                if (!_effects.TryGetValue(group.Key, out var known))
                {
                    known = new EffectRow(++_order);
                    _effects[group.Key] = known;
                }

                // a renewal: the end moved forward while the effect was still running. Renewed twice, a short effect is an aura.
                (known.Renewals, known.Aura) = EffectAura.Next(known.Buff?.Ends, best.Ends, now, best.DurationMs, known.Renewals, known.Aura);
                known.LastActive = now;
                known.Buff = best;
            }

            foreach (var (key, row) in _effects.OrderBy(kv => kv.Value.Order).ToList())
            {
                if (now - row.LastActive > TimeSpan.FromSeconds(30))
                {
                    _effects.Remove(key);
                    continue;
                }

                if (now - row.LastActive > Linger || row.Buff is not { } b)
                {
                    continue;
                }

                bool active = row.LastActive == now;
                bool endless = row.Aura || b.Ends is null;
                double seconds = !active ? 0 : endless ? 0 : Math.Max(0, (b.Ends!.Value - now).TotalSeconds);
                double fraction = active && !endless && b.Ends is { } e2 && b.DurationMs > 0 ? Math.Clamp((e2 - now).TotalMilliseconds / b.DurationMs, 0, 1) : 0;
                result.Add(new AssistItem(key, seconds, fraction, active && endless));
            }
        }

        Seen?.Invoke(result.Select(r => r.Key).ToList());
        return result;
    }

    /// <summary>Circles for the preview while nothing is running, all real buffs of the game's data: a Gladiator's own (Armor of Balance, Zikel's Blessing, Survival Stance) and the group's (a Chanter's mantras, a Cleric's prayer).</summary>
    public static IReadOnlyList<AssistItem> Sample() => new[]
    {
        new AssistItem(AssistKeys.Make(11130050, false), 39, 0.7),
        new AssistItem(AssistKeys.Make(11250000, false), 21, 0.45),
        new AssistItem(AssistKeys.Make(11270000, false), 0, 0, true),
        new AssistItem(AssistKeys.Make(18160050, true), 33, 0.8),
        new AssistItem(AssistKeys.Make(18190050, true), 12, 0.35),
        new AssistItem(AssistKeys.Make(17430010, true), 6, 0.2),
    };

    /// <summary>What the lists of the assist window offer while the game never ran: the preview's circles and a few more.</summary>
    public static IReadOnlyList<int> SampleCatalog() => Sample().Select(i => i.Key).Concat(new[]
    {
        AssistKeys.Make(11400050, false), AssistKeys.Make(11410050, false), AssistKeys.Make(17410050, true), AssistKeys.Make(18420050, true),
    }).Distinct().ToList();

    /// <summary>"4" (3.4 s rounds up), "99", "2m" from 100 s on, "∞" for an endless effect.</summary>
    internal static string Number(AssistItem item)
    {
        if (item.Endless)
        {
            return "∞";
        }

        int s = (int)Math.Ceiling(item.Seconds);
        return s >= 100 ? ((int)Math.Ceiling(s / 60.0)).ToString(CultureInfo.InvariantCulture) + "m" : s.ToString(CultureInfo.InvariantCulture);
    }

    private static Color ThemeColor(string resource, Color fallback) =>
        Application.Current?.TryFindResource(resource) is SolidColorBrush brush ? brush.Color : fallback;

    private static Point OnCircle(double cx, double cy, double r, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        return new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
    }

    private UIElement Slot(AssistItem item)
    {
        double s = Size;
        double c = s / 2;
        double ring = 3.5;
        double iconR = c - ring - 1.5;
        Color accent = item.Group ? GroupColor : ThemeColor("Brush.Accent", FallbackAccent);
        var grid = new Grid { Width = s, Height = s, Margin = new Thickness(4), Background = Brushes.Transparent, Tag = item.Key };

        // the icon, round
        BitmapImage? picture = Picture(Aion2SkillIcons.PathFor(item.SkillId) ?? Aion2Artwork.EffectIconPath(item.SkillId));
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = iconR * 2, Height = iconR * 2,
            Fill = picture is null ? new SolidColorBrush(Color.FromRgb(0x20, 0x30, 0x3C)) : new ImageBrush(picture) { Stretch = Stretch.UniformToFill },
        });

        double fraction = item.Endless ? 1 : Math.Clamp(item.Fraction, 0, 1);

        // the clock: what is left is dark over the icon, shrinking clockwise
        if (!item.Endless && fraction > 0 && fraction < 0.999)
        {
            double start = (1 - fraction) * 360;
            var fig = new PathFigure { StartPoint = new Point(c, c), IsClosed = true };
            fig.Segments.Add(new LineSegment(OnCircle(c, c, iconR, start), false));
            fig.Segments.Add(new ArcSegment(OnCircle(c, c, iconR, 360), new Size(iconR, iconR), 0, fraction > 0.5, SweepDirection.Clockwise, false));
            grid.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { fig }), Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), IsHitTestVisible = false,
                Width = s, Height = s, Stretch = Stretch.None,
            });
        }
        else if (!item.Endless && fraction >= 0.999)
        {
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = iconR * 2, Height = iconR * 2, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)) });
        }

        // the ring: dim track, and the time left in the accent colour
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = s - ring, Height = s - ring, StrokeThickness = ring, Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
        });
        double ringR = (s - ring) / 2;
        if (item.Endless || fraction >= 0.999)
        {
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = s - ring, Height = s - ring, StrokeThickness = ring, Stroke = new SolidColorBrush(accent) });
        }
        else if (fraction > 0)
        {
            double start = (1 - fraction) * 360;
            var fig = new PathFigure { StartPoint = OnCircle(c, c, ringR, start), IsClosed = false };
            fig.Segments.Add(new ArcSegment(OnCircle(c, c, ringR, 359.99), new Size(ringR, ringR), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            grid.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { fig }), Stroke = new SolidColorBrush(accent), StrokeThickness = ring,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = s, Height = s, Stretch = Stretch.None,
            });
        }

        // the number: the theme's text colour with an outline that stands out from it (dark round a light colour, light round a dark one)
        Color text = FontColor is { Length: > 0 } hex && ColorConverter.ConvertFromString(hex) is Color chosen ? chosen : ThemeColor("Brush.Text", FallbackText);
        bool lightText = (0.299 * text.R + 0.587 * text.G + 0.114 * text.B) > 140;
        Color outline = lightText ? Color.FromRgb(0x05, 0x0A, 0x0F) : Color.FromRgb(0xF4, 0xEC, 0xDA);
        UIElement number = OutlinedText(Number(item), 20, text, outline);
        if (number is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            fe.VerticalAlignment = VerticalAlignment.Bottom;
            fe.Margin = new Thickness(0, 0, -6, -6);
        }

        grid.Children.Add(number);
        if (Reorder is not null)
        {
            Interactive(grid, item.Key);
        }

        return grid;
    }

    /// <summary>Text drawn as an outlined shape: readable on every background without a frame behind it.</summary>
    private static UIElement OutlinedText(string text, double size, Color fill, Color outline)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
        Geometry geometry = formatted.BuildGeometry(new Point(0, 0));
        double w = formatted.Width + 6, h = formatted.Height + 6;
        var box = new Grid { Width = w, Height = h, IsHitTestVisible = false };
        var under = new System.Windows.Shapes.Path
        {
            Data = geometry, Stroke = new SolidColorBrush(outline), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, Width = w, Height = h, Stretch = Stretch.None,
            RenderTransform = new TranslateTransform(3, 3),
        };
        var over = new System.Windows.Shapes.Path
        {
            Data = geometry, Fill = new SolidColorBrush(fill), Width = w, Height = h, Stretch = Stretch.None, RenderTransform = new TranslateTransform(3, 3),
        };
        box.Children.Add(under);
        box.Children.Add(over);
        return box;
    }

    /// <summary>The circle can be picked up and dropped before or behind another one.</summary>
    private void Interactive(Grid slot, int key)
    {
        slot.Cursor = System.Windows.Input.Cursors.Hand;
        slot.AllowDrop = true;
        slot.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        slot.MouseMove += (_, e) =>
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                return;
            }

            Vector moved = e.GetPosition(this) - _dragStart;
            if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(slot, new DataObject("aiondps-assist-key", key), DragDropEffects.Move);
            }
        };
        slot.DragOver += (_, e) => e.Effects = e.Data.GetDataPresent("aiondps-assist-key") ? DragDropEffects.Move : DragDropEffects.None;
        slot.Drop += (_, e) =>
        {
            if (e.Data.GetData("aiondps-assist-key") is int moved && moved != key)
            {
                Point at = e.GetPosition(slot);
                bool after = Vertical ? at.Y > slot.ActualHeight / 2 : at.X > slot.ActualWidth / 2;
                Reorder?.Invoke(moved, key, after);
            }
        };
    }

    private BitmapImage? Picture(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (!_images.TryGetValue(path, out BitmapImage? image))
        {
            image = null;
            if (File.Exists(path))
            {
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.DecodePixelWidth = 96;
                image.EndInit();
                image.Freeze();
            }

            _images[path] = image;
        }

        return image;
    }
}
