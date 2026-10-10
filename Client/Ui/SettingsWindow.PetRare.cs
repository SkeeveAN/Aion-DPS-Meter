using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The Rare tab of "Shown pets": the pet mobs with few spawn points, by map (Verteron, Altgard, Abyss), then the instances, the world bosses and the pets with
/// no known place. Left the mobs with their number of spawn points, right the places on the map (cut-outs) or the instances. Information only; nothing is
/// switched here. The two sliders are kept in the settings; what "rare" means is in <see cref="Aion2PetRare"/>.
/// </summary>
public partial class SettingsWindow
{
    private static readonly string[] RareGroups = { "verteron", "altgard", "abyss", Aion2PetRare.Instances, Aion2PetRare.WorldBosses, Aion2PetRare.Unknown };
    private readonly HashSet<string> _rareClosed = new();
    private IReadOnlyDictionary<string, List<PetRareEntry>> _rare = new Dictionary<string, List<PetRareEntry>>();
    private string? _rareKey;
    private bool _rareReady;
    private bool _rareShown;

    private void InitPetRare(MeterSettings settings)
    {
        PetRareMaxSlider.Value = Math.Clamp(settings.PetRareMax, 1, 60);
        PetRareInstanceSlider.Value = Math.Clamp(settings.PetRareInstanceMin, 1, 200);
        PetRareLeftColumn.Width = new GridLength(Math.Clamp(settings.PetRegionListWidth, 120, 600));
        _rareReady = true;
        ShowPetRareLimits();
    }

    private void ShowPetRareLimits()
    {
        var loc = LocalizationManager.Instance;
        PetRareMaxText.Text = string.Format(loc["Settings.PetRare.Max"], (int)PetRareMaxSlider.Value);
        PetRareInstanceText.Text = string.Format(loc["Settings.PetRare.Instance"], (int)PetRareInstanceSlider.Value);
    }

    /// <summary>The tab is opened: the lists are made when first shown (the spawn points of every map are read for it).</summary>
    private void ShowPetRare()
    {
        if (!_rareShown)
        {
            _rareShown = true;
            RefreshPetRare();
        }
    }

    private void RefreshPetRareForLanguage()
    {
        if (_rareReady)
        {
            ShowPetRareLimits();
            if (_rareShown)
            {
                RefreshPetRare();
            }
        }
    }

