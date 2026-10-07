using System.ComponentModel;
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

    public PlayerDetailsWindow(string name, string className, string faction,
        IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf, bool heals = false,
        IReadOnlyList<Death>? deaths = null, bool taken = false, IReadOnlyList<DamageEvent>? fightEvents = null)
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
                IconFor(u.SkillId, u.Skill, className)))
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
        FillTargets(damage, nameOf, fightEvents, loc, taken || heals);
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
        CritTileText.Text = hits > 0 ? (100.0 * critHits / hits).ToString("F1") + "%" : "n/a";

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

    /// <summary>The per-target table: one row per monster hit, a total row on top. Hidden in the
    /// healing and taken views, where "target" means something else.</summary>
    private void FillTargets(IReadOnlyList<DamageEvent> damage, Func<int, string?> nameOf, IReadOnlyList<DamageEvent>? fightEvents,
        LocalizationManager loc, bool hide)
    {
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
            long everyone = fightEvents?.Where(e => !e.IsHeal && e.TargetObjectId == group.Key).Sum(e => e.Amount) ?? 0;
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
