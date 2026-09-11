namespace AionSniffer.Data;

/// <summary>Which loot-fairness rule (see MainWindow's IsTrackedLoot) currently applies.</summary>
public enum LootTier
{
    /// <summary>No curated tier boss has been engaged yet this session - the ordinary rule applies.</summary>
    None,
    SixtyFive,
    Sixty,
    Hyperion,
}

/// <summary>
/// Curated boss name (every language variant actually shipped by THIS server, see below) -&gt;
/// which instance's loot-fairness tier it belongs to. Per the user, deliberately keyed on the
/// BOSS actually fought, not on the raw zone-channel text Chat.log narrates: Aion's own English
/// name for Tahmes is "Raksang", not "Tahmes" (confirmed below), so a zone-text guess would have
/// been just as wrong as the earlier "Gardenführer Achradim" boss-alias mistake.
///
/// Names were extracted from the client's own L10N/&lt;lang&gt;/Data/data.pak
/// strings/client_strings_monster.xml (a plain, unencrypted zip - unlike Npcs/npcs.pak and
/// World/World.pak, which this server encrypts with its own "OADTENC1" container and which this
/// project will not attempt to break), the same method already used for TrainingDummyNames.
/// Two of the eight L10N language folders are mislabeled on this server: "ita" actually ships
/// Polish text and "plk" actually ships Russian text (verified by script/vocabulary, not by
/// folder name - German/English/Spanish/French/Turkish all matched their own folder correctly).
/// Chinese has no entry at all for any of these four NPCs in the local client-string dump; those
/// four names come from aioncodex.com's own "cn" locale instead and are one step less certain
/// than the rest of this table for exactly that reason.
///
/// Sauro Supply Base (Sauro-Kriegsdepot), level 65, holds both keyed bosses - Guard Captain
/// Ahuradim (the easier 1-key) and Brigade General Sheba (the harder 2-key, an NPC whose OWN
/// German/French/Polish strings call her "Shita"/"Shita"/"Shita" while English and the
/// entrance/cutscene strings call her "Sheba" - a real inconsistency in the game's own
/// localization, not a mistake made here). Tahmes (Raksang in English, level 57+) holds Raksha
/// Boilheart. Infinity Shard (Katalamize in German/French/Polish/Turkish, "Infinity Shard" only
/// in English) holds the single boss "Hyperion" - the instance nearly everyone just calls by its
/// boss's name.
///
/// Per the user, the same "65er" jewelry rule also covers the rest of the level-65 Danuar
/// instance family: Danuar Reliquary (Modor/Grendal - two-phase fight, English calls her Modor,
/// every other language calls her Grendal instead, same inconsistency pattern as Sheba/Shita),
/// Illuminary Obelisk (Dynatoum/Dainatum - same English/everyone-else spelling split again), and
/// Danuar Sanctuary (Ukahim - no such split this time, every language keeps "Ukahim"). A fourth
/// instance in this family, Ophidan Bridge, is deliberately NOT curated here: it's a siege/PvP-
/// flavored instance with no single clear PvE boss NPC in the client strings (only a generic
/// "cannon" object, not a proper name, in every non-English language) - a real chat log from an
/// actual run would be needed to resolve it properly rather than guess.
/// </summary>
public static class InstanceTierDatabase
{
    private static readonly Dictionary<string, LootTier> TierByBossName = new(StringComparer.Ordinal)
    {
        // Guard Captain Ahuradim - Sauro Supply Base, 65er
        ["Guard Captain Ahuradim"] = LootTier.SixtyFive, // en
        ["Gardenführer Achradim"] = LootTier.SixtyFive, // de
        ["Líder de guardias Acradim"] = LootTier.SixtyFive, // es
        ["Chef de la garde Achradim"] = LootTier.SixtyFive, // fr
        ["Kierownik Ogrodu Achradim"] = LootTier.SixtyFive, // pl
        ["Командир стражи Ахрадим"] = LootTier.SixtyFive, // ru
        ["Muhafız Lideri Ahradim"] = LootTier.SixtyFive, // tr
        ["近卫队长阿赫拉丁"] = LootTier.SixtyFive, // zh (aioncodex, not client-verified)

        // Brigade General Sheba - Sauro Supply Base, 65er
        ["Brigade General Sheba"] = LootTier.SixtyFive, // en
        ["Brigadegeneralin Shita"] = LootTier.SixtyFive, // de
        ["General de brigada Sita"] = LootTier.SixtyFive, // es
        ["Général de brigade Shita"] = LootTier.SixtyFive, // fr
        ["Generał Brygady Shita"] = LootTier.SixtyFive, // pl
        ["Легат Шитха"] = LootTier.SixtyFive, // ru
        ["Tuğgeneral Şita"] = LootTier.SixtyFive, // tr
        ["第40军团长西塔"] = LootTier.SixtyFive, // zh (aioncodex, not client-verified)

        // Raksha Boilheart - Tahmes/Raksang, 60er (per the user: 57+ counts as "60er" here)
        ["Raksha Boilheart"] = LootTier.Sixty, // en
        ["Raksha Kochherz"] = LootTier.Sixty, // de
        ["Rajsá Pulsohirviente"] = LootTier.Sixty, // es
        ["Raksha Cœur-bouilli"] = LootTier.Sixty, // fr
        ["Raksha Serce Kucharza"] = LootTier.Sixty, // pl
        ["Разгневанный Ракши"] = LootTier.Sixty, // ru
        ["Rakşa Kaynayan Kalp"] = LootTier.Sixty, // tr
        ["愤怒的拉克莎"] = LootTier.Sixty, // zh (aioncodex, not client-verified)

        // Hyperion - Infinity Shard, no loot counted at all (personal loot boxes)
        ["Hyperion"] = LootTier.Hyperion, // en, de
        ["Hiperión"] = LootTier.Hyperion, // es
        ["Hypérion"] = LootTier.Hyperion, // fr
        ["Гиперион"] = LootTier.Hyperion, // ru
        ["Hiperion"] = LootTier.Hyperion, // tr
        ["希佩里安"] = LootTier.Hyperion, // zh (aioncodex, not client-verified)

        // Enraged/Cursed Queen Modor - Danuar Reliquary, 65er. Non-English clients call her
        // "Grendal" instead (both phases) - same real localization inconsistency as Sheba/Shita.
        ["Enraged Queen Modor"] = LootTier.SixtyFive, // en (2nd phase)
        ["Cursed Queen Modor"] = LootTier.SixtyFive, // en (1st phase)
        ["Verfluchte Hexe Grendal"] = LootTier.SixtyFive, // de (1st phase)
        ["Zornige Hexe Grendal"] = LootTier.SixtyFive, // de (2nd phase)
        ["Grendal la Bruja maldita"] = LootTier.SixtyFive, // es (1st phase)
        ["Grendal, la bruja enfurecida"] = LootTier.SixtyFive, // es (2nd phase)
        ["Sorcière Grendal maudite"] = LootTier.SixtyFive, // fr (1st phase)
        ["Sorcière Grendal enragée"] = LootTier.SixtyFive, // fr (2nd phase)
        ["Przeklęta Czarownica Grendal"] = LootTier.SixtyFive, // pl (1st phase)
        ["Gniewna Czarownica Grendal"] = LootTier.SixtyFive, // pl (2nd phase)
        ["Проклятая ведьма Грендаль"] = LootTier.SixtyFive, // ru (1st phase)
        ["Яростная ведьма Грендаль"] = LootTier.SixtyFive, // ru (2nd phase)
        ["Lanetli Cadı Grendal"] = LootTier.SixtyFive, // tr (1st phase)
        ["Öfkeli Cadı Grendal"] = LootTier.SixtyFive, // tr (2nd phase)

        // Test Weapon Dynatoum - Illuminary Obelisk, 65er
        ["Test Weapon Dynatoum"] = LootTier.SixtyFive, // en
        ["Prototyp Dainatum"] = LootTier.SixtyFive, // de
        ["Prototipo Dainatum"] = LootTier.SixtyFive, // es
        ["Prototype de Dainatum"] = LootTier.SixtyFive, // fr
        ["Тестовое орудие Дайнатум"] = LootTier.SixtyFive, // ru
        ["Prototip Dainatum"] = LootTier.SixtyFive, // tr

        // Virulent Ukahim - Danuar Sanctuary, 65er
        ["Virulent Ukahim"] = LootTier.SixtyFive, // en
        ["Schreckensklinge Ukahim"] = LootTier.SixtyFive, // de
        ["Ucaím Filoterrorífico"] = LootTier.SixtyFive, // es
        ["Lame-effroi Ukahim"] = LootTier.SixtyFive, // fr
        ["Ostrze Strachu Ukahim"] = LootTier.SixtyFive, // pl
        ["Убийца Укахим"] = LootTier.SixtyFive, // ru
        ["Korku Bıçağı Ukahim"] = LootTier.SixtyFive, // tr

        // Ophidan Bridge's final encounter (1 mage + 2 turrets), 65er - per the user, NOT yet
        // confirmed by a real Chat.log (their group hasn't run it): matched from the client's own
        // PVE-mode strings against the user's own description ("ein Mage und 2 Kanonen"), not the
        // "Engulfed"/War PVP variant's differently-named champions. Only English names the turrets
        // as proper nouns ("Vera"/"Surkana Aetherturret") - every other language just says
        // "cannon"/"turret" generically, so those generic words are still the real alias a
        // non-English upload would report. Weaker than every other entry in this table in one
        // specific way: "Vera" and "Top" (Turkish for "cannon") are also perfectly ordinary human
        // names, so a real character named either one would misfire this lookup - a risk no other
        // boss name here carries, worth knowing about even though it's still better than guessing
        // nothing at all for an instance the group hasn't run yet.
        ["Beritran Support Magus"] = LootTier.SixtyFive, // en
        ["Verstärkungsmagier der Reserveeinheit"] = LootTier.SixtyFive, // de
        ["Mago de refuerzo de la unidad de reserva"] = LootTier.SixtyFive, // es
        ["Mage de renfort de l'unité de réserve"] = LootTier.SixtyFive, // fr
        ["Magik Posiłków Jednostki Rezerwy"] = LootTier.SixtyFive, // pl
        ["Маг резерва"] = LootTier.SixtyFive, // ru
        ["Rezerve Bölüğü Takviye Büyücüsü"] = LootTier.SixtyFive, // tr
        ["Vera"] = LootTier.SixtyFive, // en
        ["Geschütz"] = LootTier.SixtyFive, // de
        ["Cañón"] = LootTier.SixtyFive, // es
        ["Canon"] = LootTier.SixtyFive, // fr
        ["Działo"] = LootTier.SixtyFive, // pl
        ["Бомбард"] = LootTier.SixtyFive, // ru
        ["Top"] = LootTier.SixtyFive, // tr
        ["Surkana Aetherturret"] = LootTier.SixtyFive, // en
        ["Surkana-Panzerabwehrätherkanone"] = LootTier.SixtyFive, // de
        ["Cañón etéreo de defensa de tanque de surcana"] = LootTier.SixtyFive, // es
        ["Canon à Éther de défense anti-char au Surkana"] = LootTier.SixtyFive, // fr
        ["Pancerne Eterowe Działo Obronne Surkany"] = LootTier.SixtyFive, // pl
        ["Мощная пушка сурканы"] = LootTier.SixtyFive, // ru
        ["Surkana Anti Panzer Eter Topu"] = LootTier.SixtyFive, // tr
    };

    public static LootTier TierOf(string bossName) =>
        TierByBossName.TryGetValue(bossName, out LootTier tier) ? tier : LootTier.None;
}
