using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>
/// The player list without a UI Automation tree. WPF builds a peer for every row, and when the list
/// is refreshed while Windows (accessibility, the touch keyboard, an overlay) reads that tree,
/// <c>ItemAutomationPeer.GetNameCore</c> can hit a row that was just replaced and throw from inside
/// layout - it ended the meter in the middle of a game. The list is a live meter with nothing for an
/// automation client to operate, so it simply offers no peer.
/// </summary>
public sealed class QuietDataGrid : DataGrid
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}

/// <summary>The overlay's player chips, without a UI Automation tree (see <see cref="QuietDataGrid"/>).</summary>
public sealed class QuietItemsControl : ItemsControl
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
