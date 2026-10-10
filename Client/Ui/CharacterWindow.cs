using AionDPS.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The local player's Aion 2 character, laid out like the player page on the website: a strip with the class
/// emblem, name, level, legion and the key numbers, and below it tabs - equipment (the doll with the game's own
/// item icons in their rarity colours and the enchant level), skills (icon grid: active, passive, stigma),
/// the Daevanion boards (the game's node tiles) and the species knowledge of the pet window. Built in code
/// from the data the game sends at login and zone changes - see <see cref="Aion2EntityDirectory"/> - and
/// redrawn whenever a fresh record arrives.
/// </summary>
public sealed class CharacterWindow : Window
{
    private static readonly Color Gold = Color.FromRgb(0xF0, 0xB8, 0x40);

    // Rarity colours as the website paints them (the grade letters of the game's UI atlas).
    private static readonly Dictionary<int, Color> GradeColors = new()
    {
        [1] = Color.FromRgb(0xAA, 0xB2, 0xBD), [2] = Color.FromRgb(0x4C, 0xC4, 0x6A), [3] = Color.FromRgb(0x3A, 0x9B, 0xE8),
        [4] = Color.FromRgb(0xF0, 0xB0, 0x30), [5] = Color.FromRgb(0xF0, 0x7A, 0x20), [6] = Color.FromRgb(0xD9, 0x4F, 0x4F),
        [7] = Color.FromRgb(0x2F, 0xD0, 0xC0),
    };

    private static readonly string[] GradeNames = { "", "Common", "Rare", "Legend", "Unique", "Epic", "Mythic", "Special" };
    private static readonly string[] ArmorSlots = { "Helmet", "Shoulder", "Torso", "Gloves", "Pants", "Boots", "Cape", "Belt" };
    private static readonly string[] AccessorySlots = { "Necklace", "Earring", "Ring", "Bracelet", "Amulet", "Brooch", "Pendant" };

    private readonly Aion2EntityDirectory _directory;
    private readonly ContentControl _host = new();
    private readonly Dictionary<string, BitmapImage?> _images = new();
    private string _tab = "character";
    private BitmapImage? _portrait;
    private string? _portraitAskedFor;
    private int _boardIndex;

    private sealed record GearRow(Aion2EquippedItem Item, Aion2ItemInfo? Info);

    /// <summary>Opens a tab by its id (character, equipment, arcana, skills, board, species) - for the picture render.</summary>
    public void ShowTab(string id)
    {
        _tab = id;
        Render();
    }

    public CharacterWindow(Aion2EntityDirectory directory)
    {
        _directory = directory;
        Title = "Character";
        Width = 1180;
        Height = 760;
        MinWidth = 820;
        MinHeight = 480;
        FontSize = 12.5;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _host };
        ThemedChrome.Apply(this);

