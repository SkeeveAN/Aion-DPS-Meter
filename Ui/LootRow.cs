using System.ComponentModel;
using System.Runtime.CompilerServices;
using AionSniffer.Data;

namespace AionSniffer.Ui;

/// <summary>
/// One distinct (person, item) pair in the session's loot list -- see
/// ChatLogParser.LootAcquired/MainWindow.OnLootAcquired remarks for where this data comes from and
/// how "person" is resolved (whichever real character/group member the acquiring line named, "You"
/// mapped to the active registered character). Quantity accumulates across every "acquired" line
/// for this exact (person, item) pair; LastTag keeps the most recently seen verbatim "[item:...]"
/// tag text (not just the numeric id) so the loot-copy command can paste something that still
/// renders as a clickable item in Aion's own chat, per the user's request.
/// </summary>
public sealed class LootRow : INotifyPropertyChanged
{
    private long _quantity;
    private string _lastTag;

    public string Person { get; }
    public int ItemId { get; }
    public string ItemName { get; }
    public ItemGrade? Grade { get; }

    public long Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(); }
    }

    public string LastTag
    {
        get => _lastTag;
        set { _lastTag = value; OnPropertyChanged(); }
    }

    public LootRow(string person, int itemId, string itemName, ItemGrade? grade, string lastTag)
    {
        Person = person;
        ItemId = itemId;
        ItemName = itemName;
        Grade = grade;
        _lastTag = lastTag;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
