using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>One cast the bar shows. <paramref name="Key"/> names the cast itself (for the voice output, so it speaks each cast once); <paramref name="All"/>: aimed at no one in particular or at someone else but hitting an area (not at you).</summary>
public sealed record CastRowData(
    string Name, string Detail, string TargetText, bool AtYou, bool Area, bool Lethal, double ElapsedMs, int CastMs, int? LandMs, string? IconPath, bool Generic = false, string Key = "", bool All = false);

/// <summary>The target of the target: who the clicked target is aiming at, with its hit points and effects (null/empty when the stream said nothing).</summary>
public sealed record TargetOfTargetData(string Name, string From, string? ImagePath, bool IsBoss, long? Hp = null, long? MaxHp = null, IReadOnlyList<EffectData>? Effects = null);

/// <summary>An effect on the healer's target.</summary>
public sealed record EffectData(string Label, string? IconPath, string Time, bool Debuff);

/// <summary>Pictures for the combat overlay's elements, read once.</summary>
internal static class OverlayPictures
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out ImageSource? known))
        {
            return known;
        }

        ImageSource? image = null;
        try
        {
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            image = null;
        }

        Cache[path] = image;
        return image;
    }

    /// <summary>Sizes (points at the standard 12 pt base) the overlay texts use; <see cref="ThemeManager"/> defines a scaled resource for each.</summary>
    public static readonly double[] TextSizes = { 9, 11, 13, 14, 15 };

    public static string SizeKey(double pt) => "FontSize.Overlay." + pt.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gives the text its size from the scaled resource, so the user's font size reaches it - live, in the overlay and the theme editor's preview.</summary>
    public static TextBlock Scaled(TextBlock text, double pt)
    {
        text.FontSize = pt;
        text.SetResourceReference(TextBlock.FontSizeProperty, SizeKey(pt));
        return text;
    }

    public static Brush Brush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static readonly Brush Panel = Brush(0x0E, 0x18, 0x20, 0xB4);
    public static readonly Brush PanelBorder = Brush(0x29, 0x45, 0x57, 0xB0);
    public static readonly Brush Text = Brush(0xF2, 0xF5, 0xF8);
    public static readonly Brush Muted = Brush(0x9D, 0xB3, 0xC2);
    public static readonly Brush Amber = Brush(0xFF, 0xBA, 0x42);
    public static readonly Brush HpGreen = Brush(0x1A, 0x5E, 0x30);
    public static readonly Brush HpRed = Brush(0x9E, 0x24, 0x2E);
    public static readonly Brush Red = Brush(0xFF, 0x5A, 0x5A);
    public static readonly Brush Green = Brush(0x5B, 0xD0, 0x7A);
    public static readonly Brush Track = Brush(0x1B, 0x2A, 0x36, 0xD8);
    public static readonly Brush TrackLand = Brush(0x4A, 0x1F, 0x24, 0xD8);

    public static Border Card(UIElement child) => new()
    {
        Background = Panel, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = child,
    };

    /// <summary>A plain emblem for something without a picture: a rounded square with a letter.</summary>
    public static FrameworkElement Emblem(string letter, double size, Brush? fill = null) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(4), Background = fill ?? Brush(0x2B, 0x3C, 0x4A), BorderBrush = PanelBorder, BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = letter, FontWeight = FontWeights.Bold, FontSize = size * 0.5, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
}

/// <summary>The boss cast bar: up to three rows, each a skill that is being cast with its name, whom it is aimed at and a bar that runs down.</summary>
public sealed class BossCastView : Border
{
    private readonly StackPanel _rows = new();
    private readonly List<CastRow> _views = new();

    public BossCastView()
    {
        Child = _rows;
    }

    public bool HasRows => _views.Any(v => v.Visibility == Visibility.Visible);