        _directory.CharacterChanged += OnCharacterChanged;
        Closed += (_, _) => _directory.CharacterChanged -= OnCharacterChanged;
        Render();
        Loaded += (_, _) => FitHeightToContent();
    }

    /// <summary>Opens (and grows, when a re-render adds rows) to the height the content needs, so
    /// everything is visible at once - never taller than the screen's work area, where the scroll
    /// bar takes over.</summary>
    private void FitHeightToContent()
    {
        if (_host.Content is not UIElement content)
        {
            return;
        }

        content.Measure(new Size(Math.Max(ActualWidth - 20, 200), double.PositiveInfinity));
        double wanted = content.DesiredSize.Height + 30 /* title bar */ + 4;
        Rect area = SystemParameters.WorkArea;
        double height = Math.Min(wanted, area.Height);
        if (height > ActualHeight + 1)
        {
            Height = height;
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - height));
        }
    }

    /// <summary>Puts the window to the right of the meter, or to its left when the screen ends
    /// there, level with its top edge.</summary>
    public void PlaceBeside(Window meter)
    {
        Rect area = SystemParameters.WorkArea;
        double left = meter.Left + meter.ActualWidth + 6;
        if (left + Width > area.Right)
        {
            left = Math.Max(area.Left, meter.Left - Width - 6);
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = Math.Max(area.Top, Math.Min(meter.Top, area.Bottom - Height));
    }

    private void OnCharacterChanged(Aion2CharacterInfo info) => Dispatcher.BeginInvoke(new Action(Render));

    // ---------------------------------------------------------------- building blocks

    private Brush Res(string key) => (Brush)FindResource(key);

    private static SolidColorBrush Solid(Color color) => new(color);

    private static Color Blend(Color top, Color bottom, double amount) => Color.FromRgb(
        (byte)(bottom.R + (top.R - bottom.R) * amount), (byte)(bottom.G + (top.G - bottom.G) * amount), (byte)(bottom.B + (top.B - bottom.B) * amount));

    private Color PanelColor => Res("Brush.Panel") is SolidColorBrush panel ? panel.Color : Color.FromRgb(0x15, 0x25, 0x32);

    private TextBlock Text(string text, double size = 12.5, FontWeight? weight = null, Brush? brush = null, Thickness? margin = null, TextWrapping wrap = TextWrapping.NoWrap) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Res("Brush.Text"),
        Margin = margin ?? new Thickness(0),
        TextWrapping = wrap,
        TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
    };

    private Border Card(UIElement child, Thickness? padding = null, Thickness? margin = null) => new()
    {
        Background = Res("Brush.Panel"),
        BorderBrush = Res("Brush.Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(14),
        Margin = margin ?? new Thickness(0),
        Child = child,
    };

    private TextBlock Heading(string text) => Text(text.ToUpperInvariant(), 11, FontWeights.Bold, Res("Brush.TextMuted"), new Thickness(0, 0, 0, 10));

    private BitmapImage? Picture(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (_images.TryGetValue(path, out BitmapImage? cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            image = null;
        }

        _images[path] = image;
        return image;
    }

    /// <summary>A square item / skill tile like the website's: the game's icon over a rarity-coloured backdrop,
    /// the initials when there is no icon, and a small number in the corner (enchant, skill level).</summary>
    /// <summary>Enchant byte 16..20 is +15 with 1..5 Zenit ("Amplify") stages; anything else is the plain level.</summary>
    private static int ZenitStage(int enchant) => enchant is > 15 and <= 20 ? enchant - 15 : 0;

    private static string EnchantBadge(int enchant) => ZenitStage(enchant) > 0 ? $"+15 \u25C6{ZenitStage(enchant)}" : $"+{enchant}";

    private FrameworkElement IconTile(string? iconPath, string name, Color color, double size, string? badge = null, bool bonusBadge = false)
    {
        var backdrop = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.3, 0.25),
            Center = new Point(0.3, 0.25),
            RadiusX = 1,
            RadiusY = 1,
        };
        backdrop.GradientStops.Add(new GradientStop(Blend(color, Color.FromRgb(0x20, 0x17, 0x0A), 0.55), 0));
        backdrop.GradientStops.Add(new GradientStop(Blend(color, Color.FromRgb(0x0C, 0x0A, 0x08), 0.22), 1));

        var inner = new Grid();
        if (Picture(iconPath) is { } image)
        {
            inner.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true });
        }
        else
        {
            inner.Children.Add(new TextBlock
            {
                Text = name.Length >= 2 ? name[..2] : name,
                FontWeight = FontWeights.Bold,
                Foreground = Res("Brush.TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (!string.IsNullOrEmpty(badge))
        {
            inner.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(0, 0, 2, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = badge,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = bonusBadge ? Solid(Color.FromRgb(0xFF, 0xD6, 0x6B)) : Brushes.White,
                },
            });
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(2),
            BorderBrush = Solid(color),
            Background = backdrop,
            ClipToBounds = true,
            Child = inner,
        };
    }

    // ---------------------------------------------------------------- the page

    private void Render()
    {
        Aion2CharacterInfo? character = _directory.LocalCharacter;
        if (character is null)
        {
            Title = "Character";
            _host.Content = new Border
            {
                Padding = new Thickness(24),
                Child = Text("No character data yet. The game sends it when you log in or change zone - log in once with the meter running and it is kept for the next time.",
                    13, brush: Res("Brush.TextMuted"), wrap: TextWrapping.Wrap),
            };
            return;
        }

        string className = _directory.ClassOf(character.CombatId) ?? Aion2SkillNames.ClassFromCode(character.ClassCode) ?? "?";
        string? guild = _directory.GuildOf(character.CombatId);
        var gear = _directory.LocalEquipment
            .Select(e => new GearRow(e, Aion2ItemCatalog.Find(e.ItemId)))
            .OrderBy(x => x.Item.SlotIndex)
            .ToList();
        // Like the website's profile: the five arcana have their own tab, the equipment tab holds the rest.
        var arcana = gear.Where(x => x.Info?.Slot == "Arcana").OrderBy(x => x.Item.SlotIndex).ToList();
        var equipmentRows = gear.Where(x => x.Info?.Slot != "Arcana").ToList();
        var known = gear.Where(x => x.Info is not null).Select(x => x.Info!).ToList();
        double average = known.Count > 0 ? known.Average(i => i.ItemLevel) : 0;
        var skills = _directory.LocalSkills
            .Where(k => k.SkillId % 10000 == 0)
            .ToList();
        var boards = _directory.LocalDaevanion;
        var species = _directory.LocalSpecies;
        int nodes = boards.Sum(b => Aion2DaevanionCatalog.Summarize(b.BoardId, b.NodeIds).ActivePoints);

        Title = $"{character.Name} - Character";

        var tabs = new List<(string Id, string Label, int Count, Func<UIElement> Build)>();
        // The attributes arrive with the stat frame; until it was seen there is no tab.
        if (_directory.LocalAttributes is { } attributes)
        {
            tabs.Add(("character", Loc("Character.Tab"), MainAttributes.Length + Lords.Length, () => BuildCharacter(attributes)));
            StartPortraitLoad(character);
        }

        if (equipmentRows.Count > 0)
        {
            tabs.Add(("equipment", "Equipment", equipmentRows.Count, () => BuildEquipment(equipmentRows, className, average)));
        }

        if (skills.Count > 0)
        {
            tabs.Add(("skills", "Skills", skills.Count, () => BuildSkills(skills)));
        }

        if (boards.Count > 0)
        {
            tabs.Add(("board", "Daevanion Board", nodes, () => BuildBoards(boards, skills, className)));
        }

        if (species.Count > 0)
        {
            tabs.Add(("species", "Species Knowledge", species.Count, () => BuildSpecies(species, _directory.LocalPets)));
        }

        if (arcana.Count > 0)
        {
            tabs.Add(("arcana", "Arcana", arcana.Count, () => BuildArcana(arcana)));
        }

        if (tabs.Count > 0 && tabs.All(t => t.Id != _tab))
        {
            _tab = tabs[0].Id;
        }

        var page = new StackPanel { Margin = new Thickness(16, 14, 16, 0) };
        page.Children.Add(BuildStrip(character, className, guild, average, skills.Count, nodes));
        if (tabs.Count > 0)
        {
            page.Children.Add(BuildTabBar(tabs.Select(t => (t.Id, t.Label, t.Count)).ToList()));
            page.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = tabs.First(t => t.Id == _tab).Build() });
        }
        else
        {
            page.Children.Add(Text("Not sent yet - it comes with the login.", 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 14, 0, 0)));
        }

        page.Children.Add(Text(
            (character.Restored
                ? $"Saved from your last login ({character.ReceivedAt:yyyy-MM-dd HH:mm}); updated on every relog. "
                : $"As sent by the game at login / zone change ({character.ReceivedAt:HH:mm:ss}). ")
            + "Stones, rolled stats and stigma effects are not decoded and not shown.",
            11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 16, 0, 12), wrap: TextWrapping.Wrap));
        _host.Content = page;
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(new Action(FitHeightToContent), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private UIElement BuildStrip(Aion2CharacterInfo character, string className, string? guild, double average, int skillCount, int nodeCount)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement badge = EmblemBadge(className, 56, 12);
        Grid.SetColumn(badge, 0);
        row.Children.Add(badge);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        names.Children.Add(Text(character.Name, 22, FontWeights.Bold));
        names.Children.Add(Text($"{ClassCatalog.DisplayName(className, LocalizationManager.Instance.Language)} · Level {character.Level}", 12.5, brush: Res("Brush.TextMuted")));
        // Faction, legion and server on one line, like the website's profile header.
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        // 2 in the class code's faction bit is Elyos (see Aion2EntityDirectory.FactionOf); the other value is not known to mean Asmodian.
        string? faction = character.ClassCode % 4 == 2 ? "Elyos" : _directory.FactionOf(character.CombatId);
        if (faction is not null)
        {
            if (Picture(Path.Combine(AppContext.BaseDirectory, "assets", "races", "icons", faction + ".png")) is { } flag)
            {
                meta.Children.Add(new Image { Source = flag, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            meta.Children.Add(Text(faction, 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 0, 14, 0)));
        }

        if (guild is not null)
        {
            meta.Children.Add(Text($"Legion: {guild}", 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 0, 14, 0)));
        }

        if (character.ServerId > 0)
        {
            meta.Children.Add(Text(Aion2Servers.NameOf(character.ServerId), 12, brush: Res("Brush.TextMuted")));
        }

        if (meta.Children.Count > 0)
        {
            names.Children.Add(meta);
        }

        Grid.SetColumn(names, 1);
        row.Children.Add(names);

        var numbers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        numbers.Children.Add(Number(average.ToString("F1", CultureInfo.CurrentCulture), "Avg item level", Gold));
        if (skillCount > 0)
        {
            numbers.Children.Add(Number(skillCount.ToString(), "Skills", null));
        }

        if (nodeCount > 0)
        {
            numbers.Children.Add(Number(nodeCount.ToString(), "Daevanion points", null));
        }

        Grid.SetColumn(numbers, 2);
        row.Children.Add(numbers);

        // The worn titles as chips under the header, tinted by grade; behind them, after a thin divider,
        // the wings, the wing skin and the summoned pet.
        var content = new StackPanel();
        content.Children.Add(row);
        string language = LocalizationManager.Instance.Language;
        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (Aion2TitleSlot slot in _directory.LocalTitles.OrderBy(t => t.Slot))
        {
            if (Aion2Titles.Find(slot.TitleId, language) is { } title)
            {
                chips.Children.Add(Chip(title.Name, GradeChipColor(title.Grade)));
            }
        }

        var looks = new List<UIElement>();
        if (_directory.LocalWing is { } wing)
        {
            if (Aion2Wings.Find(wing.WingId, language) is { } wingInfo)
            {
                looks.Add(Chip(wingInfo.Name, GradeChipColor(wingInfo.Grade), wingInfo.IconPath));
            }

            if (wing.SkinId > 0 && Aion2Wings.Find(wing.SkinId, language) is { } skinInfo)
            {
                looks.Add(Chip(skinInfo.Name, GradeChipColor(skinInfo.Grade), skinInfo.IconPath, badge: Loc("Character.Skin")));
            }
        }

        if (_directory.LocalActivePet is > 0 and int petId && Aion2Pets.PetName(petId, language) is { } petName)
        {
            int? petLevel = _directory.LocalPetStates.Where(p => p.PetId == petId).Select(p => (int?)p.Level).FirstOrDefault();
            looks.Add(Chip(petName, GradeChipColor("Common"), Aion2Pets.PetIconPath(petId), level: petLevel));
        }

        if (looks.Count > 0)
        {
            if (chips.Children.Count > 0)
            {
                chips.Children.Add(new Border { Width = 1, Height = 24, Margin = new Thickness(2, 4, 10, 10), Background = Res("Brush.Border") });
            }

            foreach (UIElement look in looks)
            {
                chips.Children.Add(look);
            }
        }

        if (chips.Children.Count > 0)
        {
            content.Children.Add(chips);
        }

        return Card(content, new Thickness(16, 14, 22, 14));
    }

    private static string Loc(string key) => LocalizationManager.Instance[key];

    /// <summary>The website's chip colours by grade (titles and wings alike).</summary>
    private static Color GradeChipColor(string grade) => grade switch
    {
        "Rare" => Color.FromRgb(0x5B, 0xD3, 0x6B),
        "Epic" => Color.FromRgb(0xB4, 0x8C, 0xFF),
        "Legend" => Color.FromRgb(0x4A, 0xA8, 0xFF),
        "Unique" => Color.FromRgb(0xFF, 0xC9, 0x4D),
        "Special" => Color.FromRgb(0xFF, 0x5A, 0x4F),
        _ => Color.FromRgb(0x9D, 0xB3, 0xC2),
    };

    private const double ChipHeight = 32;

    /// <summary>A rounded chip of fixed height in a grade's colour: optional icon, the name, an optional small
    /// marker ("Skin") and an optional "Lv N". Titles, wings and the pet share it so they line up.</summary>
    private UIElement Chip(string text, Color color, string? iconPath = null, string? badge = null, int? level = null)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (Picture(iconPath) is { } icon)
        {
            inner.Children.Add(new Image { Source = icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        inner.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(color), VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(badge))
        {
            inner.Children.Add(new Border
            {
                BorderBrush = Solid(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, FontSize = 10.5, Foreground = Solid(color) },
            });
        }

        if (level is { } lv)
        {
            inner.Children.Add(Text($"Lv {lv}", 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(8, 0, 0, 0)));
        }

        return new Border
        {
            Height = ChipHeight,
            CornerRadius = new CornerRadius(ChipHeight / 2),
            BorderThickness = new Thickness(1),
            BorderBrush = Solid(color),
            Background = Solid(Color.FromArgb(0x24, color.R, color.G, color.B)),
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 6),
            Child = inner,
        };
    }

    /// <summary>The class emblem in a gold-rimmed rounded square; the class's first letters when the emblem is missing.</summary>
    private UIElement EmblemBadge(string className, double size, double corner)
    {
        UIElement content = Picture(Aion2Artwork.ClassEmblemPath(className)) is { } image
            ? new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(4) }
            : new TextBlock
            {
                Text = className.Length >= 2 ? className[..2].ToUpperInvariant() : "?",
                FontWeight = FontWeights.ExtraBold,
                FontSize = size / 3,
                Foreground = Solid(Gold),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(corner),
            BorderBrush = Solid(Gold),
            BorderThickness = new Thickness(2),
            Background = Res("Brush.Control"),
            Child = content,
        };
    }

    private UIElement Number(string value, string label, Color? color)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        s.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = color is { } c ? Solid(c) : Res("Brush.Text"), HorizontalAlignment = HorizontalAlignment.Center });
        s.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = Res("Brush.TextMuted"), HorizontalAlignment = HorizontalAlignment.Center });
        return s;
    }

    private UIElement BuildTabBar(IReadOnlyList<(string Id, string Label, int Count)> tabs)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach ((string id, string label, int count) in tabs)
        {
            bool selected = id == _tab;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Text(label, 13, selected ? FontWeights.SemiBold : FontWeights.Normal, selected ? Res("Brush.Text") : Res("Brush.TextMuted")));
            content.Children.Add(Text(count.ToString(), 11, brush: Res("Brush.TextMuted"), margin: new Thickness(8, 2, 0, 0)));
            string tabId = id;
            var tab = new Border
            {
                Padding = new Thickness(14, 8, 14, 7),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = selected ? Solid(Gold) : Brushes.Transparent,
                Background = selected ? Res("Brush.Control") : Brushes.Transparent,
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Child = content,
            };
            tab.MouseLeftButtonUp += (_, _) =>
            {
                if (_tab != tabId)
                {
                    _tab = tabId;
                    Render();
                }
            };
            bar.Children.Add(tab);
        }

        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Res("Brush.Border"), Child = bar };
    }

    // ---------------------------------------------------------------- character (attributes)

    // Stat-frame ids (Aion2Attributes): the six main attributes in the order of the website's hexagon (Might on top,
    // clockwise) and the ten Lord values in the order of its ring (Justice on top, clockwise).
    private static readonly (string Key, int Id)[] MainAttributes =
    {
        ("str", 1), ("dex", 2), ("agi", 5), ("wis", 6), ("int", 3), ("con", 4),
    };

    private static readonly (string Key, int Id)[] Lords =
    {
        ("justice", 7), ("destruction", 13), ("death", 14), ("wisdom", 15), ("destiny", 16),
        ("space", 17), ("time", 11), ("life", 10), ("illusion", 9), ("freedom", 8),
    };

    private const double RingSize = 440;

    /// <summary>The round symbol of an attribute or Lord on a dark disc (assets/aion2/stats).</summary>
    private UIElement AttrIcon(string key, bool lord, double size)
    {
        string file = (lord ? "lords_" : "") + (key == "con" ? "constitution" : key) + ".png";
        var disc = new RadialGradientBrush(Color.FromRgb(0x2B, 0x47, 0x62), Color.FromRgb(0x11, 0x1F, 0x2D));
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new System.Windows.Shapes.Ellipse { Fill = disc, Stroke = Res("Brush.Border"), StrokeThickness = 1 });
        if (Picture(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "stats", file)) is { } image)
        {
            grid.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.12) });
        }

        return grid;
    }

    private UIElement BuildCharacter(IReadOnlyDictionary<int, int> values)
    {
        int Value(int id) => values.GetValueOrDefault(id);

        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

        // Left: the portrait (when the server has it and the profile upload is on), then the main attributes as bars.
        int top = Math.Max(1, MainAttributes.Max(a => Value(a.Id)));
        var left = new StackPanel();
        if (_portrait is not null)
        {
            left.Children.Add(new Image { Source = _portrait, Height = 300, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center });
        }

        left.Children.Add(Heading(Loc("Character.AttrMain")));
        foreach ((string key, int id) in MainAttributes)
        {
            int value = Value(id);
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7), Opacity = value == 0 ? 0.55 : 1 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            UIElement icon = AttrIcon(key, false, 24);
            row.Children.Add(icon);
            TextBlock name = Text(Loc("Character.Attr." + key));
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Res("Brush.Border") });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(value, 0), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(top - value, 0.0001), GridUnitType.Star) });
            if (value > 0)
            {
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Res("Brush.Accent") });
            }

            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);
            TextBlock number = Text(value.ToString(CultureInfo.CurrentCulture), 13, FontWeights.Bold);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            number.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(number, 3);
            row.Children.Add(number);
            left.Children.Add(row);
        }

        Border leftCard = Card(left, new Thickness(16, 14, 16, 8));
        leftCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(leftCard, 0);
        page.Children.Add(leftCard);

        // Middle: the Lord ring.
        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        middle.Children.Add(Heading(Loc("Character.AttrLords")));
        middle.Children.Add(BuildRing(Value));
        Border middleCard = Card(middle, new Thickness(14, 14, 14, 10), new Thickness(12, 0, 12, 0));
        middleCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(middleCard, 1);
        page.Children.Add(middleCard);

        // Right: the Lord list.
        var list = new StackPanel();
        list.Children.Add(Heading(Loc("Character.AttrLords")));
        for (int i = 0; i < Lords.Length; i++)
        {
            (string key, int id) = Lords[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            UIElement icon = AttrIcon(key, true, 28);
            ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Text(Loc("Character.Attr." + key)));
            names.Children.Add(Text(Loc("Character.Lord." + key), 10.5, brush: Res("Brush.TextMuted")));
            Grid.SetColumn(names, 1);
            row.Children.Add(names);
            TextBlock number = Text(Value(id).ToString(CultureInfo.CurrentCulture), 14, FontWeights.Bold);
            number.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(number, 2);
            row.Children.Add(number);
            list.Children.Add(new Border
            {
                Padding = new Thickness(0, 5, 0, 5),
                BorderThickness = new Thickness(0, 0, 0, i == Lords.Length - 1 ? 0 : 1),
                BorderBrush = Res("Brush.Border"),
                Child = row,
            });
        }

        Border listCard = Card(list, new Thickness(16, 14, 16, 8));
        listCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(listCard, 2);
        page.Children.Add(listCard);
        return page;
    }

    /// <summary>The ring like the website's: ten Lords on a circle (clockwise from the top, the value at the lower
    /// right of the symbol), the six main attributes as a hexagon inside it (Might on top). Positioned on a canvas
    /// from the website's proportions (Lord radius 238 and hexagon radius 108 of 600).</summary>
    private UIElement BuildRing(Func<int, int> value)
    {
        const double Scale = RingSize / 600;
        double centre = RingSize / 2;
        var canvas = new Canvas { Width = RingSize, Height = RingSize };
        canvas.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 2 * 238 * Scale, Height = 2 * 238 * Scale, Stroke = Res("Brush.Border"), StrokeThickness = 2,
        });
        Canvas.SetLeft(canvas.Children[0], centre - 238 * Scale);
        Canvas.SetTop(canvas.Children[0], centre - 238 * Scale);
        var inner = new System.Windows.Shapes.Ellipse
        {
            Width = 2 * 165 * Scale, Height = 2 * 165 * Scale, Stroke = Res("Brush.Border"), StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 5 },
        };
        Canvas.SetLeft(inner, centre - 165 * Scale);
        Canvas.SetTop(inner, centre - 165 * Scale);
        canvas.Children.Add(inner);

        void Place(UIElement node, double width, double height, int index, int count, double radius)
        {
            double angle = -Math.PI / 2 + index * 2 * Math.PI / count;
            Canvas.SetLeft(node, centre + Math.Cos(angle) * radius * Scale - width / 2);
            Canvas.SetTop(node, centre + Math.Sin(angle) * radius * Scale - height / 2);
            canvas.Children.Add(node);
        }

        const double LordIcon = 46;
        for (int i = 0; i < Lords.Length; i++)
        {
            (string key, int id) = Lords[i];
            var stack = new Grid { Width = 74, Height = 66, ToolTip = $"{Loc("Character.Attr." + key)} [{Loc("Character.Lord." + key)}]" };
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LordIcon + 3) });
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var icon = new Grid { Width = LordIcon, Height = LordIcon, VerticalAlignment = VerticalAlignment.Top };
            icon.Children.Add(AttrIcon(key, true, LordIcon));
            icon.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x0B, 0x16, 0x20)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, -12, -4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = value(id).ToString(CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
            });
            stack.Children.Add(icon);
            TextBlock label = Text(Loc("Character.Attr." + key), 10.5, brush: Res("Brush.TextMuted"));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(label, 1);
            stack.Children.Add(label);
            Place(stack, 74, 66, i, Lords.Length, 238);
        }

        for (int i = 0; i < MainAttributes.Length; i++)
        {
            (string key, int id) = MainAttributes[i];
            var stack = new StackPanel { Width = 66, ToolTip = Loc("Character.Attr." + key) };
            UIElement icon = AttrIcon(key, false, 26);
            ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(icon);
            TextBlock label = Text(Loc("Character.Attr." + key), 10.5);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            TextBlock number = Text(value(id).ToString(CultureInfo.CurrentCulture), 15, FontWeights.Bold, Res("Brush.Accent"));
            number.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(number);
            Place(stack, 66, 60, i, MainAttributes.Length, 108);
        }

        return canvas;
    }

    /// <summary>Fetches the portrait once per character and session - only when the profile upload is switched on
    /// (the player is only on the server then), never in the headless picture modes. Errors leave the tab without it.</summary>
    private void StartPortraitLoad(Aion2CharacterInfo character)
    {
        if (MainWindow.Headless || character.ServerId <= 0 || string.IsNullOrEmpty(character.Name))
        {
            return;
        }

        string key = character.ServerId + "/" + character.Name;
        if (_portraitAskedFor == key || !MeterSettings.Load().AutoUploadProfile)
        {
            return;
        }

        _portraitAskedFor = key;
        string fingerprint = "aion2:" + System.Text.RegularExpressions.Regex.Replace(Aion2Servers.NameOf(character.ServerId).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        _ = Task.Run(async () =>
        {
            string? file = await Upload.PortraitClient.GetAsync(character.Name, fingerprint);
            if (file is null)
            {
                return;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    _portrait = image;
                    Render();
                }));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or TaskCanceledException)
            {
                // no portrait
            }
        });
    }

    // ---------------------------------------------------------------- equipment

    private static Color GradeColor(int grade) => GradeColors.GetValueOrDefault(grade, GradeColors[1]);

    private UIElement BuildEquipment(IReadOnlyList<GearRow> gear, string className, double average)
    {
        static int Rank(string[] order, GearRow g)
        {
            int index = Array.IndexOf(order, g.Info?.Slot ?? "");
            return index < 0 ? 99 : index;
        }

        var armor = gear.Where(g => ArmorSlots.Contains(g.Info?.Slot ?? "")).OrderBy(g => Rank(ArmorSlots, g)).ThenBy(g => g.Item.SlotIndex).ToList();
        var accessories = gear.Where(g => AccessorySlots.Contains(g.Info?.Slot ?? "")).OrderBy(g => Rank(AccessorySlots, g)).ThenBy(g => g.Item.SlotIndex).ToList();
        var other = gear.Except(armor).Except(accessories).ToList();

        var doll = new Grid();
        doll.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        doll.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        doll.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement left = SlotColumn(armor);
        UIElement middle = BuildCenter(className, average);
        UIElement right = SlotColumn(accessories);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(right, 2);
        middle.SetValue(FrameworkElement.MarginProperty, new Thickness(22, 0, 22, 0));
        doll.Children.Add(left);
        doll.Children.Add(middle);
        doll.Children.Add(right);

        var page = new StackPanel();
        page.Children.Add(doll);
        if (other.Count > 0)
        {
            var wrap = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
            foreach (GearRow g in other)
            {
                UIElement slot = GearSlot(g);
                slot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 10));
                wrap.Children.Add(slot);
            }

            page.Children.Add(wrap);
        }

        return page;
    }

    /// <summary>The arcana slots as a grid of tiles, three to a row (the website lists them the same way).</summary>
    private UIElement BuildArcana(IReadOnlyList<GearRow> arcana)
    {
        var wrap = new UniformGrid { Columns = 3 };
        foreach (GearRow g in arcana)
        {
            UIElement slot = GearSlot(g);
            slot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 10));
            wrap.Children.Add(slot);
        }

        return wrap;
    }

    private static readonly Dictionary<string, string> PetGlyphs = new()
    {
        ["cognia"] = "M30,40 l6,-10 8,6 8,-6 6,10 -6,14 H36 z",
        ["fera"] = "M44,28 l14,26 H30 z",
        ["natura"] = "M32,44 a12,12 0 1 0 24,0 a12,12 0 1 0 -24,0 z",
        ["varia"] = "M44,28 a16,16 0 1 0 0,32 a12,12 0 1 1 0,-24 z",
        ["specia"] = "M44,26 l6,14 14,4 -14,4 -6,14 -6,-14 -14,-4 14,-4 z",
    };

    /// <summary>One species' pet circle: a disc with the species' glyph, one dot per effect place coloured by its quality
    /// (dark while not unlocked), and the level underneath. The right side of the ring stays open, like the game's.</summary>
    private UIElement PetCircle(Aion2Pet pet, string key)
    {
        var canvas = new Canvas { Width = 88, Height = 88 };
        canvas.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 84, Height = 84, Fill = Res("Brush.Control"), Stroke = Res("Brush.Border"), StrokeThickness = 1,
        });
        Canvas.SetLeft(canvas.Children[0], 2);
        Canvas.SetTop(canvas.Children[0], 2);
        if (PetGlyphs.TryGetValue(key, out string? glyph))
        {
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(glyph), Fill = Res("Brush.TextMuted") });
        }

        Color[] quality =
        {
            Colors.Transparent, Color.FromRgb(0xD9, 0xDD, 0xE0), Color.FromRgb(0x5B, 0xD3, 0x6B),
            Color.FromRgb(0x4A, 0xA8, 0xFF), Color.FromRgb(0xFF, 0xC9, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x3D),
        };
        int places = Math.Max(9, pet.Kinds.Count);
        for (int i = 0; i < places; i++)
        {
            int kind = i < pet.Kinds.Count ? pet.Kinds[i] : 0;
            double angle = (double)i / 12 * Math.PI * 2 - Math.PI / 2 - 0.4;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8.4, Height = 8.4,
                Fill = kind is >= 1 and <= 5 ? Solid(quality[kind]) : Res("Brush.Border"),
            };
            Canvas.SetLeft(dot, 44 - 34 * Math.Cos(angle) - 4.2);
            Canvas.SetTop(dot, 44 + 34 * Math.Sin(angle) - 4.2);
            canvas.Children.Add(dot);
        }

        var box = new Viewbox { Width = 96, Height = 96, Child = canvas };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 14, 10) };
        stack.Children.Add(box);
        stack.Children.Add(new TextBlock
        {
            Text = $"Level {pet.Level}", FontSize = 12, Foreground = Res("Brush.TextMuted"), HorizontalAlignment = HorizontalAlignment.Center,
        });
        return stack;
    }

    private UIElement SlotColumn(IReadOnlyList<GearRow> rows)
    {
        var column = new StackPanel();
        foreach (GearRow g in rows)
        {
            UIElement slot = GearSlot(g);
            slot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            column.Children.Add(slot);
        }

        return column;
    }

    private UIElement BuildCenter(string className, double average)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        UIElement emblem = Picture(Aion2Artwork.ClassEmblemPath(className)) is { } image
            ? new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(10) }
            : Text(className.Length >= 2 ? className[..2].ToUpperInvariant() : "?", 30, FontWeights.ExtraBold, Solid(Gold));
        stack.Children.Add(new Border
        {
            Width = 110,
            Height = 140,
            CornerRadius = new CornerRadius(55, 55, 16, 16),
            BorderBrush = Solid(Gold),
            BorderThickness = new Thickness(2),
            Background = Res("Brush.Control"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = emblem is TextBlock tb ? Centered(tb) : emblem,
        });
        stack.Children.Add(Text(average > 0 ? average.ToString("F1", CultureInfo.CurrentCulture) : "-", 36, FontWeights.Bold, Solid(Gold), new Thickness(0, 12, 0, 0)));
        stack.Children.Add(Text("Avg item level", 12, brush: Res("Brush.TextMuted")));
        stack.Children.Add(Text("Hover an item for details", 10.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        return new Border
        {
            Padding = new Thickness(12, 22, 12, 22),
            VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = Res("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = new RadialGradientBrush(Color.FromArgb(0x24, Gold.R, Gold.G, Gold.B), Colors.Transparent) { Center = new Point(0.5, 0.3), GradientOrigin = new Point(0.5, 0.3), RadiusX = 0.8, RadiusY = 0.7 },
            Child = stack,
        };
    }

    private static UIElement Centered(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    private UIElement GearSlot(GearRow g)
    {
        Aion2ItemInfo? info = g.Info;
        Color color = GradeColor(info?.Grade ?? 1);
        string name = info?.Name ?? $"Item {g.Item.ItemId}";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FrameworkElement icon = IconTile(Aion2Artwork.ItemIconPath(g.Item.ItemId), name, color, 56, g.Item.Enchant > 0 ? EnchantBadge(g.Item.Enchant) : null);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(Text(name, 13, FontWeights.SemiBold, wrap: TextWrapping.Wrap));
        string slotName = info?.Slot is { Length: > 0 } s ? s : $"#{g.Item.SlotIndex}";
        text.Children.Add(Text(info is { ItemLevel: > 0 } ? $"{slotName} · IL {info.ItemLevel}" : slotName, 11.5, brush: Res("Brush.TextMuted")));
        if (info is { Grade: >= 4, Tier: > 0 })
        {
            text.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 3, 0, 0),
                Padding = new Thickness(7, 0, 7, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = Solid(Color.FromArgb(0x99, color.R, color.G, color.B)),
                Child = Text($"{GradeNames[Math.Clamp(info.Grade, 0, GradeNames.Length - 1)]} Tier {info.Tier}", 10.5, FontWeights.SemiBold, Solid(color)),
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Color panel = PanelColor;
        var background = new LinearGradientBrush(Blend(color, panel, 0.24), Blend(color, panel, 0.07), 0);
        var slot = new Border
        {
            Padding = new Thickness(6, 6, 10, 6),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Solid(Blend(color, ((SolidColorBrush)Res("Brush.Border")).Color, 0.65)),
            Background = background,
            Child = grid,
        };
        string tip = $"{name}{(g.Item.Enchant > 0 ? $" +{(ZenitStage(g.Item.Enchant) > 0 ? 15 : g.Item.Enchant)}" : "")}{(ZenitStage(g.Item.Enchant) > 0 ? $"\nAmplify stage {ZenitStage(g.Item.Enchant)}" : "")}";
        if (info is not null)
        {
            tip += $"\n{GradeNames[Math.Clamp(info.Grade, 0, GradeNames.Length - 1)]}{(info.Tier > 0 ? $" · Tier {info.Tier}" : "")}  {info.Slot}";
            if (info.ItemLevel > 0)
            {
                tip += $"\nItem level {info.ItemLevel}";
            }
        }

        slot.ToolTip = tip;
        return slot;
    }

    // ---------------------------------------------------------------- skills

    private UIElement BuildSkills(IReadOnlyList<Aion2SkillEntry> skills)
    {
        bool hasBar = skills.Any(s => s.Equipped);
        var active = skills.Where(s => !Aion2Artwork.IsPassive(s.SkillId) && !s.Stigma && (!hasBar || s.Equipped));
        // Ids like 11000000 are the class's weapon-equip entry, not a skill the player trains.
        var passive = skills.Where(s => Aion2Artwork.IsPassive(s.SkillId) && s.SkillId % 1000000 != 0);
        var stigma = skills.Where(s => s.Stigma);

        var blocks = new List<UIElement>
        {
            SkillBlock("Active", Color.FromRgb(0xF0, 0xA0, 0x30), active),
            SkillBlock("Passive", Color.FromRgb(0x3F, 0xCF, 0x55), passive),
        };
        if (skills.Any(s => s.Stigma))
        {
            blocks.Add(SkillBlock("Stigma", Color.FromRgb(0xF0, 0xA0, 0x30), stigma));
        }

        var grid = new Grid();
        for (int i = 0; i < blocks.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (blocks[i] is FrameworkElement element)
            {
                element.Margin = new Thickness(i == 0 ? 0 : 8, 0, i == blocks.Count - 1 ? 0 : 8, 0);
            }

            Grid.SetColumn(blocks[i], i);
            grid.Children.Add(blocks[i]);
        }

        var page = new StackPanel();
        page.Children.Add(grid);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        legend.Children.Add(Text("Lv = skill level;", 11, brush: Res("Brush.TextMuted")));
        legend.Children.Add(Text("  gold = includes a bonus (Daevanion, gear)", 11, brush: Solid(Color.FromRgb(0xFF, 0xD6, 0x6B))));
        page.Children.Add(legend);
        return page;
    }

    private UIElement SkillBlock(string title, Color color, IEnumerable<Aion2SkillEntry> skills)
    {
        var list = skills.OrderByDescending(s => s.Level).ThenBy(s => Aion2SkillNames.NameOf(s.SkillId), StringComparer.CurrentCultureIgnoreCase).ToList();
        var stack = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Solid(color), Margin = new Thickness(0, 2, 8, 0) });
        heading.Children.Add(Text($"{title.ToUpperInvariant()} · {list.Count}", 11, FontWeights.Bold, Res("Brush.TextMuted")));
        stack.Children.Add(heading);

        var tiles = new UniformGrid { Columns = 5 };
        foreach (Aion2SkillEntry skill in list)
        {
            string name = Aion2SkillNames.Display(Aion2SkillNames.NameOf(skill.SkillId));
            var tile = new StackPanel { Margin = new Thickness(2, 0, 2, 12), HorizontalAlignment = HorizontalAlignment.Center };
            FrameworkElement icon = IconTile(Aion2SkillIcons.PathFor(skill.SkillId), name, color, 46, $"Lv {skill.Level}", skill.Level > skill.BaseLevel);
            tile.Children.Add(icon);
            tile.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 10.5,
                Foreground = Res("Brush.Text"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 70,
                MaxHeight = 28,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
            });
            tile.ToolTip = $"{name}\nSkill level {skill.Level}{(skill.Level > skill.BaseLevel ? $" ({skill.BaseLevel} + {skill.Level - skill.BaseLevel})" : "")}";
            tiles.Children.Add(tile);
        }

        stack.Children.Add(tiles);
        return Card(stack, new Thickness(14, 14, 14, 2));
    }

    // ---------------------------------------------------------------- Daevanion boards

    private UIElement BuildBoards(IReadOnlyList<Aion2DaevanionBoard> boards, IReadOnlyList<Aion2SkillEntry> skills, string className)
    {
        _boardIndex = Math.Clamp(_boardIndex, 0, boards.Count - 1);
        var page = new StackPanel();

        var pills = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < boards.Count; i++)
        {
            var summary = Aion2DaevanionCatalog.Summarize(boards[i].BoardId, boards[i].NodeIds);
            int total = Aion2DaevanionCatalog.TotalPoints(boards[i].BoardId);
            bool selected = i == _boardIndex;
            int index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Text(summary.Name, 12.5, selected ? FontWeights.SemiBold : FontWeights.Normal));
            content.Children.Add(Text($"  {summary.ActivePoints} / {(total > 0 ? total.ToString() : "?")}", 12, FontWeights.SemiBold, Solid(Gold)));
            var pill = new Border
            {
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = selected ? Solid(Gold) : Res("Brush.Border"),
                Background = selected ? Res("Brush.Control") : Brushes.Transparent,
                Child = content,
            };
            pill.MouseLeftButtonUp += (_, _) =>
            {
                _boardIndex = index;
                Render();
            };
            pills.Children.Add(pill);
        }

        page.Children.Add(pills);

        Aion2DaevanionBoard board = boards[_boardIndex];
        var detail = new StackPanel();
        detail.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = BoardMap(board, className) });

        var sum = Aion2DaevanionCatalog.Summarize(board.BoardId, board.NodeIds);
        string bonuses = string.Join(", ", sum.SkillBonuses.Select(kv => $"{Aion2SkillNames.Display(Aion2SkillNames.NameOf(kv.Key))} +{kv.Value}"));
        if (bonuses.Length > 0)
        {
            detail.Children.Add(Text($"Skills: {bonuses}", 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 12, 0, 0), wrap: TextWrapping.Wrap));
        }

        string stats = string.Join(", ", sum.Stats.OrderByDescending(kv => kv.Value).Select(kv => $"{SpacedToken(kv.Key)} +{kv.Value}"));
        if (stats.Length > 0)
        {
            detail.Children.Add(Text(stats, 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0), wrap: TextWrapping.Wrap));
        }

        detail.Children.Add(Text("The maps show the nodes the node table knows (about half of what is active).", 10.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0), wrap: TextWrapping.Wrap));
        page.Children.Add(Card(detail, new Thickness(18)));
        return page;
    }

    /// <summary>"CriticalResist" -> "Critical Resist".</summary>
    private static string SpacedToken(string token) => System.Text.RegularExpressions.Regex.Replace(token, "([a-z])([A-Z])", "$1 $2");

    /// <summary>One board as the game draws it: every node of the board map with the game's tile by rarity (common,
    /// rare, legend, unique), the dark variant when it is not unlocked, and the class's start tile.</summary>
    private FrameworkElement BoardMap(Aion2DaevanionBoard board, string className)
    {
        var nodes = Aion2DaevanionCatalog.NodesOfBoard(board.BoardId);
        if (nodes.Count == 0)
        {
            return Text("No node table for this board.", 11.5, brush: Res("Brush.TextMuted"));
        }

        int r0 = nodes.Min(n => n.Row), r1 = nodes.Max(n => n.Row), c0 = nodes.Min(n => n.Col), c1 = nodes.Max(n => n.Col);
        const double cell = 38;
        var active = board.NodeIds.ToHashSet();
        var grid = new Grid { Width = (c1 - c0 + 1) * cell, Height = (r1 - r0 + 1) * cell };
        for (int r = r0; r <= r1; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
        }

        for (int c = c0; c <= c1; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        }

        foreach (Aion2DaevanionNode node in nodes)
        {
            bool on = active.Contains(node.Id) || node.Type == "Start";
            string art = node.Type == "Start"
                ? Aion2Artwork.BoardStartTile(className)
                : node.Grade.ToLowerInvariant() switch { "rare" => "rare", "legend" => "legend", "unique" => "unique", _ => "common" } + (on ? "" : "-off");
            FrameworkElement tile = Picture(Aion2Artwork.BoardTilePath(art)) is { } image
                ? new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(2) }
                : new Border { Margin = new Thickness(4), CornerRadius = new CornerRadius(4), Background = on ? Solid(Gold) : Res("Brush.Control") };
            tile.ToolTip = node.Type switch
            {
                "Start" => "Start",
                "SkillLevel" => int.TryParse(node.Key, out int skillId) ? $"{Aion2SkillNames.Display(Aion2SkillNames.NameOf(skillId))} +{node.Value}" : $"Skill +{node.Value}",
                _ => $"{SpacedToken(node.Key)} +{node.Value}",
            } + (on ? "" : "  (locked)");
            Grid.SetRow(tile, node.Row - r0);
            Grid.SetColumn(tile, node.Col - c0);
            grid.Children.Add(tile);
        }

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            BorderBrush = Res("Brush.Border"),
            BorderThickness = new Thickness(1),
            Background = Solid(Blend(Colors.Black, ((SolidColorBrush)Res("Brush.Window")).Color, 0.3)),
            Child = grid,
        };
    }

    // ---------------------------------------------------------------- species knowledge

    private UIElement BuildSpecies(IReadOnlyList<Aion2SpeciesKnowledge> species, IReadOnlyList<Aion2Pet> pets)
    {
        string language = LocalizationManager.Instance.Language;
        var cards = new WrapPanel();
        foreach (Aion2SpeciesKnowledge k in species.OrderBy(s => s.SpeciesId))
        {
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Text(Aion2Artwork.SpeciesName(k.SpeciesId, language).ToUpperInvariant(), 11, FontWeights.Bold, Res("Brush.TextMuted")));
            TextBlock level = Text($"Level {k.Level}", 12.5, FontWeights.SemiBold);
            Grid.SetColumn(level, 1);
            head.Children.Add(level);
            stack.Children.Add(head);

            bool maxed = k.Progress == 0 && k.Level >= 10;
            stack.Children.Add(Text(maxed ? "Max." : $"Progress: {k.Progress:N0}", 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 6, 0, 8)));
            foreach (Aion2SpeciesEffect effect in k.Effects.OrderBy(e => e.Page).ThenBy(e => e.Slot))
            {
                (string name, bool percent) = Aion2Artwork.SpeciesStat(effect.StatId, language);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                // The effect names take the colour of their quality, as in the game's pet window and on the website.
                Brush labelBrush = effect.Kind switch
                {
                    1 => Res("Brush.Text"),
                    2 => Solid(Color.FromRgb(0x5B, 0xD3, 0x6B)),
                    3 => Solid(Color.FromRgb(0x4A, 0xA8, 0xFF)),
                    4 => Solid(Color.FromRgb(0xFF, 0xC9, 0x4D)),
                    5 => Solid(Color.FromRgb(0xFF, 0x8A, 0x3D)),
                    _ => Res("Brush.TextMuted"),
                };
                TextBlock label = Text(name, 12.5, brush: labelBrush);
                label.Margin = new Thickness(0, 0, 12, 0);
                TextBlock value = Text(percent ? $"{(effect.Value / 100.0).ToString("F1", CultureInfo.CurrentCulture)} %" : effect.Value.ToString("N0"), 12.5, FontWeights.SemiBold);
                Grid.SetColumn(value, 1);
                row.Children.Add(label);
                row.Children.Add(value);
                stack.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Res("Brush.Border"), Padding = new Thickness(0, 6, 0, 6), Child = row });
            }

            Border card = Card(stack, new Thickness(14, 14, 14, 8), new Thickness(0, 0, 14, 14));
            card.Width = 290;

            // The species' circle sits centred above its table, as on the website.
            var column = new StackPanel();
            Aion2Pet? pet = pets.FirstOrDefault(p => p.SpeciesId == k.SpeciesId);
            if (pet is not null)
            {
                column.Children.Add(PetCircle(pet, Aion2Artwork.SpeciesName(pet.SpeciesId, "en").ToLowerInvariant()));
            }

            column.Children.Add(card);
            cards.Children.Add(column);
        }

        var page = new StackPanel();
        page.Children.Add(cards);
        page.Children.Add(Text("As of the last login or zone change.", 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
        return page;
    }
}
