using System.Windows;
using System.Windows.Controls;

namespace AionSniffer.Ui;

/// <summary>One remembered player as the grid shows them.</summary>
public sealed record KnownPlayerRow(string Name, string ClassName, string Faction, string Pinned, string LastSeen);

/// <summary>
/// Browse and correct what the meter remembers about people it has seen.
///
/// <para>Exists because the grid's own right-click menu only reaches someone who happens to be in
/// the current session. A faction that was derived wrongly -- an arena opponent of your own
/// faction, say -- otherwise stayed wrong until that player turned up in a fight again.</para>
///
/// <para>Edits are written straight through and saved immediately: this window is opened
/// deliberately to fix something, and losing that on a crash would be worse than the cost of a
/// small file write.</para>
/// </summary>
public partial class PlayerDatabaseWindow : Window
{
    private readonly KnownPlayers _players;

    public PlayerDatabaseWindow(KnownPlayers players, string ownFaction)
    {
        InitializeComponent();
        _players = players;
        OwnFaction = ownFaction;
        Refresh();
    }

    /// <summary>Used only to pick a sensible first value for a player with no faction yet: the one
    /// the local player is NOT, since a correction is nearly always about an opponent.</summary>
    private string OwnFaction { get; }

    private void Refresh()
    {
        string filter = SearchBox.Text.Trim();
        var rows = _players.All()
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(p => new KnownPlayerRow(
                p.Name,
                p.ClassName.Length > 0 ? p.ClassName : "?",
                p.Faction.Length > 0 ? p.Faction : "-",
                p.FactionIsManual ? "yes" : "",
                p.LastSeenUtc == default ? "-" : p.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
            .ToList();

        // Selection is restored by name, not by index: the list is re-sorted on every refresh and
        // an index would land on whoever moved into that slot.
        string? selected = (PlayersGrid.SelectedItem as KnownPlayerRow)?.Name;
        PlayersGrid.ItemsSource = rows;
        PlayersGrid.SelectedItem = rows.FirstOrDefault(r => r.Name == selected);

        CountText.Text = filter.Length == 0
            ? $"{rows.Count} players"
            : $"{rows.Count} of {_players.All().Count} players";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnSwitchFactionClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is not KnownPlayerRow row)
        {
            return;
        }

        string next = row.Faction == "Elyos" ? "Asmodian"
            : row.Faction == "Asmodian" ? "Elyos"
            : OwnFaction == "Elyos" ? "Asmodian"
            : "Elyos";

        _players.SetFactionManually(row.Name, next);
        _players.SaveIfChanged();
        Refresh();
    }

    private void OnClearManualClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is not KnownPlayerRow row)
        {
            return;
        }

        _players.ClearManualFaction(row.Name);
        _players.SaveIfChanged();
        Refresh();
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (PlayersGrid.SelectedItem is not KnownPlayerRow row)
        {
            return;
        }

        if (MessageBox.Show(this, $"Forget everything remembered about {row.Name}?",
                "Player Database", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _players.Remove(row.Name);
        _players.SaveIfChanged();
        Refresh();
    }
}