    public void Show(IReadOnlyList<CastRowData> rows)
    {
        while (_views.Count < rows.Count)
        {
            var view = new CastRow();
            _views.Add(view);
            _rows.Children.Add(view);
        }

        for (int i = 0; i < _views.Count; i++)
        {
            if (i < rows.Count)
            {
                _views[i].Update(rows[i]);
                _views[i].Visibility = Visibility.Visible;
            }
            else
            {
                _views[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    private sealed class CastRow : Border
    {
        private readonly Image _icon = new() { Width = 28, Height = 28, Stretch = Stretch.Uniform };
        private readonly ContentControl _iconHost = new() { Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _name = OverlayPictures.Scaled(new() { FontWeight = FontWeights.Bold, Foreground = OverlayPictures.Text, TextTrimming = TextTrimming.CharacterEllipsis }, 13);
        private readonly TextBlock _target = OverlayPictures.Scaled(new() { FontWeight = FontWeights.SemiBold, Foreground = OverlayPictures.Amber, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }, 13);
        private readonly TextBlock _detail = OverlayPictures.Scaled(new() { Foreground = OverlayPictures.Muted, TextTrimming = TextTrimming.CharacterEllipsis }, 11);
        private readonly TextBlock _tags = OverlayPictures.Scaled(new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }, 11);
        private readonly TextBlock _time = OverlayPictures.Scaled(new() { FontWeight = FontWeights.Bold, Foreground = OverlayPictures.Text, VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Right }, 15);
        private readonly ColumnDefinition _castPart = new(), _landPart = new();
        private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1) };
        private readonly Border _castTrack = new() { Background = OverlayPictures.Track };
        private readonly Border _landTrack = new() { Background = OverlayPictures.TrackLand };
        private readonly Border _edge = new() { BorderThickness = new Thickness(0) };

        public CastRow()
        {
            Margin = new Thickness(0, 0, 0, 4);
            Padding = new Thickness(6, 4, 6, 4);
            CornerRadius = new CornerRadius(5);
            Background = OverlayPictures.Panel;
            BorderThickness = new Thickness(1);
            BorderBrush = OverlayPictures.PanelBorder;

            _iconHost.Content = _icon;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // the skill's name on top in bold with its target in yellow right behind it (the tags at the right), the caster below
            var nameLine = new DockPanel();
            DockPanel.SetDock(_tags, Dock.Right);
            nameLine.Children.Add(_tags);
            var skillAndTarget = new StackPanel { Orientation = Orientation.Horizontal };
            skillAndTarget.Children.Add(_name);
            skillAndTarget.Children.Add(_target);
            nameLine.Children.Add(skillAndTarget);
            text.Children.Add(nameLine);
            text.Children.Add(_detail);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(_iconHost);
            Grid.SetColumn(text, 1);
            top.Children.Add(text);
            Grid.SetColumn(_time, 2);
            top.Children.Add(_time);

            var bar = new Grid { Height = 7, Margin = new Thickness(0, 4, 0, 0) };
            bar.ColumnDefinitions.Add(_castPart);
            bar.ColumnDefinitions.Add(_landPart);
            bar.Children.Add(_castTrack);
            Grid.SetColumn(_landTrack, 1);
            bar.Children.Add(_landTrack);
            Grid.SetColumnSpan(_fill, 2);
            bar.Children.Add(_fill);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(bar);
            Child = stack;
        }

        public void Update(CastRowData row)
        {
            // a skill with a name of its own leads (bold) with the caster below; a plain "Attack" gives the lead to the caster and sits below, small and not bold
            _name.Text = row.Generic ? row.Detail : row.Name;
            _name.FontWeight = FontWeights.Bold;
            _target.Text = "→ " + row.TargetText;
            _detail.Text = row.Generic ? row.Name : row.Detail;
            _icon.Source = OverlayPictures.Load(row.IconPath);
            _iconHost.Content = _icon.Source is null ? OverlayPictures.Emblem("!", 28) : _icon;

            var loc = LocalizationManager.Instance;
            _tags.Inlines.Clear();
            if (row.Lethal)
            {
                _tags.Inlines.Add(new System.Windows.Documents.Run(loc["Combat.Lethal"]) { Foreground = OverlayPictures.Red });
            }
            else if (row.Area)
            {
                _tags.Inlines.Add(new System.Windows.Documents.Run(loc["Combat.Area"]) { Foreground = OverlayPictures.Amber });
            }

            bool alert = row.AtYou || row.Lethal;
            BorderBrush = alert ? OverlayPictures.Red : OverlayPictures.PanelBorder;
            _fill.Background = alert ? OverlayPictures.Red : OverlayPictures.Amber;

            double cast = Math.Max(1, row.CastMs);
            double land = row.LandMs is int l ? l : 0;
            _castPart.Width = new GridLength(cast, GridUnitType.Star);
            _landPart.Width = new GridLength(Math.Max(0.0001, land), GridUnitType.Star);
            _landTrack.Visibility = land > 0 ? Visibility.Visible : Visibility.Collapsed;
            double total = cast + land;
            ((ScaleTransform)_fill.RenderTransform).ScaleX = Math.Clamp(row.ElapsedMs / total, 0, 1);
            double left = Math.Max(0, cast - row.ElapsedMs);
            _time.Text = (left / 1000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
            _time.Foreground = left < 500 ? OverlayPictures.Red : OverlayPictures.Text;
        }
    }
}

/// <summary>The target of the target is four parts, each a card of its own that can be placed and sized on its own: picture, name, hit point bar and effect bar.</summary>
public sealed class TargetIconView : Border
{
    private readonly Image _picture = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };

    public TargetIconView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(4);
        Child = _picture;
    }

    public void Show(TargetOfTargetData data)
    {
        ImageSource? image = OverlayPictures.Load(data.ImagePath);
        _picture.Source = image;
        Child = image is null ? OverlayPictures.Emblem(data.IsBoss ? "B" : "?", 34) : _picture;
    }
}

/// <summary>The name of the target of the target (one name: a boss's or a player's).</summary>
public sealed class TargetNameView : Border
{
    private readonly TextBlock _name = OverlayPictures.Scaled(new() { FontWeight = FontWeights.Bold, Foreground = OverlayPictures.Text, TextTrimming = TextTrimming.CharacterEllipsis }, 14);

