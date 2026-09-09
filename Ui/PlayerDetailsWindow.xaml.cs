using System.Windows;
using AionSniffer.Combat;

namespace AionSniffer.Ui;

/// <summary>One skill's contribution for one player, as shown in the details grid.</summary>
public sealed record SkillRow(string Skill, int Hits, double CritRate, long Total, long Min, long Max, long Average);

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
        var rows = SkillBreakdown.For(events, trustLoggedFlag: isLocalPlayer)
            .Select(u => new SkillRow(
                u.Skill,
                u.Hits,
                100.0 * u.CritHits / u.Hits,
                u.Total,
                u.Min,
                u.Max,
                (long)Math.Round((double)u.Total / u.Hits)))
            .ToList();

        SkillsGrid.ItemsSource = rows;

        long total = rows.Sum(r => r.Total);
        int hits = rows.Sum(r => r.Hits);
        var targets = damage.Select(e => nameOf(e.TargetObjectId)).Where(n => n is not null).Distinct().Count();
        SummaryText.Text = $"{className} · {total:N0} damage over {hits:N0} hits, {rows.Count} abilities, {targets} targets";

        CritNoteText.Text = isLocalPlayer
            ? "Crit rates are read straight from your own log, where Aion flags them reliably."
            : "Crit rates are ESTIMATED from the damage spread: a crit lands for about 2,3x a normal hit. "
              + "Aion only flags crits reliably in the log of the player who scored them -- another client "
              + "records roughly half of them. Validated at 95,8% accuracy against a log where every crit "
              + "was flagged, with a tendency to overstate by around 3 percentage points. Abilities used "
              + "fewer than 6 times are left at 0%, since a handful of hits cannot show the two clusters.";
    }
}
