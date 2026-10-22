using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>One skill's contribution for one player, as shown in the details grid.
/// SharePercent is this skill's share of the player's OWN total damage (0-100), not the raid's -
/// it drives the ShareBar under the Total column, mirroring PlayerRow.SharePercent in
/// MainWindow.</summary>
/// <summary>One monster the player hit: their damage on it, their DPS over their time on it and
/// their share of everything dealt to it (SharePercent, 0 when the fight's other damage is not known).</summary>
public sealed record TargetRow(string Name, long Total, double Dps, double SharePercent)
{
    public string ShareDisplay => SharePercent > 0 ? SharePercent.ToString("F1", System.Globalization.CultureInfo.CurrentCulture) + "%" : "";
}

public sealed record SkillRow(string Skill, int Hits, double CritRate, long Total, long Min, long Max, long Average, double SharePercent,
    System.Windows.Media.ImageSource? Icon = null, HitKindCounts? Kinds = null)
{
    private string Of(int count) => Kinds is { Known: > 0 } k ? k.Rate(count).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "%" : "–";

    /// <summary>The share of this skill's direct hits that landed from behind / from the front / perfect / double / as several strikes (from the hit data; "–" when it said nothing).</summary>
    public string BackDisplay => Kinds is null ? "–" : Of(Kinds.Back);

    public string FrontDisplay => Kinds is null ? "–" : Of(Kinds.Front);

    public string PerfectDisplay => Kinds is null ? "–" : Of(Kinds.Perfect);

    public string DoubleDisplay => Kinds is null ? "–" : Of(Kinds.Double);

    public string MultiDisplay => Kinds is null ? "–" : Of(Kinds.Multi);
}

/// <summary>
/// What the meter has gathered about one character: which abilities they used, how often, how hard
/// each one hits, and how much of it critted.
///
/// <para>Opened from the Players grid's context menu. Read-only and a snapshot -- it does not
/// follow the fight, because a table that reshuffles under the cursor while a boss is being killed
/// is unreadable.</para>
/// </summary>
public partial class PlayerDetailsView : UserControl
{
    // Each column's own header text; the sorted one gets an arrow behind it.
    private readonly Dictionary<DataGridColumn, string> _headers = new();

