using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AionDPS.Ui;

/// <summary>
/// One row in the main window's player list. Deliberately a plain mutable view model (not a
/// record) so the DataGrid can update fields in place as new DamageEvents arrive, without
/// rebuilding the whole row -- matches how a live meter actually behaves (numbers tick up),
/// not how a one-shot report would.
/// </summary>
public sealed class PlayerRow : INotifyPropertyChanged
{
    private long _damage;
    private double? _dps;
    private long? _relicAp;
    private int _rank;
    private double _sharePercent;
    private long _damageTaken;
    private bool _showShareBar = true;
    private bool _showDamageTaken = true;
    private bool _showRelicAp;
    private string _defenseDisplay = "";
    private string _pvpDisplay = "";

    private string _name = "?";
    private string _className = "?";
    private int _level;
    private string _faction = "";
    private bool _isEnemy;

    public int ObjectId { get; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameDisplay)); }
    }

    private int _deaths;

    /// <summary>Deaths in the fight shown, in the damage-taken mode (0 elsewhere).</summary>
    public int Deaths
    {
        get => _deaths;
        set { _deaths = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameDisplay)); }
    }

    /// <summary>The name, followed by a skull and the count when the player died.</summary>
    public string NameDisplay => Deaths > 0 ? $"{Name}  ☠{(Deaths > 1 ? Deaths.ToString() : "")}" : Name;

    public string ClassName
    {
        get => _className;
        set { _className = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClassBrush)); }
    }

    public int Level
    {
        get => _level;
        set { _level = value; OnPropertyChanged(); }
    }

    /// <summary>"Elyos"/"Asmodian", or empty while nothing has placed this player on a side yet.
    /// Derived, never read from the log -- see Combat/FactionResolver.</summary>
    public string Faction
    {
        get => _faction;
        set { _faction = value; OnPropertyChanged(); }
    }

    /// <summary>Drives the row's background. Kept separate from <see cref="Faction"/> because the
    /// two answer different questions: a faction can be known while the side is not (nobody has
    /// registered a character yet), and a side can be known while the faction has no name.</summary>
    public bool IsEnemy
    {
        get => _isEnemy;
        set { _isEnemy = value; OnPropertyChanged(); }
    }

    public long Damage
    {
        get => _damage;
        set { _damage = value; OnPropertyChanged(); OnPropertyChanged(nameof(DamageCompact)); OnPropertyChanged(nameof(RateOrDamageCompact)); OnPropertyChanged(nameof(TotalOrTakenCompact)); }
    }

    /// <summary>Null renders as "n/a" in the grid -- see DpsCalculator's remarks on why a single
    /// hit (or otherwise zero elapsed time) must not show a fabricated rate.</summary>
    public double? Dps
    {
        get => _dps;
        set { _dps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DpsDisplay)); OnPropertyChanged(nameof(DpsCompact)); OnPropertyChanged(nameof(RateOrHealDisplay)); OnPropertyChanged(nameof(RateOrDamageCompact)); }
    }

    /// <summary>Damage and rate in thousands/millions, for the compact overlay's dense lines.</summary>
    public string DamageCompact => Compact(Damage);

    public string DpsCompact => Dps is double d ? Compact((long)Math.Round(d)) : "-";

    /// <summary>Short form for the compact overlay: 1.24B, 91.60M, 412.3K, 2.8K, 950.</summary>
    public static string Compact(long value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1e9).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (value / 1e6).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (value / 1e3).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
    };

    public string DpsDisplay => Dps is double d ? d.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "n/a";

    /// <summary>AP the relics currently in this player's bag will pay out once exchanged (see
    /// Data/RelicApDatabase) -- shown on its own, not folded into the session's real AP total,
    /// because the two are not the same kind of number: this is a projection, not AP already
    /// earned, and it's what decides who still needs relics handed to them before the group
    /// exchanges.</summary>
    public long? RelicAp
    {
        get => _relicAp;
        set { _relicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Mirror MeterSettings.ShowRelicAp - see ShowDamageTaken's own remarks.</summary>
    public bool ShowRelicAp
    {
        get => _showRelicAp;
        set { _showRelicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Per the user: only the relic share, not the combined total -- this line's whole
    /// purpose is deciding who still needs relics handed to them before the group exchanges them,
    /// and a number that mixes in already-earned AP obscures exactly that. Blank once the relics
    /// are exchanged (RelicAp drops back to 0/null), same as any player who never picked one up,
    /// or whenever the setting is off.</summary>
    public string ApDisplay => ShowRelicAp && RelicAp is long relic && relic > 0
        ? $"Relic AP: {relic:N0}"
        : "";

    /// <summary>1-based position by damage within the rows currently shown, independent of how
    /// the user sorted the grid; 0 (blank) until the first refresh ranks the row.</summary>
    public int Rank
    {
        get => _rank;
        set { _rank = value; OnPropertyChanged(); OnPropertyChanged(nameof(RankDisplay)); }
    }

    /// <summary>This row's damage as a share of the biggest row shown, 0-100 - the length of the coloured
    /// bar behind the row (the website's meter bars work the same way).</summary>
    public double FillPercent
    {
        get => _fillPercent;
        set { _fillPercent = value; OnPropertyChanged(); }
    }

    private double _fillPercent;

    /// <summary>Taken mode: damage the group shields absorbed on this player, drawn as the blue part of the bar
    /// behind the part that got through (<see cref="Damage"/>). 0 in every other mode.</summary>
    public long Absorbed
    {
        get => _absorbed;
        set { _absorbed = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAbsorbed)); OnPropertyChanged(nameof(AbsorbedDisplay)); OnPropertyChanged(nameof(AbsorbedCompact)); }
    }

    /// <summary>All in One modes: the shield absorbed something on this player, shown in blue beside the damage taken.</summary>
    public bool HasAbsorbed => Absorbed > 0 && (AllMode || AllCompact);

    public string AbsorbedDisplay => HasAbsorbed ? $"+{Absorbed:N0}" : "";

    public string AbsorbedCompact => Absorbed > 0 ? Compact(Absorbed) : "";

    private long _absorbed;

    /// <summary>Length of the blue bar: the damage that got through plus the absorbed damage, as a share of the
    /// biggest such total shown, 0-100. 0 when nothing was absorbed, so no blue bar shows.</summary>
    public double AbsorbedFillPercent
    {
        get => _absorbedFillPercent;
        set { _absorbedFillPercent = value; OnPropertyChanged(); }
    }

    private double _absorbedFillPercent;

    /// <summary>The class colour, translucent, for that bar. Same colours as the website's meter.</summary>
    public System.Windows.Media.Brush ClassBrush => ClassBrushes.For(ClassName);

    public string RankDisplay => Rank > 0 ? Rank.ToString() : "";

    /// <summary>This row's share of the shown rows' combined damage, 0-100 - what the bar under the
    /// Damage/DPS line visualises.</summary>
    public double SharePercent
    {
        get => _sharePercent;
        set { _sharePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShareDisplay)); OnPropertyChanged(nameof(ShareOrHealCompact)); }
    }

    public string ShareDisplay => SharePercent.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "%";

    /// <summary>Damage this player RECEIVED inside the shown window (from the selected target
    /// only when one is picked) - the tank/aggro question the dealt-damage columns cannot answer.</summary>
    public long DamageTaken
    {
        get => _damageTaken;
        set { _damageTaken = value; OnPropertyChanged(); OnPropertyChanged(nameof(TakenDisplay)); OnPropertyChanged(nameof(TotalOrTakenCompact)); OnPropertyChanged(nameof(TakenCompact)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    public string TakenDisplay => !AllCompact && (ShowDamageTaken || AllMode) && DamageTaken > 0 ? $"↓ {DamageTaken:N0}" : "";

    private bool _allCompact;

    /// <summary>The All in One Compact mode is on: the usual Damage columns, plus a second line under the name with the
    /// healing and the damage taken (see AllLine). The row carries <see cref="Healing"/> and <see cref="DamageTaken"/> like in All.</summary>
    public bool AllCompact
    {
        get => _allCompact;
        set
        {
            _allCompact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TakenDisplay));
            OnPropertyChanged(nameof(HasAbsorbed));
            OnPropertyChanged(nameof(AbsorbedDisplay));
        }
    }

    public string HealCompact => Healing > 0 ? Compact(Healing) : "–";

    public string TakenCompact => DamageTaken > 0 ? Compact(DamageTaken) : "–";

    private bool _allMode;
    private long _healing;

    /// <summary>The All in One mode is on: the row then also carries <see cref="Healing"/> and the damage taken from
    /// monsters in <see cref="DamageTaken"/>, and the columns that otherwise show the rate or the share show those.</summary>
    public bool AllMode
    {
        get => _allMode;
        set
        {
            _allMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TakenDisplay));
            OnPropertyChanged(nameof(HasAbsorbed));
            OnPropertyChanged(nameof(AbsorbedDisplay));
            OnPropertyChanged(nameof(RateOrHealDisplay));
            OnPropertyChanged(nameof(RateOrDamageCompact));
            OnPropertyChanged(nameof(ShareOrHealCompact));
            OnPropertyChanged(nameof(TotalOrTakenCompact));
            OnPropertyChanged(nameof(HasSecondaryInfo));
        }
    }

    /// <summary>Healing done in the shown window, in All in One mode (0 elsewhere).</summary>
    public long Healing
    {
        get => _healing;
        set
        {
            _healing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RateOrHealDisplay));
            OnPropertyChanged(nameof(ShareOrHealCompact));
            OnPropertyChanged(nameof(HealCompact));
        }
    }

    /// <summary>The window's and the chips' rate column: DPS, or the healing in All in One mode.</summary>
    public string RateOrHealDisplay => AllMode ? (Healing > 0 ? $"+{Healing:N0}" : "–") : DpsDisplay;

    /// <summary>The compact overlay's columns in All in One mode read Damage, Heal, Taken instead of DPS, share, total.</summary>
    public string RateOrDamageCompact => AllMode ? DamageCompact : DpsCompact;

    public string ShareOrHealCompact => AllMode ? (Healing > 0 ? Compact(Healing) : "–") : ShareDisplay;

    public string TotalOrTakenCompact => AllMode ? (DamageTaken > 0 ? Compact(DamageTaken) : "–") : DamageCompact;

    /// <summary>Whether the second info line (AP/Taken/Defense/Pvp) has anything to show at all -
    /// per the user, that line should default to invisible (and its Auto row collapse to zero
    /// height with it) rather than sit there empty, since AP/Taken/Defense are all opt-in settings
    /// now and most rows will have none of them on.</summary>
    public bool HasSecondaryInfo =>
        ApDisplay.Length > 0 || TakenDisplay.Length > 0 || DefenseDisplay.Length > 0 || PvpDisplay.Length > 0;

    /// <summary>Mirror MeterSettings.ShowShareBars/ShowDamageTaken - set on every refresh so a
    /// changed setting reaches rows that already exist.</summary>
    public bool ShowShareBar
    {
        get => _showShareBar;
        set { _showShareBar = value; OnPropertyChanged(); }
    }

    public bool ShowDamageTaken
    {
        get => _showDamageTaken;
        set { _showDamageTaken = value; OnPropertyChanged(); OnPropertyChanged(nameof(TakenDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Avoided-attack tally ("D 3 · P 12 · B 8 (41%)", see Combat/DefenseStats) - blank
    /// when nothing was aimed at this player in the shown window, or when the setting is off.</summary>
    public string DefenseDisplay
    {
        get => _defenseDisplay;
        set { _defenseDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Kills/deaths/biggest hit against players (see Combat/PvpStats) - only filled in
    /// PVP mode, blank otherwise.</summary>
    public string PvpDisplay
    {
        get => _pvpDisplay;
        set { _pvpDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    public PlayerRow(int objectId)
    {
        ObjectId = objectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The opt-in extras that still get a second line under the row (relic AP, avoided
    /// attacks, PVP stats). Damage received has its own column, so it does not count here.</summary>
    public bool HasExtraInfo => ApDisplay.Length > 0 || DefenseDisplay.Length > 0 || PvpDisplay.Length > 0;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(HasSecondaryInfo))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasExtraInfo)));
        }
    }
}
