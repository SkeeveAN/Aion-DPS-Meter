using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>One skill's contribution for one player, as shown in the details grid.
/// SharePercent is this skill's share of the player's OWN total damage (0-100), not the raid's -
/// it drives the ShareBar under the Total column, mirroring PlayerRow.SharePercent in
/// MainWindow.</summary>
public sealed record SkillRow(string Skill, int Hits, double CritRate, long Total, long Min, long Max, long Average, double SharePercent,
    System.Windows.Media.ImageSource? Icon = null);

/// <summary>
/// What the meter has gathered about one character: which abilities they used, how often, how hard
/// each one hits, and how much of it critted.
///
/// <para>Opened from the Players grid's context menu. Read-only and a snapshot -- it does not
/// follow the fight, because a table that reshuffles under the cursor while a boss is being killed
/// is unreadable.</para>
/// </summary>
public partial class PlayerDetailsWindow : Window
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
    private static System.Windows.Media.ImageSource? IconFor(int skillId)
    {
        if (Aion2.Protocol.Aion2SkillIcons.PathFor(skillId) is not string path)
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

    public PlayerDetailsWindow(string name, string className, string faction,
        IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf, bool heals = false,
        IReadOnlyList<Death>? deaths = null, bool taken = false)
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
        ThemedChrome.Apply(this);
        DataContext = new { ClassName = className, Faction = faction };

        HeaderText.Text = name;

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
                IconFor(u.SkillId)))
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

        CritNoteText.Text = "Crit rates are read straight from the game server's hit data, exact for every player.";
        CritNoteText.Visibility = heals ? Visibility.Collapsed : Visibility.Visible;

        // Taken: the deaths in this fight, each with its killing blow, where the crit note was.
        if (taken)
        {
            CritNoteText.Text = deaths is { Count: > 0 }
                ? string.Join("\n", deaths.Select(d => "☠ " + d.At.ToLocalTime().ToString("HH:mm:ss") + "  " + (d.KillingBlow is DamageEvent blow
                    ? string.Format(loc["Details.KilledBy"], nameOf(blow.SourceObjectId) ?? loc["Details.Monster"], blow.Skill ?? "?", blow.Amount.ToString("N0"))
                    : loc["Details.Died"])))
                : loc["Details.NoDeath"];
        }
    }
}
