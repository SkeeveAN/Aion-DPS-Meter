using System.Globalization;

namespace AionDPS.Ui;

public enum ChecklistGroup { Overview, Daily, Weekly }

/// <summary>One counter of the checklist (entries, daily tasks, weekly counters). The checklist window and the dashboard widget "Checklist" draw the same rows, so the numbers always agree.</summary>
public sealed record ChecklistRow(string Key, ChecklistGroup Group, string Label, int Count, int Cap, bool Estimated, bool CountsUp, int Bonus = 0, Aion2.TicketDef? Def = null, Aion2.ChecklistManual.Counter? Manual = null);

/// <summary>The counters of one character and the choice of the player which elements the widget shows (one small switch in front of each element in the checklist).</summary>
public static class ChecklistRows
{
    private static readonly int[] OverviewIds = { 1, 8, 4, 80000001 };
    public const int DailyDungeonTicket = 101;

    /// <summary>What the widget shows as long as the player has not chosen: the Odyle energy and the four entry counters, as it always was.</summary>
    private static readonly string[] DefaultKeys = { "odyle", "ticket:1", "ticket:4", "ticket:8", "ticket:101" };

    public static bool IsPicked(string key)
    {
        List<string>? chosen = MeterSettings.Load().ChecklistWidgetItems;
        return (chosen ?? DefaultKeys.ToList()).Contains(key);
    }

    public static void SetPicked(string key, bool on)
    {
        MeterSettings settings = MeterSettings.Load();
        List<string> chosen = settings.ChecklistWidgetItems ?? DefaultKeys.ToList();
        chosen.Remove(key);
        if (on)
        {
            chosen.Add(key);
        }

        settings.ChecklistWidgetItems = chosen;
        settings.Save();
    }

