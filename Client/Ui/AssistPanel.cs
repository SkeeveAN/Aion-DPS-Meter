using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The content of the assist: the local player's skill cooldowns (frame 0x4738, see <see cref="Aion2Cooldowns"/>) and the effects running on the
/// character (frames 0x2a38 / 0x2b38 / 0x2c38, see <see cref="Aion2CombatState.BuffsOn"/>). Shown in the assist window (theme colours, cards, ready skills too)
/// and in the assist overlay (fixed light-on-dark colours, running cooldowns and effects only).
/// </summary>
public sealed class AssistPanel : StackPanel
{
    /// <summary>The cooldown number falls about 2.3 to 2.5 times as fast as the clock (docs/protocol/cooldown-frames.md); the time shown is the number divided by this, hence the "~".</summary>
    private const double CooldownRate = 2.4;

    /// <summary>Skill ids from here are real skills; the small ids are shared cooldown groups.</summary>
    private const int FirstSkillId = 10_000_000;

    private static readonly Brush OverlayText = Brushes.White;
    private static readonly Brush OverlayMuted = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2));
    private static readonly Brush OverlayAccent = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A));

    private readonly bool _overlay;
    private readonly Dictionary<string, BitmapImage?> _images = new();

    /// <summary>The highest number seen per skill since it last was ready: what the bar of a running cooldown is measured against.</summary>
    private readonly Dictionary<int, int> _peak = new();

    /// <summary>How long an effect that is gone stays in the list (so a renewed one does not make its row jump away and back).</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2.5);

    private sealed class EffectRow(int order)
    {
        public int Order { get; } = order;
        public DateTime LastActive { get; set; }
        public BuffNotice? Buff { get; set; }
    }

    private readonly Dictionary<int, EffectRow> _effects = new();
    private int _order;

    public AssistPanel(bool overlay)
    {
        _overlay = overlay;
    }

    /// <summary>Whether the last <see cref="Refresh"/> had a running cooldown or an effect to show.</summary>
    public bool HasContent { get; private set; }

    public void Refresh(Aion2EntityDirectory? dir)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Children.Clear();
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock

        // cooldowns: running ones first (the least left on top), then the ready skills
        (IReadOnlyList<CooldownEntry> entries, DateTime at) = Aion2Cooldowns.Last;
        double sinceList = at == default ? 0 : Math.Max(0, (now - at).TotalSeconds);
        var running = new List<(int Id, double Seconds, double Fraction)>();
        var ready = new List<int>();
        foreach (CooldownEntry e in entries)
        {
            if (e.Id < FirstSkillId || Aion2Artwork.IsPassive(e.Id))
            {
                continue;
            }

            double left = e.Remaining / CooldownRate / 1000.0 - sinceList;
            if (e.Remaining > 0 && left > 0)
            {
                _peak[e.Id] = Math.Max(_peak.GetValueOrDefault(e.Id), e.Remaining);
                running.Add((e.Id, left, Math.Clamp(left * CooldownRate * 1000.0 / _peak[e.Id], 0, 1)));
            }
            else
            {
                _peak.Remove(e.Id);
                if (Aion2SkillNames.NameOf(e.Id) is { Length: > 0 } n && !long.TryParse(n, out _))
                {
                    ready.Add(e.Id);
                }
            }
        }

        var cooldowns = new StackPanel();
        cooldowns.Children.Add(Heading(loc["Assist.Cooldowns"]));
        if (running.Count == 0 && (_overlay || ready.Count == 0))
        {
            if (!_overlay)
            {
                var empty = Label(loc["Assist.NoCooldowns"], "Brush.TextMuted", 12);
                empty.TextWrapping = TextWrapping.Wrap;
                cooldowns.Children.Add(empty);
            }
        }

        foreach (var r in running.OrderBy(r => r.Seconds))
        {
            cooldowns.Children.Add(Row(r.Id, Aion2SkillNames.DisplayOf(r.Id), "~" + Seconds(r.Seconds), r.Fraction, false));
        }

        if (!_overlay)
        {
            foreach (int id in ready.OrderBy(i => i))
            {
                cooldowns.Children.Add(Row(id, Aion2SkillNames.DisplayOf(id), loc["Assist.Ready"], 0, true));
            }
        }

        // effects on the local player
        var buffs = new StackPanel();
        buffs.Children.Add(Heading(loc["Assist.Buffs"]));
        int shown = 0;
        if (dir is not null && dir.LocalPlayerId is >= 0 and int self)
        {
            // one row per effect: a short effect that is renewed all the time (the Chanter's mantras) gets a new instance or a new end every few
            // seconds, so rows are kept in the order they first appeared and stay a moment after their end instead of vanishing and coming back
            foreach (var group in dir.Combat.BuffsOn(self, now).GroupBy(b => b.SkillId))
            {
                if (Aion2Artwork.IsPassiveEffect(group.Key))
                {
                    continue; // a passive skill's standing effect (Identify Weakness, weapon training ...) is not something put on you
                }

                string english = Aion2SkillNames.NameOf(group.Key);
                if (long.TryParse(english, out _) && Aion2SkillIcons.PathFor(group.Key) is null)
                {
                    continue; // a nameless state (rest, speed ...) says nothing to the player
                }

                BuffNotice best = group.OrderBy(b => b.Ends is null ? DateTime.MaxValue : b.Ends.Value).Last();
                if (!_effects.TryGetValue(group.Key, out var known))
                {
                    known = new EffectRow(++_order);
                    _effects[group.Key] = known;
                }

                known.LastActive = now;
                known.Buff = best;
            }

            foreach (var (skill, row) in _effects.OrderBy(kv => kv.Value.Order).ToList())
            {
                if (now - row.LastActive > TimeSpan.FromSeconds(30))
                {
                    _effects.Remove(skill);
                    continue;
                }

                if (now - row.LastActive > Linger || row.Buff is not { } b)
                {
                    continue;
                }

                bool active = row.LastActive == now;
                string time = !active ? Seconds(0) : b.Ends is { } end ? Seconds(Math.Max(0, (end - now).TotalSeconds)) : "\u221E";
                double fraction = active && b.Ends is { } e2 && b.DurationMs > 0 ? Math.Clamp((e2 - now).TotalMilliseconds / b.DurationMs, 0, 1) : 0;
                buffs.Children.Add(Row(skill, Aion2SkillNames.DisplayOf(skill), time, fraction, false));
                shown++;
            }
        }

        if (shown == 0 && !_overlay)
        {
            buffs.Children.Add(Label(loc["Assist.NoBuffs"], "Brush.TextMuted", 12));
        }

        HasContent = running.Count > 0 || shown > 0;
        if (_overlay)
        {
            if (running.Count > 0)
            {
                Children.Add(cooldowns);
            }

            if (shown > 0)
            {
                buffs.Margin = new Thickness(0, running.Count > 0 ? 6 : 0, 0, 0);
                Children.Add(buffs);
            }

            return;
        }

        Children.Add(Card(cooldowns));
        Children.Add(Card(buffs));
        var note = Label(loc["Assist.Note"], "Brush.TextSubtle", 11);
        note.TextWrapping = TextWrapping.Wrap;
        Children.Add(note);
    }

    private static string Seconds(double s) =>
        s >= 60 ? $"{(int)(s / 60)}:{(int)(s % 60):00}" : s >= 10 ? ((int)Math.Ceiling(s)).ToString(CultureInfo.CurrentCulture) + " s" : s.ToString("0.0", CultureInfo.CurrentCulture) + " s";

    private TextBlock Heading(string text)
    {
        TextBlock block = Label(text, "Brush.Text", _overlay ? 11 : 14, bold: true);
        block.Margin = new Thickness(0, 0, 0, _overlay ? 2 : 4);
        return block;
    }

    private UIElement Row(int skillId, string name, string time, double fraction, bool ready)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double size = _overlay ? 24 : 28;
        var icon = new Image { Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0), Source = Picture(Aion2SkillIcons.PathFor(skillId)) };
        Grid.SetRowSpan(icon, 2);
        grid.Children.Add(icon);

        TextBlock label = Label(name, ready ? "Brush.TextSubtle" : "Brush.Text", _overlay ? 12.5 : 13);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        TextBlock value = Label(time, ready ? "Brush.TextSubtle" : "Brush.Accent", _overlay ? 12.5 : 13, bold: !ready);
        value.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        if (fraction > 0)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = fraction, Height = 4, Margin = new Thickness(0, 2, 0, 0), BorderThickness = new Thickness(0) };
            if (_overlay)
            {
                bar.Foreground = OverlayAccent;
                bar.Background = new SolidColorBrush(Color.FromArgb(0x60, 0x29, 0x45, 0x57));
            }
            else
            {
                bar.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
                bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
            }

            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, 1);
            Grid.SetColumnSpan(bar, 2);
            grid.Children.Add(bar);
        }

        return grid;
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
                image.DecodePixelWidth = 56;
                image.EndInit();
                image.Freeze();
            }

            _images[path] = image;
        }

        return image;
    }

    private TextBlock Label(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (_overlay)
        {
            block.Foreground = brush switch { "Brush.Accent" => OverlayAccent, "Brush.Text" => OverlayText, _ => OverlayMuted };
        }
        else
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        return block;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 6), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }
}
