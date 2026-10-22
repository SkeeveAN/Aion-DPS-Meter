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
        FightExtras? extras = null, int playerId = -1, DateTime? fightStart = null, DateTime? fightEnd = null, ReplayData? replay = null, PlayerSeenInfo? seen = null)
    {
        Title = "Character Details";
        Height = seen is null ? 680 : 780;
        Width = 1000;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontSizeProperty, "FontSize.Base");
        Content = new PlayerDetailsView(name, className, faction, events, nameOf, heals, deaths, taken, fightEvents, extras, playerId, fightStart, fightEnd, replay, seen);
        ThemedChrome.Apply(this);
    }
}

/// <summary>What the game's "player appeared" frame said of a player, for the details window: nothing of it ever goes online from here
/// (see the profile upload for the rule about the 10 against 10 arena). <see cref="Equalized"/>: first met in that arena, so the combat power is not shown.</summary>
public sealed record PlayerSeenInfo(int? Level, int? ItemLevel, int? CombatPower, IReadOnlyList<Aion2.Aion2WornItem> Worn, bool Equalized, int ServerId = 0, IReadOnlyList<Aion2.Aion2EquippedItem>? RemoteGear = null);
