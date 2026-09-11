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
/// Per the user, the same "65er" jewelry rule also covers Sauro's own side-room trash mobs (see
/// their own remarks below - loot picked up before Ahuradim/Sheba is dead otherwise falls through
/// to the ordinary rule, since nothing has told CurrentLootTier which instance this is yet) and
/// the rest of the level-65 Danuar instance family: Danuar Reliquary (Modor/Grendal - two-phase
/// fight, English calls her Modor, every other language calls her Grendal instead, same
/// inconsistency pattern as Sheba/Shita), Illuminary Obelisk (Dynatoum/Dainatum - same
/// English/everyone-else spelling split again), Danuar Sanctuary (three co-equal bosses -
/// Tagnu/Suyaroka/Ukahim), and Ophidan Bridge's PVE encounter (Vera/Surkana Aetherturret/Beritran
/// Support Magus - see that entry's own remarks on why it's less certain than the rest of this
/// table, not yet confirmed by a real Chat.log).
/// </summary>
public static class InstanceTierDatabase
{
    private static readonly Dictionary<string, LootTier> TierByBossName = new(StringComparer.Ordinal)
    {
        // Sauro Supply Base's side-room trash mobs, not just its two real bosses - found necessary
        // from a real report: loot picked up on the way to Ahuradim/Sheba (before either is dead)
        // showed up untouched by the 65er rule, since nothing had yet told CurrentLootTier which
        // instance this is. These names/translations are already verified in the backend's own
        // GAME_NAME_TRANSLATIONS (see backend/public/i18n.js) against client_strings_dic_monster.xml.
        ["Guard Captain Rohuka"] = LootTier.SixtyFive, // en
        ["Wachhauptmann Rohuka"] = LootTier.SixtyFive, // de
        ["Capitaine de la garde Rohuka"] = LootTier.SixtyFive, // fr
        ["Capitán de la guardia Rojuca"] = LootTier.SixtyFive, // es
        ["Начальник охраны Рохка"] = LootTier.SixtyFive, // ru
        ["Kapitan Straży Rohuka"] = LootTier.SixtyFive, // pl
        ["Rohuka Nöbetçi Yüzbaşısı"] = LootTier.SixtyFive, // tr
        ["警备队长罗赫卡"] = LootTier.SixtyFive, // zh
        ["Chief Gunner Kurmata"] = LootTier.SixtyFive, // en
        ["Chefkanonierin Kurmata"] = LootTier.SixtyFive, // de
        ["Canonnier en chef Kurmata"] = LootTier.SixtyFive, // fr
        ["Jefe artillero Curmata"] = LootTier.SixtyFive, // es
        ["Главный канонир Курмата"] = LootTier.SixtyFive, // ru
        ["Naczelny Kanonier Kurmata"] = LootTier.SixtyFive, // pl
        ["Topçu Başı Kurmata"] = LootTier.SixtyFive, // tr
        ["炮兵队长库尔玛塔"] = LootTier.SixtyFive, // zh
        ["Derakanak the Reaver"] = LootTier.SixtyFive, // en
        ["Dunkelverschlinger Derakanak"] = LootTier.SixtyFive, // de
        ["Derakanak sombre-glouton"] = LootTier.SixtyFive, // fr
        ["Devorador oscuro Deracanac"] = LootTier.SixtyFive, // es
        ["Поглотитель тьмы Дераканак"] = LootTier.SixtyFive, // ru
        ["Pochłaniacz Ciemności Derakanak"] = LootTier.SixtyFive, // pl
        ["Karanlık Yok Edici Derakanak"] = LootTier.SixtyFive, // tr
        ["Chief of Staff Moriata"] = LootTier.SixtyFive, // en
        ["Stabschef Moriata"] = LootTier.SixtyFive, // de
        ["Chef d'état-major Moriata"] = LootTier.SixtyFive, // fr
        ["Jefe del estado mayor Moriata"] = LootTier.SixtyFive, // es
        ["Главный советник Мориата"] = LootTier.SixtyFive, // ru
        ["Szef Sztabu Moriata"] = LootTier.SixtyFive, // pl
        ["Alay Başkanı Moriata"] = LootTier.SixtyFive, // tr
        ["参谋长摩里亚塔"] = LootTier.SixtyFive, // zh
        ["Researcher Teselik"] = LootTier.SixtyFive, // en
        ["Forscherin Teselik"] = LootTier.SixtyFive, // de
        ["Chercheuse Teselik"] = LootTier.SixtyFive, // fr
        ["Investigadora Teselic"] = LootTier.SixtyFive, // es
        ["Исследовательница Тесерик"] = LootTier.SixtyFive, // ru
        ["Badaczka Teselik"] = LootTier.SixtyFive, // pl
        ["Araştırmacı Teselik"] = LootTier.SixtyFive, // tr
        ["研究专家泰塞里克"] = LootTier.SixtyFive, // zh
        ["Commander Ranodim"] = LootTier.SixtyFive, // en
        ["Versorgungskommandant Ranodim"] = LootTier.SixtyFive, // de
        ["Commandant chargé de l'approvisionnement Ranodim"] = LootTier.SixtyFive, // fr
        ["Comandante de abastecimiento Ranodim"] = LootTier.SixtyFive, // es
        ["Командир Ланодим"] = LootTier.SixtyFive, // ru
        ["Komendant Zaopatrzenia Ranodim"] = LootTier.SixtyFive, // pl
        ["Tedarik Komutanı Ranodim"] = LootTier.SixtyFive, // tr
        ["兵站指挥官拉诺丁"] = LootTier.SixtyFive, // zh
        ["Gatekeeper Stranir"] = LootTier.SixtyFive, // en
        ["Torwächter Slurt"] = LootTier.SixtyFive, // de
        ["Gardien Slurt"] = LootTier.SixtyFive, // fr
        ["Portero Eslurte"] = LootTier.SixtyFive, // es
        ["Защитник врат Слот"] = LootTier.SixtyFive, // ru
        ["Strażnik Bramy Slurt"] = LootTier.SixtyFive, // pl
        ["Kapı Muhafızı Slurt"] = LootTier.SixtyFive, // tr
        ["Darkblade Ovanuka"] = LootTier.SixtyFive, // en
        ["Inspektionsoffizier Obanuka"] = LootTier.SixtyFive, // de
        ["Officier inspecteur Obanuka"] = LootTier.SixtyFive, // fr
        ["Oficial de inspección Obanuca"] = LootTier.SixtyFive, // es
        ["Офицер-инспектор Ованка"] = LootTier.SixtyFive, // ru
        ["Oficer Inspekcji Obanuka"] = LootTier.SixtyFive, // pl
        ["Obanuka Teftiş Subayı"] = LootTier.SixtyFive, // tr
        ["Archmagus Sayahum"] = LootTier.SixtyFive, // en
        ["Inspektionsoffizier Sayahum"] = LootTier.SixtyFive, // de
        ["Officier inspecteur Sayahum"] = LootTier.SixtyFive, // fr
        ["Oficial de inspección Sayaum"] = LootTier.SixtyFive, // es
        ["Офицер-инспектор Саяхум"] = LootTier.SixtyFive, // ru
        ["Oficer Inspekcji Sayahum"] = LootTier.SixtyFive, // pl
        ["Sayahum Teftiş Subayı"] = LootTier.SixtyFive, // tr

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

        // Danuar Sanctuary's three co-equal bosses (the dictionary text names all three as
        // interchangeable "Special Research Team" commanders from "Beritra's Fang unit" - see
        // backend/public/i18n.js's own remarks on how this was confirmed).
        ["Virulent Ukahim"] = LootTier.SixtyFive, // en
        ["Schreckensklinge Ukahim"] = LootTier.SixtyFive, // de
        ["Ucaím Filoterrorífico"] = LootTier.SixtyFive, // es
        ["Lame-effroi Ukahim"] = LootTier.SixtyFive, // fr
        ["Ostrze Strachu Ukahim"] = LootTier.SixtyFive, // pl
        ["Убийца Укахим"] = LootTier.SixtyFive, // ru
        ["Korku Bıçağı Ukahim"] = LootTier.SixtyFive, // tr
        ["Chief Medic Tagnu"] = LootTier.SixtyFive, // en
        ["Oberheilerin Tagnu"] = LootTier.SixtyFive, // de
        ["Maîtresse soigneuse Tagnu"] = LootTier.SixtyFive, // fr
        ["Sanadora superior Tañu"] = LootTier.SixtyFive, // es
        ["Капитан целителей Такну"] = LootTier.SixtyFive, // ru
        ["Główna uzdrowicielka Tagnu"] = LootTier.SixtyFive, // pl
        ["Yüksek Şifacı Tagnu"] = LootTier.SixtyFive, // tr
        ["医务队长塔格努"] = LootTier.SixtyFive, // zh
        ["Warmage Suyaroka"] = LootTier.SixtyFive, // en
        ["Stabsoffizierin Syaroka"] = LootTier.SixtyFive, // de
        ["Officier supérieur Syaroka"] = LootTier.SixtyFive, // fr
        ["Oficial superior Siaroca"] = LootTier.SixtyFive, // es
        ["Советница Саярока"] = LootTier.SixtyFive, // ru
        ["Oficer sztabu Syaroka"] = LootTier.SixtyFive, // pl
        ["Binbaşı Syaroka"] = LootTier.SixtyFive, // tr
        ["参谋士官斯亚罗卡"] = LootTier.SixtyFive, // zh

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
