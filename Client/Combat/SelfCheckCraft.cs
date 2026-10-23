using System.Buffers.Binary;
using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The crafting tool: the stock read from the login frame (inventory, character storage, account storage), a change of one slot, a sort that resends the inventory
/// (what is more in the inventory than the single frames explain was taken from the storage), and the materials of a plan (an ingot broken down into its ore,
/// the first recipe of the list getting the stock first, a recipe of the other faction found by its twin).
/// </summary>
public static class SelfCheckCraft
{
    private const int Kinah = 930100001, Elixir = 530116001, Tinder = 530126003, Potion = 530126004, Mana = 530126005, Heal = 530146001, Crystal = 530116006, Greater = 530116010;

    /// <summary>A frame as the game sends it: opcode, then a record per item (kind byte, 9 bytes, item u32, quantity u64, container, slot, 12 bytes).</summary>
    private static byte[] Frame(byte opcodeHigh, int minimumLength, params (byte Kind, byte Container, byte Slot, int Item, long Quantity)[] records)
    {
        var body = new List<byte> { (byte)records.Length };
        foreach (var r in records)
        {
            body.Add(r.Kind);
            body.AddRange(new byte[9]);
            var item = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(item, (uint)r.Item);
            body.AddRange(item);
            var quantity = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(quantity, (ulong)r.Quantity);
            body.AddRange(quantity);
            body.Add(r.Container);
            body.Add(r.Slot);
            body.AddRange(new byte[12]);
        }

        while (body.Count + 2 < minimumLength)
        {
            body.Add(0);
        }

        var frame = new List<byte> { opcodeHigh == 0x11 ? (byte)0x11 : (byte)0x1b, 0x56 };
        frame.AddRange(body);
        return frame.ToArray();
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Crafting tool (stock, sort, recipes, ingots):");
        DateTime t = new(2026, 10, 21, 17, 40, 0, DateTimeKind.Local);
        Aion2Stock.Reset();

        // login: 5 elixirs in the inventory, 10 in the character storage, 3 potions there too, 7 in the account storage
        Aion2Stock.NoteSnapshot(Frame(0x11, 6000, (1, 1, 0, Elixir, 5), (1, 15, 0, Elixir, 10), (1, 15, 1, Potion, 3), (1, 2, 0, Potion, 7), (1, 1, 1, Kinah, 1000)), t);
        bool login = Aion2Stock.Count(Elixir, StockSources.All) == 15 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7 && Aion2Stock.Count(Potion, StockSources.Inventory) == 0
            && Aion2Stock.Count(Kinah, StockSources.Inventory) == 1000;
        Console.WriteLine($"  -> the login frame fills the three stocks apart: {login}");

        // one slot changes (8 elixirs now), an item of kind 2 goes
        Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 0, Elixir, 8)), t.AddSeconds(5));
        bool one = Aion2Stock.Count(Elixir, StockSources.Inventory) == 8 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 10;
        Aion2Stock.NoteChange(Frame(0x1b, 0, (2, 1, 1, Kinah, 0)), t.AddSeconds(6));
        bool gone = Aion2Stock.Count(Kinah, StockSources.Inventory) == 0;
        Console.WriteLine($"  -> a changed slot sets its quantity, a slot of kind 2 is gone: {one && gone}");

        // eight new items at once (kind 0) are loot, not a sort: nothing is taken from the storage and the other slots stay
        Aion2Stock.NoteChange(Frame(0x1b, 0, (0, 1, 20, Tinder, 1), (0, 1, 21, Mana, 1), (0, 1, 22, Heal, 1), (0, 1, 23, Crystal, 1), (0, 1, 24, Greater, 1), (0, 1, 25, Kinah, 5), (0, 1, 26, Potion, 1), (0, 1, 27, Elixir, 1)), t.AddSeconds(8));
        bool loot = Aion2Stock.Count(Elixir, StockSources.Inventory) == 9 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 10 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7;
        Console.WriteLine($"  -> eight pickups at once are no sort (the storages stay as they were): {loot}");

        // a sort resends the inventory: 14 elixirs in all now, so 5 of the 9 explained by the single frames came from the character storage; 1 potion in the sort = 0 more than the loot
        Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 0, Elixir, 14), (1, 1, 1, Tinder, 1), (1, 1, 2, Mana, 1), (1, 1, 3, Heal, 1), (1, 1, 4, Crystal, 1), (1, 1, 5, Greater, 1), (1, 1, 6, Kinah, 5), (1, 1, 7, Potion, 1)), t.AddSeconds(30));
        bool sort = Aion2Stock.Count(Elixir, StockSources.Inventory) == 14 && Aion2Stock.Count(Elixir, StockSources.CharacterStorage) == 5 && Aion2Stock.Count(Potion, StockSources.AccountStorage) == 7
            && Aion2Stock.Count(Tinder, StockSources.Inventory) == 1;
        Console.WriteLine($"  -> a sort lists the inventory again: what is more than the single frames explain was taken from the storage (5 of 10 elixirs): {sort}");

        // the recipes: three smithing hammers need 6 ingots each; an ingot is made of ore, so the list asks for the ore
        CraftRecipe? hammer = Aion2CraftData.Find(119016001);
        int ingot = hammer?.Components[0].Item ?? 0;
        CraftRecipe? smelt = hammer is null ? null : Aion2CraftData.ProducerOf(ingot, true);
        bool data = hammer is not null && smelt is not null && hammer.Elyos && Aion2CraftData.All.Count > 1000;
        Console.WriteLine($"  -> the recipe table is read (a hammer, the recipe of its ingot): {data}");
        if (!data)
        {
            Aion2Stock.Reset();
            return false;
        }

        var none = new Dictionary<int, long>();
        List<CraftResult> raw = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer!.Id, Count = 3 } }, none, resolve: true, elyos: true);
        long ingots = hammer.Components[0].Count * 3;
        long batches = (ingots + smelt!.Count - 1) / smelt.Count;
        bool oreAsked = raw.Count == 1 && raw[0].Intermediates.Any(i => i.Item == ingot && i.Crafts == batches)
            && smelt.Components.All(c => raw[0].Materials.Any(m => m.Item == c.Item && m.Needed == c.Count * batches)) && raw[0].Materials.All(m => m.Item != ingot);
        Console.WriteLine($"  -> 3 hammers: {ingots} ingots = {batches} crafts of the ingot recipe, the list shows the ore and no ingot: {oreAsked}");

        List<CraftResult> unresolved = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer.Id, Count = 3 } }, none, resolve: false, elyos: true);
        bool plain = unresolved[0].Materials.Any(m => m.Item == ingot && m.Needed == ingots) && unresolved[0].Intermediates.Count == 0;
        Console.WriteLine($"  -> without breaking down, the ingot is a line of its own: {plain}");

        // ingots in the stock are used first: 6 on hand = 6 fewer to make
        var withIngots = new Dictionary<int, long> { [ingot] = 6 };
        List<CraftResult> some = CraftCalc.Compute(new[] { new CraftEntry { Recipe = hammer.Id, Count = 3 } }, withIngots, resolve: true, elyos: true);
        long restBatches = (ingots - 6 + smelt.Count - 1) / smelt.Count;
        bool used = some[0].Intermediates.Any(i => i.Item == ingot && i.Crafts == restBatches) && some[0].Materials.Any(m => m.Item == ingot && m.Needed == 6 && m.Have == 6);
        Console.WriteLine($"  -> 6 ingots on hand are used first (counted as there, the rest is made): {used}");

        // two recipes want the same material: the first one in the list gets the stock
        int shared = smelt.Components[0].Item;
        var scarce = new Dictionary<int, long> { [shared] = smelt.Components[0].Count * 2 };
        var twice = new[] { new CraftEntry { Recipe = smelt.Id, Count = 1 }, new CraftEntry { Recipe = smelt.Id, Count = 3 } };
        List<CraftResult> order = CraftCalc.Compute(twice, scarce, resolve: true, elyos: true);
        long firstHave = order[0].Materials.First(m => m.Item == shared).Have, secondHave = order[1].Materials.First(m => m.Item == shared).Have;
        bool first = firstHave == smelt.Components[0].Count && secondHave == smelt.Components[0].Count && order[0].Percent >= order[1].Percent;
        Console.WriteLine($"  -> the first recipe of two that want the same material gets the stock first ({firstHave} then {secondHave}): {first}");

        // the plan made as an Elyos is found again as an Asmodian: the twin recipe makes the same piece
        CraftRecipe? twin = Aion2CraftData.ForFaction(hammer.Id, false);
        bool faction = twin is { Elyos: false } && twin.Category == hammer.Category && twin.Level == hammer.Level && Aion2CraftData.ForFaction(twin.Id, true)?.Id == hammer.Id;
        Console.WriteLine($"  -> a recipe of one faction finds its twin of the other and back: {faction}");

        // the learned recipes: the list frame 0x7e8d (n, then id + 0 each); the twin of the other faction counts as learned too
        Aion2Recipes.Reset();
        bool noList = Aion2Recipes.IsKnown(hammer);
        var list = new List<byte> { 0x8d, 0x7e, 2 };
        foreach (int id in new[] { hammer.Id, 100000001 })
        {
            var entry = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)id);
            list.AddRange(entry);
        }

        bool read = Aion2Recipes.Note(list.ToArray()) && Aion2Recipes.IsKnown(hammer) && Aion2Recipes.IsKnown(twin!) && !Aion2Recipes.IsKnown(smelt) && !Aion2Recipes.Note(list.Take(10).ToArray());
        Aion2Recipes.Reset();
        // a crafting level-up (profession 1 = weapon smithing, level 50 absolute) teaches the recipes learnt by level up to it, in the category of the profession only
        Aion2Recipes.Note(list.ToArray());
        int before = Aion2CraftData.All.Count(r => Aion2Recipes.IsKnown(r));
        byte[] levelUp = { 0x8d, 0x7d, 1, 50, 0, 0, 0, 0, 0 };
        int taught = Aion2Recipes.NoteLevelUp(levelUp);
        CraftRecipe? forged = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Blacksmithing" && r.Learn == "Auto" && r.Level is > 1 and <= 50 && r.Id != hammer.Id);
        CraftRecipe? tailored = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Tailoring" && r.Learn == "Auto" && r.Level is > 1 and <= 50);
        CraftRecipe? later = Aion2CraftData.All.FirstOrDefault(r => r.Category == "Blacksmithing" && r.Learn == "Auto" && r.Level > 50);
        bool levelled = taught > 0 && forged is not null && Aion2Recipes.IsKnown(forged) && tailored is not null && !Aion2Recipes.IsKnown(tailored) && later is not null && !Aion2Recipes.IsKnown(later) && Aion2Recipes.NoteLevelUp(levelUp) == 0;
        Aion2Recipes.Reset();
        bool noBase = Aion2Recipes.NoteLevelUp(levelUp) == 0 && !Aion2Recipes.HasData;
        Console.WriteLine($"  -> a crafting level-up teaches the level's recipes of its profession (nothing before a login list): {levelled && noBase} ({before} known before)");
        bool known = noList && read && levelled && noBase;
        Console.WriteLine($"  -> without a list every recipe counts as learned, the list frame sets the learned ones (twin included): {known}");

        // chain skills: 1838 opens the follow-up, 1938 closes a window, an unclosed one ends by itself
        Aion2Chains.Reset();
        byte[] open = new byte[11];
        open[0] = 0x38; open[1] = 0x18; open[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(2), 11010340);
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(7), 11420340);
        byte[] close = new byte[7];
        close[0] = 0x38; close[1] = 0x19; close[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(close.AsSpan(3), 11420340);
        bool opened = Aion2Chains.NoteOpen(open, t) && Aion2Chains.Available(t.AddSeconds(1)) is [{ Skill: 11420340, SecondsLeft: > 1.9 and < 2.1 }];
        bool closed = Aion2Chains.NoteClose(close) && Aion2Chains.Available(t.AddSeconds(1)).Count == 0;
        Aion2Chains.NoteOpen(open, t);
        bool expired = Aion2Chains.Available(t.AddSeconds(4)).Count == 0;
        Aion2Chains.Reset();
        bool chain = opened && closed && expired;
        Console.WriteLine($"  -> a chain skill is open for 3 s after frame 1838, closed by 1938 or by the time: {chain}");

        // the legion list: a member with a comment (length in bytes, one more byte) and one without, the name's length counting characters
        static byte[] Member(int id, int server, int classCode, string name, int level, int rank, string? comment)
        {
            var m = new List<byte>();
            m.AddRange(BitConverter.GetBytes(id));
            m.AddRange(new byte[] { 0, 0 });
            m.AddRange(BitConverter.GetBytes((ushort)server));
            m.AddRange(BitConverter.GetBytes(classCode));
            m.Add((byte)name.Length);
            m.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
            m.AddRange(new byte[] { (byte)level, 0, 0, 0, (byte)rank });
            if (comment is not null)
            {
                byte[] c = System.Text.Encoding.UTF8.GetBytes(comment);
                m.Add((byte)c.Length);
                m.AddRange(c);
                m.Add(0x1b);
            }

            m.AddRange(BitConverter.GetBytes(1_791_000_000_000L));
            m.AddRange(BitConverter.GetBytes(2047));
            m.AddRange(BitConverter.GetBytes(2100));
            m.AddRange(BitConverter.GetBytes(95000));
            m.AddRange(new byte[4]);
            m.Add(2);
            m.AddRange(BitConverter.GetBytes(470418));
            m.AddRange(new byte[4]);
            return m.ToArray();
        }

        var legionFrame = new List<byte> { 0x05, 0x8a, 0, 0, 0x14, 2 };
        legionFrame.AddRange(new byte[8]);
        legionFrame.AddRange(Member(207710, 1304, 30, "Sidy", 45, 2, "Hier könnte Werbung stehen"));
        legionFrame.AddRange(Member(208558, 1304, 18, "Pencilgon", 22, 0, null));
        legionFrame.AddRange(Member(208939, 2304, 34, "Ärgernis", 45, 1, null));
        bool legion = Aion2LegionList.TryParse(legionFrame.ToArray(), out List<LegionMember> listed) && listed.Count == 3
            && listed[0] is { Name: "Sidy", ServerId: 1304, ClassCode: 30, Level: 45, Rank: 2, ItemLevel: 2100, CombatPower: 95000, CharacterId: 207710 }
            && listed[1] is { Name: "Pencilgon", Level: 22, Rank: 0 } && listed[2] is { Name: "Ärgernis", ServerId: 2304 };
        Console.WriteLine($"  -> the legion list is read with and without a comment, names counted in characters: {legion}");

        // the weekly points of the achievements frame: only that entry is read
        var ach = new List<byte> { 0x56, 0x83, 2 };
        foreach ((uint id, uint count) in new[] { (100030000u, 1u), (210000100u, 3440u) })
        {
            ach.AddRange(BitConverter.GetBytes(id));
            ach.AddRange(BitConverter.GetBytes(count));
            ach.AddRange(BitConverter.GetBytes(1_791_499_543_290UL));
            ach.Add(0);
        }

        bool points = Aion2Tickets.TryParseAchievements(ach.ToArray(), out TicketReading week) && week.Id == Aion2Tickets.WeeklyPointsId && week.Value.Count == 3440
            && week.Value.TimeMs == 1_791_499_543_290 && !Aion2Tickets.TryParseAchievements(ach.Take(10).ToArray(), out _);
        Console.WriteLine($"  -> the week's season points are read from the achievements frame: {points}");

        // the own rank of the ranking window (a real frame: Abyss, overall, rank 1,764, 92,435 points, fell 16 places)
        byte[] rankFrame = Convert.FromHexString("578D00000100E4060000353B030000001805044161687A08416B617473756B6901002D00000000000000021369010000000000347B226B696C6C5F636F756E74223A31312C2264656174685F636F756E74223A34322C226173736973745F636F756E74223A36317DF0FFFFFF00000000120000000F0000003422000016C5282CA1010000");
        bool ranked = Aion2Rankings.TryParseOwnRank(rankFrame, t, out OwnRank own) && own is { Type: 1, Scope: 0, Entry: { Rank: 1764, ServerId: 1304, Name: "Aahz", Legion: "Akatsuki", ClassId: 2, Points: 92435, Change: -16, Grade: 18 } }
            && own.Entry.StatsJson.Contains("kill_count") && !Aion2Rankings.TryParseOwnRank(rankFrame.Take(60).ToArray(), t, out _);
        Console.WriteLine($"  -> the own rank of the ranking window is read: {ranked}");

        // the debug points of the recorder: an unknown opcode is a point the first time and once more after two quiet minutes, never a third time, a known opcode never
        var watchSet = new Aion2.Capture.UnknownFrameWatch(new[] { 1, 2 });
        byte[] probe = { 5, 0, 1, 2 };
        DateTime w0 = new(2026, 10, 23, 12, 0, 0);
        bool pointFirst = watchSet.Note(5, probe, w0) is { Opcode: 5, Length: 4, Seen: 1 };
        bool pointQuiet = watchSet.Note(5, probe, w0.AddSeconds(30)) is null && watchSet.Note(1, probe, w0) is null;
        bool pointAgain = watchSet.Note(5, probe, w0.AddSeconds(200)) is { Seen: 3 };
        bool pointCapped = watchSet.Note(5, probe, w0.AddSeconds(500)) is null && watchSet.UnknownOpcodes == 1;
        Console.WriteLine($"  -> an unknown frame is a debug point once, again after two quiet minutes, never more than twice, a known one never: {pointFirst && pointQuiet && pointAgain && pointCapped}");

        // the privacy answer: only an accepted current version allows uploads, never asked or an older version is asked, declined is the restricted mode and is not asked again
        var fresh = new Ui.MeterSettings();
        var accepted = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Accepted, PrivacyVersion = Ui.PrivacyConsent.Version };
        var older = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Accepted, PrivacyVersion = Ui.PrivacyConsent.Version - 1 };
        var declined = new Ui.MeterSettings { PrivacyDecision = Ui.PrivacyConsent.Declined };
        bool consent = !Ui.PrivacyConsent.IsAccepted(fresh) && Ui.PrivacyConsent.NeedsAsking(fresh) && Ui.PrivacyConsent.IsAccepted(accepted) && !Ui.PrivacyConsent.NeedsAsking(accepted)
            && !Ui.PrivacyConsent.IsAccepted(older) && Ui.PrivacyConsent.NeedsAsking(older) && !Ui.PrivacyConsent.IsAccepted(declined) && !Ui.PrivacyConsent.NeedsAsking(declined)
            && Ui.PrivacyConsent.RestrictedTools.SetEquals(new[] { "checklist", "attributes", "statcalc", "loot", "craft", "timetable", "theme", "network" });
        Console.WriteLine($"  -> the privacy answer: uploads only with the current version accepted, older or never asked is asked, declined is not asked again, eight tools stay: {consent}");

        Aion2Stock.Reset();
        return consent && pointFirst && pointQuiet && pointAgain && pointCapped && ranked && points && legion && chain && login && one && gone && loot && sort && oreAsked && plain && used && first && faction && known;
    }
}
