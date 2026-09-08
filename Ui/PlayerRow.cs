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
    private long? _ap;
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

    /// <summary>Shown as a second line under Damage/DPS. Two sources feed it (see
    /// MainWindow.ApTotalFor): the session's personal AP counter, which Chat.log only ever reports
    /// for the local player, and AP from looted relics, which any group member can earn (see
    /// Data/RelicApDatabase). So this is populated for "You" always, and for another player as
    /// soon as they pick up a relic -- null everywhere else, including mob rows, rather than a
    /// fabricated 0.</summary>
    public long? Ap
    {
        get => _ap;
        set { _ap = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); }
    }

    /// <summary>AP earned from relics, part of <see cref="Ap"/> rather than additional to it --
    /// shown separately because the two halves are not the same kind of number: the rest is what
    /// the client reported gaining, this is what the relics in the bag WILL pay once exchanged.
    /// Exchanging them makes the client report that payout as an ordinary AP gain, at which point
    /// the same AP is in the total twice; seeing the relic share is what makes that visible
    /// instead of silently inflating the figure.</summary>
    public long? RelicAp
    {
        get => _relicAp;
        set { _relicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); }
    }

    public string ApDisplay => Ap is not long ap
        ? ""
        : RelicAp is long relic && relic > 0
            ? $"AP: {ap:N0} ({relic:N0} Rel.)"
            : $"AP: {ap:N0}";

    public PlayerRow(int objectId)
    {
        ObjectId = objectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
