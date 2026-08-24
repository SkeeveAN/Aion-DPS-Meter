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

    private string _name = "?";
    private string _className = "?";
    private int _level;

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

    /// <summary>Null for every row except "You"'s -- per the user, shown as a second line under
    /// Damage/DPS. AP is a session-wide personal total (see ChatLogParser.PersonalStatChanged
    /// remarks: it's never attributed to a specific target the way damage is), not something a
    /// mob or other player row could ever have a real value for, so this stays null there rather
    /// than showing a fabricated 0.</summary>
    public long? Ap
    {
        get => _ap;
        set { _ap = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); }
    }

    public string ApDisplay => Ap is long ap ? $"AP: {ap:N0}" : "";

    public PlayerRow(int objectId)
    {
        ObjectId = objectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