    private void OnPetRareSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_rareReady)
        {
            return;
        }

        ShowPetRareLimits();
        if (_rareShown)
        {
            RefreshPetRare();
        }
    }

    private void RefreshPetRare()
    {
        _rare = Aion2PetRare.Compute((int)PetRareMaxSlider.Value, (int)PetRareInstanceSlider.Value);
        var language = LocalizationManager.Instance.Language;
        var comparer = RegionNameComparer(language);
        foreach (string group in _rare.Keys.ToList())
        {
            _rare[group].Sort((a, b) => a.Count != b.Count ? a.Count.CompareTo(b.Count) : comparer.Compare(RareName(a), RareName(b)));
        }

        var all = RareGroups.SelectMany(g => _rare.GetValueOrDefault(g) ?? new()).ToList();
        if (_rareKey is null || all.All(e => e.Key != _rareKey))
        {
            _rareKey = (RareGroups.Select(g => _rare.GetValueOrDefault(g)?.FirstOrDefault()).FirstOrDefault(e => e is not null))?.Key;
        }

        BuildPetRareTree();
        ShowPetRareDetail();
    }

    private string RareName(PetRareEntry e)
    {
        string language = LocalizationManager.Instance.Language;
        return e.NpcId == 0 ? Aion2Pets.PetName(e.PetId, language) ?? $"#{e.PetId}" : Aion2Npcs.NameOf(e.NpcId, language) ?? $"#{e.NpcId}";
    }

    /// <summary>Under the mob name: its pet; for a pet without a place the mobs the game lists for it.</summary>
    private string RareSubtitle(PetRareEntry e)
    {
        var loc = LocalizationManager.Instance;
        if (e.NpcId != 0)
        {
            return Aion2Pets.PetName(e.PetId, loc.Language) ?? $"#{e.PetId}";
        }

        var mobs = Aion2Pets.NpcsOfPet(e.PetId).Select(n => Aion2Npcs.NameOf(n, loc.Language)).Where(n => n is not null).Distinct().Take(3).ToList();
        return mobs.Count == 0 ? loc["Settings.PetRare.NoMob"] : string.Join(", ", mobs);
    }

    /// <summary>The name of a group: the three maps by the names of the Region tab, the others by their own texts.</summary>
    private static string RareGroupName(string group) => group is Aion2PetRare.Instances or Aion2PetRare.WorldBosses or Aion2PetRare.Unknown
        ? LocalizationManager.Instance["Settings.PetRare.Group." + group]
        : RegionGroupName(group);

    /// <summary>For layout checks: chooses the first line whose key starts with the text.</summary>
    internal void PickPetRare(string? prefix)
    {
        var entry = prefix is null ? null : RareGroups.SelectMany(g => _rare.GetValueOrDefault(g) ?? new()).FirstOrDefault(e => e.Key.StartsWith(prefix, StringComparison.Ordinal));
        if (entry is not null)
        {
            _rareKey = entry.Key;
            foreach (string map in new[] { "verteron", "altgard", "abyss" }.Where(m => entry.Category != m))
            {
                _rareClosed.Add(map); // so the list shows the part with the chosen line
            }

            BuildPetRareTree();
            ShowPetRareDetail();
        }
    }

    private void BuildPetRareTree()
    {
        PetRareTree.Children.Clear();
        foreach (string group in RareGroups)
        {
            var entries = _rare.GetValueOrDefault(group) ?? new();
            bool closed = _rareClosed.Contains(group);
            var arrow = new TextBlock { Text = closed ? "▸" : "▾", FontSize = 10, Width = 14, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var title = new TextBlock { Text = RareGroupName(group), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var count = new TextBlock { Text = entries.Count.ToString(CultureInfo.CurrentCulture), FontSize = 11 };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var badge = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 1), Child = count, VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var head = new DockPanel { Margin = new Thickness(2, 7, 4, 5), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
            head.Children.Add(arrow);
            head.Children.Add(title);
            string groupId = group;
            head.MouseLeftButtonDown += (_, _) =>
            {
                if (!_rareClosed.Remove(groupId))
                {
                    _rareClosed.Add(groupId);
                }

                BuildPetRareTree();
            };
            PetRareTree.Children.Add(head);
            if (closed)
            {
                continue;
            }

            if (group is Aion2PetRare.Instances or Aion2PetRare.WorldBosses)
            {
                AddRareSubGroups(group, entries);
                continue;
            }

            foreach (var entry in entries)
            {
                PetRareTree.Children.Add(RareItem(entry, 14));
            }
        }
    }

    /// <summary>The instances (by instance name) and the world bosses (by map) are grouped one level further.</summary>
    private void AddRareSubGroups(string group, List<PetRareEntry> entries)
    {
        var language = LocalizationManager.Instance.Language;
        string SubName(PetRareEntry e) => e.SubNames?.GetValueOrDefault(language) ?? e.SubNames?.GetValueOrDefault("en") ?? RareGroupName(e.SubKey ?? "");
        string[] firstMaps = { "verteron", "altgard", "abyss" };
        var comparer = RegionNameComparer(language);
        var subs = entries.GroupBy(e => e.SubKey ?? "").Select(g => (Key: g.Key, Name: SubName(g.First()), Items: g.ToList()))
            .OrderBy(g => group == Aion2PetRare.WorldBosses && Array.IndexOf(firstMaps, g.Key) >= 0 ? Array.IndexOf(firstMaps, g.Key) : firstMaps.Length)
            .ThenBy(g => g.Name, comparer).ToList();
        foreach (var sub in subs)
        {
            string id = group + "/" + sub.Key;
            bool closed = _rareClosed.Contains(id);
            var arrow = new TextBlock { Text = closed ? "\u25B8" : "\u25BE", FontSize = 10, Width = 14, VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var title = new TextBlock { Text = sub.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var count = new TextBlock { Text = sub.Items.Count.ToString(CultureInfo.CurrentCulture), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            var head = new DockPanel { Margin = new Thickness(14, 4, 4, 3), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(arrow);
            head.Children.Add(title);
            head.MouseLeftButtonDown += (_, _) =>
            {
                if (!_rareClosed.Remove(id))
                {
                    _rareClosed.Add(id);
                }

                BuildPetRareTree();
            };
            PetRareTree.Children.Add(head);
            if (!closed)
            {
                foreach (var entry in sub.Items)
                {
                    PetRareTree.Children.Add(RareItem(entry, 28));
                }
            }
        }
    }

    private Border RareItem(PetRareEntry entry, double indent)
    {
        bool selected = entry.Key == _rareKey;
        var name = new TextBlock { Text = RareName(entry), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        name.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.Accent" : "Brush.Text");
        var pet = new TextBlock { Text = RareSubtitle(entry), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 };
        pet.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        var count = new TextBlock { Text = $"({entry.Count})", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        var texts = new StackPanel();
        texts.Children.Add(name);
        texts.Children.Add(pet);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);
        row.Children.Add(texts);
        var item = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(indent, 0, 0, 1), Cursor = Cursors.Hand, Child = row,
            BorderThickness = new Thickness(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        if (selected)
        {
            item.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            item.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        string key = entry.Key;
        item.MouseLeftButtonDown += (_, _) =>
        {
            _rareKey = key;
            BuildPetRareTree();
            ShowPetRareDetail();
        };
        return item;
    }

    private void ShowPetRareDetail()
    {
        var loc = LocalizationManager.Instance;
        var entry = RareGroups.SelectMany(g => _rare.GetValueOrDefault(g) ?? new()).FirstOrDefault(e => e.Key == _rareKey);
        PetRareInfo.Inlines.Clear();
        PetRareHint.Text = "";
        PetRareView.Content = null;
        if (entry is null)
        {
            PetRareInfo.Inlines.Add(new Run(loc["Settings.PetRare.Nothing"]));
            return;
        }

        string pet = Aion2Pets.PetName(entry.PetId, loc.Language) ?? $"#{entry.PetId}";
        var subtle = (Brush)FindResource("Brush.TextSubtle");
        void Line(string text) => PetRareInfo.Inlines.Add(new Run(text) { Foreground = subtle });
        if (entry.NpcId == 0)
        {
            PetRareInfo.Inlines.Add(new Run(pet) { FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Brush.Accent") });
            PetRareInfo.Inlines.Add(new LineBreak());
            Line(loc["Settings.PetRare.UnknownInfo"]);
            PetRareView.Content = RareRows(Aion2Pets.NpcsOfPet(entry.PetId).Select(n => (Aion2Npcs.NameOf(n, loc.Language) ?? $"#{n}", "0")).Distinct().ToList());
            return;
        }

        PetRareInfo.Inlines.Add(new Run(RareName(entry)) { FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Brush.Accent") });
        Line($" · {string.Format(loc["Settings.PetRare.Pet"], pet)}");
        PetRareInfo.Inlines.Add(new LineBreak());
        var map = entry.MapKey is null ? null : Aion2Maps.All.FirstOrDefault(m => m.Key == entry.MapKey);
        string where = map is null ? "" : RareGroupName(map.Key);
        var parts = new List<string>();
        if (entry.Category == Aion2PetRare.WorldBosses)
        {
            parts.Add(loc["Settings.PetRare.WorldBoss"]);
            parts.Add(string.Format(loc["Settings.PetRare.Points"], entry.Count));
        }
        else if (entry.Category == Aion2PetRare.Instances)
        {
            parts.Add(string.Format(loc["Settings.PetRare.InThisInstance"], entry.Count));
            parts.Add(entry.PetOnOtherMaps.Count == 0 ? loc["Settings.PetRare.NotOnMaps"] : string.Format(loc["Settings.PetRare.OnOtherMaps"], MapCounts(entry)));
        }
        else
        {
            parts.Add(string.Format(loc["Settings.PetRare.OnMap"], where, entry.Count));
            if (entry.PetOnMap > entry.Count)
            {
                parts.Add(string.Format(loc["Settings.PetRare.PetOnMap"], where, entry.PetOnMap));
            }

            if (entry.PetOnOtherMaps.Count > 0)
            {
                parts.Add(string.Format(loc["Settings.PetRare.OnOtherMaps"], MapCounts(entry)));
            }

            if (entry.PetInstancePoints > 0)
            {
                parts.Add(string.Format(loc["Settings.PetRare.InInstances"], entry.PetInstancePoints));
            }
        }

        Line(string.Join(" · ", parts));
        if (map is not null)
        {
            PetRareView.Content = MapSnippets.Build(map, entry.Spots, entry.OtherSpots, loc.Language);
            int places = MapSnippets.PlaceCount(map, entry.Spots);
            PetRareHint.Text = loc["Settings.PetRare.MapHint"] + (places > 1 ? " " + string.Format(loc["Settings.PetRare.Places"], places) : "");
        }
        else
        {
            PetRareView.Content = RareRows(entry.Places.Select(p => (p.NameIn(loc.Language), p.Count.ToString(CultureInfo.CurrentCulture))).ToList());
            PetRareHint.Text = entry.Category == Aion2PetRare.Instances ? loc["Settings.PetRare.PlacesHint"] : "";
        }
    }

    /// <summary>"Verteron 1, Altgard 2": the pet's spawn points on the other maps.</summary>
    private static string MapCounts(PetRareEntry entry) =>
        string.Join(", ", entry.PetOnOtherMaps.Select(kv => $"{RareGroupName(kv.Key)} {kv.Value}"));

    private UIElement RareRows(IReadOnlyList<(string Name, string Count)> rows)
    {
        var panel = new StackPanel();
        foreach (var (name, count) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 12, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var number = new TextBlock { Text = count, VerticalAlignment = VerticalAlignment.Center };
            number.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            Grid.SetColumn(number, 1);
            row.Children.Add(text);
            row.Children.Add(number);
            panel.Children.Add(row);
        }

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }
}