    public TargetNameView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(6, 4, 6, 4);
        _name.VerticalAlignment = VerticalAlignment.Center;
        Child = _name;
    }

    public void Show(TargetOfTargetData data)
    {
        _name.Text = data.Name;
    }
}

/// <summary>A hit point bar on a card of its own (current, maximum and percent in the bar).</summary>
public sealed class HpBarView : Border
{
    private readonly TextBlock _text = OverlayPictures.Scaled(new()
    {
        FontWeight = FontWeights.SemiBold, Foreground = OverlayPictures.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1 },
    }, 11);
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(1, 1), CornerRadius = new CornerRadius(3), Background = OverlayPictures.HpRed };

    public HpBarView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(4);
        var bar = new Grid();
        bar.Children.Add(new Border { Background = OverlayPictures.Track, CornerRadius = new CornerRadius(3) });
        bar.Children.Add(_fill);
        // the numbers, the check and the fight time shrink to fit a narrow bar instead of being cut off at both ends
        bar.Children.Add(new Viewbox { Child = _text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Child = bar;
    }

    /// <summary>The boss's rage comes five minutes after the combat began (0x4b36): from 4:00 on the time turns yellow, from 4:40 red.</summary>
    public static readonly TimeSpan RageWarn = TimeSpan.FromSeconds(240);
    public static readonly TimeSpan RageSoon = TimeSpan.FromSeconds(280);
    private static readonly TimeSpan LongestShown = TimeSpan.FromHours(3);

    /// <summary>m:ss, or h:mm:ss from an hour on.</summary>
    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    public static Brush ElapsedBrush(TimeSpan elapsed) =>
        elapsed >= RageSoon ? OverlayPictures.Red : elapsed >= RageWarn ? OverlayPictures.Amber : OverlayPictures.Muted;

    /// <summary>Red; green while the target has debuffs on it. The numbers keep a dark edge so they read on either. <paramref name="elapsed"/>: the time since the
    /// fight began, behind the numbers (the bar of a boss only; null shows none).</summary>
    public void Show(long? hp, long? max, bool debuffed, string? check = null, TimeSpan? elapsed = null)
    {
        _fill.Background = debuffed ? OverlayPictures.HpGreen : OverlayPictures.HpRed;
        double fraction = hp is long h && max is long m && m > 0 ? Math.Clamp((double)h / m, 0, 1) : 0;
        ((ScaleTransform)_fill.RenderTransform).ScaleX = fraction;
        string numbers = hp is long current
            ? max is long total && total > 0 ? $"{current:N0} / {total:N0}  ({fraction * 100:0}%)" : $"{current:N0}"
            : "–";
        if (!string.IsNullOrEmpty(check))
        {
            numbers += "  " + check;
        }

        _text.Inlines.Clear();
        _text.Inlines.Add(new Run(numbers));
        if (elapsed is { } time && time >= TimeSpan.Zero && time <= LongestShown)
        {
            _text.Inlines.Add(new Run("  " + FormatElapsed(time)) { Foreground = ElapsedBrush(time) });
        }
    }
}

/// <summary>The effects (debuffs, buffs or both, as chosen) as a bar of small pictures with their time left, on a card of its own.</summary>
public sealed class EffectBarView : Border
{
    private readonly WrapPanel _effects = new();

    public EffectBarView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(4);
        Child = _effects;
    }

    public void Show(IReadOnlyList<EffectData> effects) => Fill(_effects, effects);

    internal static void Fill(WrapPanel panel, IReadOnlyList<EffectData> effects)
    {
        panel.Children.Clear();
        foreach (EffectData effect in effects.Take(16))
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 4, 2), Width = 26 };
            ImageSource? icon = OverlayPictures.Load(effect.IconPath);
            // a debuff the game has no picture for (most of a boss's own) gets a red marker with a down arrow
            FrameworkElement picture = icon is null
                ? effect.Debuff
                    ? OverlayPictures.Emblem("▼", 22, OverlayPictures.Brush(0x7A, 0x2A, 0x30))
                    : OverlayPictures.Emblem(effect.Label.Length > 0 ? effect.Label[..1].ToUpperInvariant() : "?", 22, OverlayPictures.Brush(0x24, 0x4A, 0x36))
                : new Image { Source = icon, Width = 22, Height = 22 };
            picture.ToolTip = effect.Label;
            cell.Children.Add(picture);
            cell.Children.Add(OverlayPictures.Scaled(new TextBlock { Text = effect.Time, Foreground = OverlayPictures.Muted, HorizontalAlignment = HorizontalAlignment.Center }, 9));
            panel.Children.Add(cell);
        }
    }
}
