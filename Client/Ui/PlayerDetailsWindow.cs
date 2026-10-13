using System.Windows;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The player details in a window of their own (a double click on a player in the meter); the content is <see cref="PlayerDetailsView"/>,
/// which the session manager also embeds beside its list.</summary>
public sealed class PlayerDetailsWindow : Window
{
    public PlayerDetailsWindow(string name, string className, string faction,
        IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf, bool heals = false,
        IReadOnlyList<Death>? deaths = null, bool taken = false, IReadOnlyList<DamageEvent>? fightEvents = null,
        FightExtras? extras = null, int playerId = -1, DateTime? fightStart = null, DateTime? fightEnd = null, ReplayData? replay = null)
    {
        Title = "Character Details";
        Height = 680;
        Width = 1000;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontSizeProperty, "FontSize.Base");
        Content = new PlayerDetailsView(name, className, faction, events, nameOf, heals, deaths, taken, fightEvents, extras, playerId, fightStart, fightEnd, replay);
        ThemedChrome.Apply(this);
    }
}
