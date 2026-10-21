namespace AionDPS.Aion2;

/// <summary>
/// The militia guards ("Guard", German "Wachmann" in the client): NPCs of a city or camp that are friendly at home and kill a player of the other side at once.
/// The game announces an NPC (with its place) only within some tens of metres, so a guard shows on the map once the player comes near.
/// The map draws them (a red badge with a skull and a danger zone) on the open maps of the other side only; Stacy's fork had the idea (17 October 2026:
/// the guards at the Coldwind Shelter camp in Verteron, the record 2026-10-17 17:03:10) and drew them on Altgard and Morheim for an Elyos.
/// </summary>
public static class Aion2Guards
{
    /// <summary>The danger zone drawn around a guard, in metres.</summary>
    public const double ZoneMetres = 20;

    /// <summary>The 29 NPC ids the client names "Guard" (npcs/npc_names.json: en Guard, de Wachmann, fr Garde, ...).</summary>
    public static bool IsGuard(int npcId) => npcId is >= 2930813 and <= 2930820 or >= 2931239 and <= 2931242 or 2939024
        or 2950012 or 2950139 or 2950619 or 2950727 or 2950739 or 2950744 or 2950839 or 2950973 or 2951099 or 2951100
        or 2959287 or 2959288 or >= 2989445 and <= 2989448;

    /// <summary>Whether the guards of this map are the player's enemies: an open map of the other side (World_L is the Elyos world, World_D the Asmodian one).
    /// The Abyss and the instances belong to nobody; an unknown own side draws nothing.</summary>
    public static bool IsHostileMap(string? mapTable, string? ownFaction)
    {
        string? side = mapTable is null ? null : mapTable.Contains("World_L", StringComparison.Ordinal) ? "Elyos" : mapTable.Contains("World_D", StringComparison.Ordinal) ? "Asmodian" : null;
        return side is not null && ownFaction is not null && side != ownFaction;
    }
}