    public static List<ChecklistRow> Build(Aion2.TicketCharacter character, DateTime seenAt, DateTime now, bool premium, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var rows = new List<ChecklistRow>();
        int CapOf(Aion2.TicketDef def) => premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;

        foreach (int id in OverviewIds)
        {
            if (Aion2.Aion2Tickets.Find(id) is not { } def || !(character.Tickets.ContainsKey(id) || Aion2.Aion2Tickets.IsLocked(character, id)))
            {
                continue;
            }

            bool locked = Aion2.Aion2Tickets.IsLocked(character, id); // the level is too low: grey, 0 / cap
            Aion2.TicketValue own = locked || !character.Tickets.TryGetValue(id, out Aion2.TicketValue found) ? default : found;
            int count = locked ? 0 : Aion2.Aion2Tickets.Expected(def, own.Count, seenAt, now, premium);
            rows.Add(new ChecklistRow("ticket:" + id, ChecklistGroup.Overview, def.ShortIn(language), count, Math.Max(CapOf(def), count + own.Bonus), count != own.Count, true, own.Bonus, def));
        }

        // the three Abyss Corridors, one row each (0 / 1 = not entered yet since the Thursday / Saturday 21:30 reset), as "corridor: artifact", the artifact as the client names it
        // the word "corridor" alone (the client's zone name is "Abyss Corridor": Abysskorridor, Corridor d'Abysse, Corredor del Abismo, ...)
        string corridorName = language switch { "de" => "Korridor", "fr" => "Corridor", "es" or "pt" => "Corredor", "ru" => "Галерея", "ja" => "回廊", "ko" => "회랑", _ => "Corridor" };
        foreach ((int zone, Dictionary<string, string> names) in Aion2.ChecklistManual.Corridors)
        {
            string artifact = names.GetValueOrDefault(language) ?? names["en"];
            rows.Add(new ChecklistRow("corridor:" + zone, ChecklistGroup.Overview, corridorName + ": " + artifact, Aion2.ChecklistManual.CorridorEntered(character.Name, zone, now) ? 1 : 0, 1, false, true));
        }

        // the mandatory tasks of the home region as one counter of five (the count read last, or 0 as an estimate when a daily reset came since)
        bool stale = character.Tickets.TryGetValue(Aion2.Aion2Tickets.MissionsId, out Aion2.TicketValue missions) && Aion2.Aion2Tickets.MissionsStale(missions, DateTime.Now);
        string region = loc[character.ServerId / 1000 == 2 ? "Checklist.RegionAsmodian" : "Checklist.RegionElyos"];
        rows.Add(new ChecklistRow("daily:missions", ChecklistGroup.Daily, string.Format(CultureInfo.CurrentCulture, loc["Checklist.DailyTasks"], region), stale ? 0 : missions.Count, Aion2.Aion2Tickets.MissionsPerDay, stale, true));

        // weekly: the "Daily Dungeon" counter (cap 14, extra entries behind it as the bonus), the scroll purchases, the trade guild, the season points, the conversions
        Aion2.ChecklistManual.State shared = Aion2.ChecklistManual.Get(Aion2.ChecklistManual.SharedKey, now);
        bool dungeonLocked = Aion2.Aion2Tickets.IsLocked(character, DailyDungeonTicket);
        if (Aion2.Aion2Tickets.Find(DailyDungeonTicket) is { } dungeon && (dungeonLocked || character.Tickets.ContainsKey(dungeon.Id)))
        {
            Aion2.TicketValue value = dungeonLocked ? default : character.Tickets[dungeon.Id];
            int count = dungeonLocked ? 0 : Aion2.Aion2Tickets.Expected(dungeon, value.Count, seenAt, now, premium);
            rows.Add(new ChecklistRow("ticket:" + DailyDungeonTicket, ChecklistGroup.Weekly, dungeon.ShortIn(language), count, Math.Max(dungeon.Max, count), count != value.Count, false, value.Bonus, dungeon));
        }

        rows.Add(new ChecklistRow("weekly:verteron", ChecklistGroup.Weekly, ChecklistWindow.ScrollName("verteron", language, loc["Checklist.ScrollVerteron"]), Aion2.ChecklistManual.VerteronCount(shared), Aion2.ChecklistManual.VerteronMax, false, true));
        rows.Add(new ChecklistRow("weekly:abyss", ChecklistGroup.Weekly, ChecklistWindow.ScrollName("abyss", language, loc["Checklist.ScrollAbyss"]), Aion2.ChecklistManual.AbyssCount(shared), Aion2.ChecklistManual.AbyssMax, false, true));
        // 4 a week per character and 16 per server, 20 together; each counts only while its reset time lies ahead
        long nowMs = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds();
        int bought = 0;
        foreach ((int id, int limit) in new[] { (Aion2.Aion2Tickets.ShopOdyleId, Aion2.Aion2Tickets.ShopOdylePerCharacter), (Aion2.Aion2Tickets.ShopOdyleServerId, Aion2.Aion2Tickets.ShopOdylePerServer) })
        {
            if (character.Tickets.TryGetValue(id, out Aion2.TicketValue shop) && shop.TimeMs > nowMs)
            {
                bought += Math.Min(shop.Count, limit);
            }
        }

        rows.Add(new ChecklistRow("weekly:shopodyle", ChecklistGroup.Weekly, loc["Checklist.ShopOdyle"], bought, Aion2.Aion2Tickets.ShopOdyleMax, false, true));
        if (character.Tickets.TryGetValue(Aion2.Aion2Tickets.WeeklyPointsId, out Aion2.TicketValue weekly))
        {
            int points = nowMs - weekly.TimeMs < 7L * 24 * 3600 * 1000 ? Math.Min(weekly.Count, Aion2.Aion2Tickets.WeeklyPointsMax) : 0;
            rows.Add(new ChecklistRow("weekly:points", ChecklistGroup.Weekly, loc["Checklist.WeeklyPoints"], points, Aion2.Aion2Tickets.WeeklyPointsMax, false, true));
        }

        // the two conversions come from the game; the hand tick stays for what the meter did not see
        Aion2.ChecklistManual.State own2 = Aion2.ChecklistManual.Get(character.Name, now);
        int convertBig = own2.ConvertBig;
        int convertSmall = own2.ConvertSmall;
        if (character.Tickets.TryGetValue(Aion2.Aion2Tickets.ConvertServerId, out Aion2.TicketValue serverConvert) && serverConvert.TimeMs > nowMs)
        {
            convertBig = Math.Max(convertBig, Math.Min(serverConvert.Count, Aion2.ChecklistManual.ConvertBigMax));
        }

        if (character.Tickets.TryGetValue(Aion2.Aion2Tickets.ConvertCharacterId, out Aion2.TicketValue characterConvert) && characterConvert.TimeMs > nowMs)
        {
            convertSmall = Math.Max(convertSmall, Math.Min(characterConvert.Count, Aion2.ChecklistManual.ConvertSmallMax));
        }

        rows.Add(new ChecklistRow("weekly:convertbig", ChecklistGroup.Weekly, loc["Checklist.ConvertBig"], convertBig, Aion2.ChecklistManual.ConvertBigMax, false, true, 0, null, Aion2.ChecklistManual.Counter.ConvertBig));
        rows.Add(new ChecklistRow("weekly:convertsmall", ChecklistGroup.Weekly, loc["Checklist.ConvertSmall"], convertSmall, Aion2.ChecklistManual.ConvertSmallMax, false, true, 0, null, Aion2.ChecklistManual.Counter.ConvertSmall));
        return rows;
    }
}
