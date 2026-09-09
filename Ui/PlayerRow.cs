using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AionSniffer.Ui;

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

    private string _name = "?";
    private string _className = "?";
    private int _level;
    private string _faction = "";
    private bool _isEnemy;

    public int ObjectId { get; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string ClassName
    {
        get => _className;
        set { _className = value; OnPropertyChanged(); }
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
        set { _damage = value; OnPropertyChanged(); }
    }

    /// <summary>Null renders as "n/a" in the grid -- see DpsCalculator's remarks on why a single
    /// hit (or otherwise zero elapsed time) must not show a fabricated rate.</summary>
    public double? Dps
    {
        get => _dps;
        set { _dps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DpsDisplay)); }
    }

    public string DpsDisplay => Dps is double d ? d.ToString("F0") : "n/a";

    /// <summary>AP the relics currently in this player's bag will pay out once exchanged (see
    /// Data/RelicApDatabase) -- shown on its own, not folded into the session's real AP total,
    /// because the two are not the same kind of number: this is a projection, not AP already
    /// earned, and it's what decides who still needs relics handed to them before the group
    /// exchanges.</summary>
    public long? RelicAp
    {
        get => _relicAp;
        set { _relicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); }
    }

    /// <summary>Per the user: only the relic share, not the combined total -- this line's whole
    /// purpose is deciding who still needs relics handed to them before the group exchanges them,
    /// and a number that mixes in already-earned AP obscures exactly that. Blank once the relics
    /// are exchanged (RelicAp drops back to 0/null), same as any player who never picked one up.</summary>
    public string ApDisplay => RelicAp is long relic && relic > 0
        ? $"Relic AP: {relic:N0}"
        : "";

    public PlayerRow(int objectId)
    {
        ObjectId = objectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
