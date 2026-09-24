using System.Windows;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>One skill's contribution for one player, as shown in the details grid.
/// SharePercent is this skill's share of the player's OWN total damage (0-100), not the raid's -
/// it drives the ShareBar under the Total column, mirroring PlayerRow.SharePercent in
/// MainWindow.</summary>
public sealed record SkillRow(string Skill, int Hits, double CritRate, long Total, long Min, long Max, long Average, double SharePercent);

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
    public PlayerDetailsWindow(string name, string className, string faction, bool isLocalPlayer,
        IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf)
    {
        InitializeComponent();
        DataContext = new { ClassName = className, Faction = faction };

        HeaderText.Text = name;

        var damage = events.Where(e => !e.IsHeal).ToList();

        // The local player's client flags its own crits properly; nobody else's does. Estimating
        // over a known answer would only add error, so the flag wins where it is trustworthy.
        var breakdown = SkillBreakdown.For(events, trustLoggedFlag: isLocalPlayer).ToList();
        long total = breakdown.Sum(u => u.Total);
        var rows = breakdown
            .Select(u => new SkillRow(
                u.Skill,
                u.Hits,
                100.0 * u.CritHits / u.Hits,
                u.Total,
                u.Min,
                u.Max,
                (long)Math.Round((double)u.Total / u.Hits),
                total > 0 ? 100.0 * u.Total / total : 0))
            .ToList();

        SkillsGrid.ItemsSource = rows;

        int hits = rows.Sum(r => r.Hits);
        var targets = damage.Select(e => nameOf(e.TargetObjectId)).Where(n => n is not null).Distinct().Count();
        SummaryText.Text = $"{className} · {rows.Count} abilities, {targets} targets";

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

        CritNoteText.Text = isLocalPlayer
            ? "Crit rates are read straight from your own log, where Aion flags them reliably."
            : "Crit rates are ESTIMATED from the damage spread: a crit lands for about 2,3x a normal hit. "
              + "Aion only flags crits reliably in the log of the player who scored them -- another client "
              + "records roughly half of them. Validated at 95,8% accuracy against a log where every crit "
              + "was flagged, with a tendency to overstate by around 3 percentage points. Abilities used "
              + "fewer than 6 times are left at 0%, since a handful of hits cannot show the two clusters.";
    }
}
