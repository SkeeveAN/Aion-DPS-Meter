using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>One cast the bar shows.</summary>
public sealed record CastRowData(
    string Name, string Detail, string TargetText, bool AtYou, bool Area, bool Lethal, double ElapsedMs, int CastMs, int? LandMs, string? IconPath);

/// <summary>The target of the target: who the clicked target is aiming at.</summary>
public sealed record TargetOfTargetData(string Name, string From, string? ImagePath, bool IsBoss);

/// <summary>An effect on the healer's target.</summary>
public sealed record EffectData(string Label, string? IconPath, string Time, bool Debuff);

/// <summary>The healer's target: name, hit points and effects.</summary>
public sealed record HealerData(string Name, long? Hp, long? MaxHp, IReadOnlyList<EffectData> Effects);

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
        private readonly TextBlock _name = new() { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = OverlayPictures.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _detail = new() { FontSize = 11, Foreground = OverlayPictures.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _tags = new() { FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        private readonly TextBlock _time = new() { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = OverlayPictures.Text, VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Right };
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
            var nameLine = new DockPanel();
            DockPanel.SetDock(_tags, Dock.Right);
            nameLine.Children.Add(_tags);
            nameLine.Children.Add(_name);
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
            _name.Text = row.Name;
            _detail.Text = row.Detail.Length > 0 ? row.Detail + " → " + row.TargetText : "→ " + row.TargetText;
            _detail.Foreground = row.AtYou ? OverlayPictures.Red : OverlayPictures.Muted;
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

/// <summary>The target of the target: a picture (a boss's, or a player's class) and the name of who the clicked target is aiming at.</summary>
public sealed class TargetOfTargetView : Border
{
    private readonly Image _picture = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _pictureHost = new() { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Width = 56, Height = 34 };
    private readonly TextBlock _name = new() { FontWeight = FontWeights.Bold, FontSize = 14, Foreground = OverlayPictures.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _from = new() { FontSize = 11, Foreground = OverlayPictures.Muted, TextTrimming = TextTrimming.CharacterEllipsis };

    public TargetOfTargetView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(6, 4, 6, 4);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_from);
        text.Children.Add(_name);
        var row = new DockPanel();
        DockPanel.SetDock(_pictureHost, Dock.Left);
        row.Children.Add(_pictureHost);
        row.Children.Add(text);
        Child = row;
        _pictureHost.Content = _picture;
    }

    public void Show(TargetOfTargetData data)
    {
        _name.Text = data.Name;
        _from.Text = data.From + " →";
        ImageSource? image = OverlayPictures.Load(data.ImagePath);
        _picture.Source = image;
        // a boss's picture is wide, a class emblem square
        _pictureHost.Width = data.IsBoss ? 56 : 34;
        _pictureHost.Content = image is null ? OverlayPictures.Emblem(data.IsBoss ? "B" : "?", 34) : _picture;
    }
}

/// <summary>The healer's target: its name, a hit point bar and its effects (debuffs, buffs or both, as chosen).</summary>
public sealed class HealerTargetView : Border
{
    private readonly TextBlock _name = new() { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = OverlayPictures.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _hpText = new() { FontSize = 11, Foreground = OverlayPictures.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(1, 1) };
    private readonly Grid _hpBar = new() { Height = 16, Margin = new Thickness(0, 3, 0, 0) };
    private readonly WrapPanel _effects = new() { Margin = new Thickness(0, 4, 0, 0) };
    private bool _showHp = true, _showEffects = true;

    public HealerTargetView()
    {
        Background = OverlayPictures.Panel;
        BorderBrush = OverlayPictures.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(6, 4, 6, 4);
        _hpBar.Children.Add(new Border { Background = OverlayPictures.Track, CornerRadius = new CornerRadius(3) });
        _hpFill.CornerRadius = new CornerRadius(3);
        _hpBar.Children.Add(_hpFill);
        _hpBar.Children.Add(_hpText);
        var stack = new StackPanel();
        stack.Children.Add(_name);
        stack.Children.Add(_hpBar);
        stack.Children.Add(_effects);
        Child = stack;
    }

    public void Configure(bool showHp, bool showEffects)
    {
        _showHp = showHp;
        _showEffects = showEffects;
        _hpBar.Visibility = showHp ? Visibility.Visible : Visibility.Collapsed;
        _effects.Visibility = showEffects ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Show(HealerData data)
    {
        _name.Text = data.Name;
        if (_showHp)
        {
            double fraction = data.Hp is long hp && data.MaxHp is long max && max > 0 ? Math.Clamp((double)hp / max, 0, 1) : 0;
            ((ScaleTransform)_hpFill.RenderTransform).ScaleX = fraction;
            _hpFill.Background = fraction < 0.3 ? OverlayPictures.Red : fraction < 0.6 ? OverlayPictures.Amber : OverlayPictures.Green;
            _hpText.Text = data.Hp is long h
                ? data.MaxHp is long m && m > 0 ? $"{h:N0} / {m:N0}  ({fraction * 100:0}%)" : $"{h:N0}"
                : "–";
        }

        if (!_showEffects)
        {
            return;
        }

        _effects.Children.Clear();
        foreach (EffectData effect in data.Effects.Take(16))
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 4, 2), Width = 26 };
            ImageSource? icon = OverlayPictures.Load(effect.IconPath);
            FrameworkElement picture = icon is null
                ? OverlayPictures.Emblem(effect.Label.Length > 0 ? effect.Label[..1].ToUpperInvariant() : "?", 22, effect.Debuff ? OverlayPictures.Brush(0x5A, 0x24, 0x2A) : OverlayPictures.Brush(0x24, 0x4A, 0x36))
                : new Image { Source = icon, Width = 22, Height = 22 };
            picture.ToolTip = effect.Label;
            cell.Children.Add(picture);
            cell.Children.Add(new TextBlock { Text = effect.Time, FontSize = 9, Foreground = OverlayPictures.Muted, HorizontalAlignment = HorizontalAlignment.Center });
            _effects.Children.Add(cell);
        }
    }
}