    /// <summary>A click on a header sorts by it: numbers largest first, the skill name A to Z;
    /// a second click reverses. The arrow shows which column and which way.</summary>
    private void OnSkillsSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        bool byName = e.Column.SortMemberPath == "Skill";
        ListSortDirection direction = e.Column.SortDirection switch
        {
            ListSortDirection.Descending => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => byName ? ListSortDirection.Ascending : ListSortDirection.Descending,
        };
        ApplySort(e.Column, direction);
    }

    private void ApplySort(DataGridColumn column, ListSortDirection direction)
    {
        if (string.IsNullOrEmpty(column.SortMemberPath) || SkillsGrid.ItemsSource is null)
        {
            return;
        }

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(SkillsGrid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
        foreach (DataGridColumn other in SkillsGrid.Columns)
        {
            other.SortDirection = null;
            other.Header = _headers.GetValueOrDefault(other, other.Header as string ?? "");
        }

        column.SortDirection = direction;
        column.Header = _headers.GetValueOrDefault(column, "") + (direction == ListSortDirection.Descending ? " ▼" : " ▲");
    }
    /// <summary>The skill's icon (see Aion2.Protocol.Aion2SkillIcons), decoded once at its small
    /// size; null when the skill has none.</summary>
    private static System.Windows.Media.ImageSource? IconFor(int skillId, string? skillName, string? className)
    {
        // A fight read back from the history carries no skill ids, only the names: the name finds the icon then.
        if ((Aion2.Protocol.Aion2SkillIcons.PathFor(skillId) ?? Aion2.Protocol.Aion2SkillIcons.PathForName(skillName, className)) is not string path)
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>What the "player appeared" frame told of the player: a chip for the 10 against 10 arena, the item level and the combat power as tiles, and the worn
    /// pieces in three columns. In the arena the combat power is not shown at all (the game evens it out, and so the skill levels).</summary>
    private void BuildSeenPanel(PlayerSeenInfo? seen)
    {
        SeenHost.Children.Clear();
        if (seen is null || (seen.ItemLevel is null && seen.CombatPower is null && seen.Worn.Count == 0 && seen.RemoteGear is not { Count: > 0 }))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        TabPlayerButton.Visibility = Visibility.Visible;
        var tiles = new StackPanel { Orientation = Orientation.Horizontal };
        if (seen.Equalized)
        {
            tiles.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Brush.Panel"), BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Accent"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = loc["Details.Seen.Chip10v10"], FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("Brush.Accent") },
            });
        }

        Border Tile(string label, string value, bool accent)
        {
            var valueText = new TextBlock { Text = value, Style = (Style)FindResource("StatTileValue") };
            if (accent)
            {
                valueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            }

            return new Border { Style = (Style)FindResource("StatTile"), Child = new StackPanel { Children = { new TextBlock { Text = label, Style = (Style)FindResource("StatTileLabel") }, valueText } } };
        }

        if (seen.ItemLevel is int itemLevel)
        {
            tiles.Children.Add(Tile(loc["Details.Seen.ItemLevel"], itemLevel.ToString("N0", CultureInfo.CurrentCulture), false));
        }

        if (seen.Equalized)
        {
            tiles.Children.Add(Tile(loc["Details.Seen.CombatPower"], loc["Details.Seen.Evened"], true));
        }
        else if (seen.CombatPower is int power)
        {
            tiles.Children.Add(Tile(loc["Details.Seen.CombatPower"], power.ToString("N0", CultureInfo.CurrentCulture), false));
        }

        SeenHost.Children.Add(tiles);
        // The rest of the equipment (and the exact numbers) as aiondps.com holds it, when it has been asked and knows the player; else the pieces of the frame.
        var lines = new List<(string Slot, string Name, int Enchant)>();
        if (seen.RemoteGear is { Count: > 0 } remoteGear)
        {
            string[] order = { "MainHand", "SubHand", "Helmet", "Shoulder", "Torso", "Pants", "Gloves", "Boots", "Cape", "Belt", "Necklace", "Earring", "Ring", "Bracelet", "Amulet", "Rune", "Arcana", "Brooch", "Pendant" };
            foreach (var piece in remoteGear.Select(g => (Info: Aion2.Protocol.Aion2ItemCatalog.Find(g.ItemId), Gear: g)).OrderBy(g => Array.IndexOf(order, g.Info?.Slot ?? "")).ThenBy(g => g.Gear.SlotIndex))
            {
                string category = piece.Info?.Slot ?? "";
                lines.Add((category.Length > 0 ? loc["Details.Seen.Cat." + category] : "?", piece.Info?.Name ?? piece.Gear.ItemId.ToString(CultureInfo.InvariantCulture), piece.Gear.Enchant));
            }
        }
        else
        {
            foreach (Aion2.Aion2WornItem piece in seen.Worn)
            {
                lines.Add((loc[$"Details.Seen.Slot{piece.SlotIndex}"], Aion2.Protocol.Aion2ItemCatalog.Find(piece.ItemId)?.Name ?? piece.ItemId.ToString(CultureInfo.InvariantCulture), piece.EnchantTotal));
            }
        }

        if (lines.Count > 0)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 0) };
            foreach ((string slot, string itemName, int enchant) in lines)
            {
                var line = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 12, 1), ToolTip = itemName };
                line.Inlines.Add(new System.Windows.Documents.Run(slot + "  ") { Foreground = (System.Windows.Media.Brush)FindResource("Brush.TextMuted") });
                line.Inlines.Add(new System.Windows.Documents.Run(itemName));
                if (enchant > 0)
                {
                    line.Inlines.Add(new System.Windows.Documents.Run($"  +{enchant} {loc["Details.Seen.Total"]}") { Foreground = (System.Windows.Media.Brush)FindResource("Brush.Accent") });
                    line.ToolTip = itemName + " - " + loc["Details.Seen.TotalTip"];
                }

                grid.Children.Add(line);
            }

            SeenHost.Children.Add(grid);
        }

        if (seen.Equalized)
        {
            SeenHost.Children.Add(new TextBlock { Text = loc["Details.Seen.EvenedNote"], FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Brush.TextMuted") });
        }
    }

    /// <summary>Asks aiondps.com what it holds of the player (his full equipment with the exact enchant, filled by NC's page) and redraws the block with it. Only for players the
    /// game told of and whose server is known; a failure or an unknown player leaves the block as it was. Never in the headless modes (no dispatcher there).</summary>
    private async void StartRemoteLoad(PlayerSeenInfo? seen, string name)
    {
        if (seen is null || seen.ServerId <= 0 || Aion2.Protocol.Aion2Servers.NameOf(seen.ServerId) is not { Length: > 0 } serverName)
        {
            return;
        }

        string fingerprint = "aion2:" + System.Text.RegularExpressions.Regex.Replace(serverName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        Upload.RemoteLookup result = await Upload.RemoteCharacterClient.GetAsync(name, fingerprint);
        if (result.Status != Upload.RemoteLookupStatus.Found || result.Character is not { Gear.Count: > 0 } found)
        {
            return;
        }

        BuildSeenPanel(seen with { RemoteGear = found.Gear });
    }

    public PlayerDetailsView(string name, string className, string faction,
        IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf, bool heals = false,
        IReadOnlyList<Death>? deaths = null, bool taken = false, IReadOnlyList<DamageEvent>? fightEvents = null,
        FightExtras? extras = null, int playerId = -1, DateTime? fightStart = null, DateTime? fightEnd = null, ReplayData? replay = null, PlayerSeenInfo? seen = null)
    {
        // Taken: the hits this player took, one row per attacker and attack ("Transcendent
        // Bakarma : Attack"); an attacker with no name of its own is "Monster".
        var loc = LocalizationManager.Instance;
        if (taken)
        {
            string monster = loc["Details.Monster"];
            events = events.Select(e => e with { Skill = $"{nameOf(e.SourceObjectId) ?? monster} : {e.Skill ?? "?"}", SkillId = 0 }).ToList();
        }

        InitializeComponent();
        DataContext = new { ClassName = className, Faction = faction };

        HeaderText.Text = name;
        BuildSeenPanel(seen);
        StartRemoteLoad(seen, name);

        // The half the main window is showing: damage, or heals in heal mode.
        var damage = events.Where(e => e.IsHeal == heals).ToList();
        if (heals)
        {
            AmountTileLabel.Text = LocalizationManager.Instance["Details.Tile.Heal"];
            RateTileLabel.Text = LocalizationManager.Instance["Details.Tile.Hps"];
        }
        else if (taken)
        {
            AmountTileLabel.Text = loc["Details.Tile.Taken"];
            RateTileLabel.Text = loc["Details.Tile.Dtps"];
        }

        var breakdown = SkillBreakdown.For(events, heals).ToList();
        long total = breakdown.Sum(u => u.Total);
        var rows = breakdown
            .Select(u => new SkillRow(
                Aion2.Protocol.Aion2SkillNames.Display(u.Skill),
                u.Hits,
                100.0 * u.CritHits / u.Hits,
                u.Total,
                u.Min,
                u.Max,
                (long)Math.Round((double)u.Total / u.Hits),
                total > 0 ? 100.0 * u.Total / total : 0,
                IconFor(u.SkillId, u.Skill, className), u.Kinds))
            .ToList();

        SkillsGrid.ItemsSource = rows;
        foreach (DataGridColumn column in SkillsGrid.Columns)
        {
            _headers[column] = column.Header as string ?? "";
        }

        if (SkillsGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "Total") is DataGridColumn totalColumn)
        {
            ApplySort(totalColumn, ListSortDirection.Descending);
        }

        int hits = rows.Sum(r => r.Hits);
        FillTargets(damage, nameOf, fightEvents, loc, taken, heals);
        DrawCurve(damage);
        var targets = damage.Select(e => nameOf(e.TargetObjectId)).Where(n => n is not null).Distinct().Count();
        SummaryText.Text = taken
            ? $"{className} · ☠ {deaths?.Count ?? 0} · {string.Format(loc["Details.Attacks"], rows.Count)}"
            : $"{className} · {rows.Count} abilities, {targets} targets";

        // Wall-clock, first hit to last hit -- same definition as DpsCalculator.AllDpsWallClock's
        // "ALL" view (that method itself isn't reusable here: it filters events by sourceObjectId,
        // but `damage` is already this one player's events with no object id available to filter
        // by). Null below its one-hit/zero-duration floor, same reasoning as that method's own
        // remarks: a rate over no elapsed time is not a number, and showing the raw total instead
        // would read as a real rate rather than as "undefined".
        double? seconds = damage.Count > 1
            ? (damage.Max(e => e.Timestamp) - damage.Min(e => e.Timestamp)).TotalSeconds
            : null;
        if (seconds is <= 0)
        {
            seconds = null;
        }

        DmgTileText.Text = total.ToString("N0");
        DpsTileText.Text = seconds is double s ? (total / s).ToString("N0") : "n/a";
        TimeTileText.Text = seconds is double s2 ? TimeSpan.FromSeconds(s2).ToString(@"mm\:ss") : "n/a";
        HitsPerSecTileText.Text = seconds is double s3 ? (hits / s3).ToString("F1") : "n/a";
        HitsTileText.Text = hits.ToString("N0");
        int critHits = breakdown.Sum(u => u.CritHits);
        // how the hits landed over the whole fight: only when the hit data said anything
        var allKinds = HitKindCounts.Of(damage.Where(e => !e.IsTick).ToList());
        if (!heals && !taken && allKinds.Known > 0)
        {
            SummaryText.Text += $" · {loc["Details.Col.Back"]} {allKinds.Rate(allKinds.Back):0.#}% · {loc["Details.Col.Perfect"]} {allKinds.Rate(allKinds.Perfect):0.#}% · {loc["Details.Col.Double"]} {allKinds.Rate(allKinds.Double):0.#}% · {loc["Details.Col.Multi"]} {allKinds.Rate(allKinds.Multi):0.#}%";
        }

        CritTileText.Text = hits > 0 ? (100.0 * critHits / hits).ToString("F1") + "%" : "–";

        // What a summon of the player did (a Spiritmaster's spirits, a Sorcerer's walls), when any of it was counted for him: its share of the damage shown.
        long viaSummon = damage.Where(e => e.ViaSummon).Sum(e => e.Amount);
        if (!heals && !taken && viaSummon > 0 && total > 0)
        {
            var summonValue = new TextBlock { Text = $"{100.0 * viaSummon / total:0.#}%", Style = (Style)FindResource("StatTileValue") };
            summonValue.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            var tile = new Border
            {
                Style = (Style)FindResource("StatTile"),
                ToolTip = $"{loc["Details.Tile.Summons"]}: {viaSummon:N0}",
                Child = new StackPanel { Children = { new TextBlock { Text = loc["Details.Tile.Summons"], Style = (Style)FindResource("StatTileLabel") }, summonValue } },
            };
            TilesPanel.Children.Add(tile);
        }

        CritNoteText.Text = loc["Details.Crit.Note"] + " " + loc["Details.Kinds.Note"];
        CritNoteText.Visibility = heals ? Visibility.Collapsed : Visibility.Visible;

        // Taken: the deaths in this fight, each with its killing blow, where the crit note was.
        if (taken)
        {
            string Span(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";
            CritNoteText.Text = deaths is { Count: > 0 }
                ? string.Format(loc["Details.DeathsTotal"], deaths.Count, Span(TimeSpan.FromTicks(deaths.Where(d => d.RevivedAt is not null).Sum(d => (d.RevivedAt!.Value - d.At).Ticks))))
                    + "\n" + string.Join("\n", deaths.Select(d => "☠ " + d.At.ToLocalTime().ToString("HH:mm:ss") + "  " + (d.KillingBlow is DamageEvent blow
                        ? string.Format(loc["Details.KilledBy"], nameOf(blow.SourceObjectId) ?? loc["Details.Monster"], blow.Skill ?? "?", blow.Amount.ToString("N0"))
                        : loc["Details.Died"])
                        + "  ·  " + (d.RevivedAt is DateTime back ? string.Format(loc["Details.DeadFor"], Span(back - d.At)) : loc["Details.NotRevived"])))
                : loc["Details.NoDeath"];
        }

        _replay = replay;
        ReplayButton.Visibility = replay is { HasData: true } && !taken ? Visibility.Visible : Visibility.Collapsed;
        InitRotation(extras, playerId, fightStart, fightEnd, events.Where(e => e.IsHeal == heals).ToList(), nameOf, taken, className);
    }

    private RotationReport? _rotation;
    private ReplayData? _replay;
    private bool _targetsShown, _curveShown;

    /// <summary>The rotation and effect views of this player over the fight: from what the packet stream said besides damage (see <see cref="FightExtras"/>).
    /// Not in the damage-taken view, where there is no rotation to show.</summary>
    private void InitRotation(FightExtras? extras, int playerId, DateTime? from, DateTime? to, IReadOnlyList<DamageEvent> mine, Func<int, string?> nameOf, bool taken, string className)
    {
        if (taken)
        {
            TabBar.Visibility = Visibility.Collapsed;
            return;
        }

        // the targets table and the DPS curve make room for the rotation and the effects, which need the height
        _targetsShown = TargetsGrid.Visibility == Visibility.Visible;
        _curveShown = CurvePanel.Visibility == Visibility.Visible;
        if (extras is { IsEmpty: false } && playerId >= 0 && from is DateTime start && to is DateTime end && end > start)
        {
            _rotation = RotationAnalysis.Build(extras, playerId, start, end, mine, nameOf);
        }

        var report = _rotation;
        bool rotation = report is { Uses.Count: > 0 };
        RotationBox.Visibility = OpenerRow.Visibility = RotationGrid.Visibility = rotation ? Visibility.Visible : Visibility.Collapsed;
        NoRotationText.Visibility = rotation ? Visibility.Collapsed : Visibility.Visible;
        if (rotation)
        {
            RotationGrid.ItemsSource = report!.Skills.Select(k => new RotationLine(
                k.Name, IconFor(k.SkillId, null, className), k.Uses, k.PerMinute.ToString("0.0"),
                k.AvgGapSeconds > 0 ? k.AvgGapSeconds.ToString("0.0") + " s" : "–", TimeSpan.FromSeconds(k.FirstAt).ToString(@"m\:ss"),
                k.SharePercent > 0 ? k.SharePercent.ToString("0.0") + "%" : "")).ToList();
            foreach (RotationUse use in report.Opener)
            {
                OpenerPanel.Children.Add(new System.Windows.Controls.Image
                {
                    Source = IconFor(use.SkillId, null, className), Width = 24, Height = 24, Margin = new Thickness(0, 0, 3, 0),
                    ToolTip = Aion2.Protocol.Aion2SkillNames.Display(use.Name) + " · " + TimeSpan.FromSeconds(use.At).ToString(@"m\:ss\.f"),
                });
            }

            Timeline.Show(report, DpsPerSecond(mine, start: from!.Value, report.DurationSeconds));
            Timeline.Configure(ShowPassivesBox.IsChecked == true, ShowDpsBox.IsChecked == true, ShowStaggerBox.IsChecked == true);
        }

        bool buffs = report is { Buffs.Count: > 0 } || report is { Debuffs.Count: > 0 };
        BuffsGrid.Visibility = buffs ? Visibility.Visible : Visibility.Collapsed;
        NoBuffsText.Visibility = buffs ? Visibility.Collapsed : Visibility.Visible;
        if (buffs)
        {
            BuffsGrid.ItemsSource = report!.Buffs.Select(b => new BuffLine(
                b.Name, IconFor(b.SkillId, null, className), b.UptimePercent.ToString("0.0") + "%", b.FromYouPercent > 0 ? b.FromYouPercent.ToString("0.0") + "%" : "–", b.Applications, b.Givers)).ToList();
        }

        // the effects on the bosses (debuffs): what stayed on them, and how much of it came from this player
        bool debuffs = report is { Debuffs.Count: > 0 };
        DebuffsGrid.Visibility = debuffs ? Visibility.Visible : Visibility.Collapsed;
        DebuffsHeader.Visibility = debuffs ? Visibility.Visible : Visibility.Collapsed;
        if (debuffs)
        {
            DebuffsGrid.ItemsSource = report!.Debuffs!.Select(b => new BuffLine(
                b.Name, IconFor(b.SkillId, null, className), b.UptimePercent.ToString("0.0") + "%", b.FromYouPercent > 0 ? b.FromYouPercent.ToString("0.0") + "%" : "–", b.Applications, b.Givers)).ToList();
        }
    }

    /// <summary>The player's damage per second as the average of the last five seconds, from the start of the fight.</summary>
    private static double[] DpsPerSecond(IReadOnlyList<DamageEvent> events, DateTime start, double duration)
    {
        int seconds = (int)Math.Ceiling(duration) + 1;
        var buckets = new double[seconds];
        foreach (DamageEvent e in events.Where(e => !e.IsHeal))
        {
            int i = (int)(e.Timestamp - start).TotalSeconds;
            if (i >= 0 && i < seconds)
            {
                buckets[i] += e.Amount;
            }
        }

        var average = new double[seconds];
        double window = 0;
        for (int i = 0; i < seconds; i++)
        {
            window += buckets[i];
            if (i >= 5)
            {
                window -= buckets[i - 5];
            }

            average[i] = window / Math.Min(5, i + 1);
        }

        return average;
    }

    private void OnReplayClicked(object sender, RoutedEventArgs e)
    {
        if (_replay is not null)
        {
            new FightReplayWindow(_replay) { Owner = Window.GetWindow(this) }.Show();
        }
    }

    private void OnDetailsTabChanged(object sender, RoutedEventArgs e)
    {
        if (TabHost is null)
        {
            return;
        }

        SkillsGrid.Visibility = TabSkillsButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RotationPanel.Visibility = TabRotationButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BuffsPanel.Visibility = TabBuffsButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = TabPlayerButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        bool skills = TabSkillsButton.IsChecked == true;
        TargetsGrid.Visibility = skills && _targetsShown ? Visibility.Visible : Visibility.Collapsed;
        CurvePanel.Visibility = skills && _curveShown ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRotationOptionChanged(object sender, RoutedEventArgs e)
    {
        Timeline?.Configure(ShowPassivesBox.IsChecked == true, ShowDpsBox.IsChecked == true, ShowStaggerBox.IsChecked == true);
    }

    /// <summary>The per-target table: one row per monster hit, a total row on top. Hidden in the
    /// taken view, where "target" means something else. In the healing view it lists the players healed.</summary>
    private void FillTargets(IReadOnlyList<DamageEvent> damage, Func<int, string?> nameOf, IReadOnlyList<DamageEvent>? fightEvents,
        LocalizationManager loc, bool hide, bool heals)
    {
        // The healing view lists the players the healer healed, with the healing they got from this healer.
        if (heals)
        {
            TargetsGrid.Columns[1].Header = loc["Details.Tile.Heal"];
            TargetsGrid.Columns[2].Header = loc["Details.Tile.Hps"];
        }

        if (hide || damage.Count == 0)
        {
            TargetsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        var rows = new List<TargetRow>();
        foreach (var group in damage.GroupBy(e => e.TargetObjectId))
        {
            long total = group.Sum(e => e.Amount);
            double seconds = (group.Max(e => e.Timestamp) - group.Min(e => e.Timestamp)).TotalSeconds;
            long everyone = fightEvents?.Where(e => e.IsHeal == heals && e.TargetObjectId == group.Key).Sum(e => e.Amount) ?? 0;
            rows.Add(new TargetRow(nameOf(group.Key) ?? loc["Details.Monster"], total, seconds > 0 ? total / seconds : 0,
                everyone > 0 ? Math.Min(100.0, 100.0 * total / everyone) : 0));
        }

        rows = rows.OrderByDescending(r => r.Total).ToList();
        long all = rows.Sum(r => r.Total);
        double span = (damage.Max(e => e.Timestamp) - damage.Min(e => e.Timestamp)).TotalSeconds;
        rows.Insert(0, new TargetRow(loc["Details.Col.All"], all, span > 0 ? all / span : 0, 0));
        TargetsGrid.ItemsSource = rows;
        TargetsGrid.Visibility = Visibility.Visible;
    }

    private double[] _curve = Array.Empty<double>();
    private double _curveStep = 1;
    private DateTime _curveStart;

    /// <summary>The damage per second (per few seconds over a long span) from the first to the last
    /// event: the data for the curve under the tables.</summary>
    private void DrawCurve(IReadOnlyList<DamageEvent> events)
    {
        if (events.Count < 2)
        {
            CurvePanel.Visibility = Visibility.Collapsed;
            return;
        }

        DateTime first = events.Min(e => e.Timestamp);
        double span = (events.Max(e => e.Timestamp) - first).TotalSeconds;
        if (span < 2)
        {
            CurvePanel.Visibility = Visibility.Collapsed;
            return;
        }

        _curveStep = Math.Max(1, Math.Ceiling(span / 300));
        var buckets = new double[(int)Math.Floor(span / _curveStep) + 1];
        foreach (DamageEvent e in events)
        {
            buckets[(int)Math.Floor((e.Timestamp - first).TotalSeconds / _curveStep)] += e.Amount / _curveStep;
        }

        _curve = buckets;
        _curveStart = first;
        CurveTitle.Text = LocalizationManager.Instance["Details.Curve"];
        CurveStartText.Text = "00:00";
        CurveEndText.Text = TimeSpan.FromSeconds(span).ToString(span >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
        CurveMaxText.Text = Combat_Compact((long)buckets.Max());
        RedrawCurve();
    }

    private static string Combat_Compact(long value) => PlayerRow.Compact(value);

    private void OnCurveSizeChanged(object sender, SizeChangedEventArgs e) => RedrawCurve();

    private void RedrawCurve()
    {
        CurveCanvas.Children.Clear();
        double width = CurveCanvas.ActualWidth, height = CurveCanvas.ActualHeight;
        if (_curve.Length < 2 || width < 10 || height < 10)
        {
            return;
        }

        double max = Math.Max(_curve.Max(), 1);
        double usable = height - 16;
        var points = new PointCollection();
        for (int i = 0; i < _curve.Length; i++)
        {
            points.Add(new Point(width * i / (_curve.Length - 1), height - 12 - usable * _curve[i] / max));
        }

        var area = new PointCollection(points) { new Point(width, height - 12), new Point(0, height - 12) };
        var fill = new System.Windows.Shapes.Polygon { Points = area, Opacity = 0.18 };
        fill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Accent");
        var line = new System.Windows.Shapes.Polyline { Points = points, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Accent");
        CurveCanvas.Children.Add(fill);
        CurveCanvas.Children.Add(line);
    }
}
