namespace AionSniffer.Data;

/// <summary>
/// Curated ALLOWLIST of real, selectable end-boss names only - deliberately just the boss subset
/// of InstanceTierDatabase's own curated boss/trash-mob list (see that file's own remarks for full
/// provenance: names extracted from the client's own per-language L10N client_strings_monster.xml,
/// already verified against real Chat.log for everything included here). Per the user: only a
/// dungeon's actual final boss - the one also selectable via the game's own Instance Info GUI -
/// may ever appear in this app's own Mob/Boss dropdown/search or be uploaded; a mini-boss/trash mob
/// killed on the way must not, regardless of its own NPC rank (Elite and even Legendary-rank
/// "mini-bosses" exist on some paths to a real end boss, so rank alone is not a reliable signal -
/// this is an allowlist, not a rank-based heuristic).
///
/// Unlike a blocklist (reject only what's confirmed trash, permissive by default - too weak here,
/// since an unrecognized name is far more likely to be an uncurated mini-boss than a genuinely new
/// end boss this app has just never seen before), this is an ALLOWLIST: an unrecognized name is
/// EXCLUDED, not passed through. That means every instance the group actually runs must eventually
/// have its real end boss(es) added here by name (the same way every name below was found: from
/// the client's own L10N strings, never guessed) before this app will show or upload it - a real
/// gap today (only the handful of instances below are covered so far), but per the user, that gap
/// is strictly preferable to letting an unnoticed mini-boss slip through.
///
/// Every name below is verified directly against this server's own Aion game install
/// (D:\Spiele\AION\OriginAion\L10N\&lt;lang&gt;\data\data.pak, a plain zip, NOT the encrypted
/// Npcs/npcs.pak or World/World.pak) rather than guessed or taken from a third-party site: joined
/// across all 8 languages by the client's own stable per-language &lt;name&gt; key in
/// strings/client_strings_monster.xml (the real in-combat NAMEPLATE table - not
/// client_strings_dic_monster.xml, a separate lore/encyclopedia table that can and does contain its
/// own translation bugs unrelated to what Chat.log actually narrates, e.g. Ukahim's own Polish dic
/// entry misnaming her "Syaroka" while the real nameplate table correctly says "Ukahim"). Per the
/// user, the client also loads L10N/&lt;lang&gt; BEFORE L10N/2_&lt;lang&gt; (this server's own overlay,
/// which exists only for de/fr/en and only for a few hundred keys) - every name below was checked
/// against both and none of them differ, but the overlay would win if it ever did. "ita"/"plk" are
/// this server's own known folder mislabeling (verified indirectly by content/vocabulary, not by
/// folder name - "ita" actually ships Polish text, "plk" actually ships Russian text), same as
/// InstanceTierDatabase's own header remarks. A name genuinely missing from the client's own data
/// for a given language (not every NPC has all 8) is simply left out rather than guessed.
/// </summary>
public static class EndBossDatabase
{
    private static readonly HashSet<string> EndBossNames = new(StringComparer.Ordinal)
    {
        // Guard Captain Ahuradim - Sauro Supply Base, 1-key (easier)
        "Guard Captain Ahuradim", // en
        "Gardenführer Achradim", // de
        "Líder de guardias Acradim", // es
        "Chef de la garde Achradim", // fr
        "Kierownik Ogrodu Achradim", // pl
        "Командир стражи Ахрадим", // ru
        "Muhafız Lideri Ahradim", // tr
        "近卫队长阿赫拉丁", // zh

        // Brigade General Sheba - Sauro Supply Base, 2-key (harder)
        "Brigade General Sheba", // en
        "Brigadegeneralin Shita", // de
        "General de brigada Sita", // es
        "Général de brigade Shita", // fr
        "Generał Brygady Shita", // pl
        "Легат Шитха", // ru
        "Tuğgeneral Şita", // tr
        "第40军团长西塔", // zh

        // Raksha Boilheart - Tahmes/Raksang
        "Raksha Boilheart", // en
        "Raksha Kochherz", // de
        "Rajsá Pulsohirviente", // es
        "Raksha Cœur-bouilli", // fr
        "Raksha Serce Kucharza", // pl
        "Разгневанный Ракши", // ru
        "Rakşa Kaynayan Kalp", // tr
        "愤怒的拉克莎", // zh

        // Hyperion - Infinity Shard, sole boss
        "Hyperion", // en, de
        "Hiperión", // es
        "Hypérion", // fr
        "Гиперион", // ru
        "Hiperion", // tr
        "希佩里安", // zh

        // Queen Modor/Grendal - Danuar Reliquary, both phases
        "Enraged Queen Modor", // en (2nd phase)
        "Cursed Queen Modor", // en (1st phase)
        "Verfluchte Hexe Grendal", // de (1st phase)
        "Zornige Hexe Grendal", // de (2nd phase)
        "Grendal la Bruja maldita", // es (1st phase)
        "Grendal, la bruja enfurecida", // es (2nd phase)
        "Sorcière Grendal maudite", // fr (1st phase)
        "Sorcière Grendal enragée", // fr (2nd phase)
        "Przeklęta Czarownica Grendal", // pl (1st phase)
        "Gniewna Czarownica Grendal", // pl (2nd phase)
        "Проклятая ведьма Грендаль", // ru (1st phase)
        "Яростная ведьма Грендаль", // ru (2nd phase)
        "Lanetli Cadı Grendal", // tr (1st phase)
        "Öfkeli Cadı Grendal", // tr (2nd phase)

        // Test Weapon Dynatoum/Dainatum - Illuminary Obelisk
        "Test Weapon Dynatoum", // en
        "Prototyp Dainatum", // de, pl (identical text in both, confirmed separately)
        "Prototipo Dainatum", // es
        "Prototype de Dainatum", // fr
        "Тестовое орудие Дайнатум", // ru
        "Prototip Dainatum", // tr
        "实验兵器戴纳通", // zh

        // Danuar Sanctuary's three co-equal bosses
        "Virulent Ukahim", // en
        "Schreckensklinge Ukahim", // de
        "Ucaím Filoterrorífico", // es
        "Lame-effroi Ukahim", // fr
        "Ostrze Strachu Ukahim", // pl
        "Убийца Укахим", // ru
        "Korku Bıçağı Ukahim", // tr
        "斥候士官乌卡辛", // zh
        "Chief Medic Tagnu", // en
        "Oberheilerin Tagnu", // de
        "Maîtresse soigneuse Tagnu", // fr
        "Sanadora superior Tañu", // es
        "Капитан целителей Такну", // ru
        "Główna uzdrowicielka Tagnu", // pl
        "Yüksek Şifacı Tagnu", // tr
        "医务队长塔格努", // zh
        "Warmage Suyaroka", // en
        "Stabsoffizierin Syaroka", // de
        "Officier supérieur Syaroka", // fr
        "Oficial superior Siaroca", // es
        "Советница Саярока", // ru
        "Oficer sztabu Syaroka", // pl
        "Binbaşı Syaroka", // tr
        "参谋士官斯亚罗卡", // zh

        // Ophidan Bridge's final encounter (1 mage + 2 turrets) - per the user, all three ARE real
        // bosses of this instance (confirmed, unlike the tentative/unconfirmed status this same
        // trio carried in InstanceTierDatabase before). Only English names the turrets as proper
        // nouns - every other language just says "cannon"/"turret" generically, so those generic
        // words are still the real alias a non-English upload would report.
        "Beritran Support Magus", // en
        "Verstärkungsmagier der Reserveeinheit", // de
        "Mago de refuerzo de la unidad de reserva", // es
        "Mage de renfort de l'unité de réserve", // fr
        "Magik Posiłków Jednostki Rezerwy", // pl
        "Маг резерва", // ru
        "Rezerve Bölüğü Takviye Büyücüsü", // tr
        "预备部队法师支援兵", // zh
        "Vera", // en
        "Geschütz", // de
        "Cañón", // es
        "Canon", // fr
        "Działo", // pl
        "Бомбард", // ru
        "Top", // tr
        "投石炮", // zh
        "Surkana Aetherturret", // en
        "Surkana-Panzerabwehrätherkanone", // de
        "Cañón etéreo de defensa de tanque de surcana", // es
        "Canon à Éther de défense anti-char au Surkana", // fr
        "Pancerne Eterowe Działo Obronne Surkany", // pl
        "Мощная пушка сурканы", // ru
        "Surkana Anti Panzer Eter Topu", // tr
        "大战车苏尔卡纳魔力炮", // zh

        // Stahlrose: Anlegestelle (solo deck 1) - Maintenance Chief Notakiki, confirmed real and
        // already uploaded (see backend/public/i18n.js) - per the client's own text she's
        // "Rumakiki's right-hand man", not the overall raid's endboss, but she IS the real endboss
        // of THIS specific solo instance (a separate, selectable Instance Info entry of its own).
        "Maintenance Chief Notakiki", // en
        "Wartungsleiterin Notakiki", // de
        "Responsable de la maintenance Notakiki", // fr
        "Jefa de mantenimiento Notaquiqui", // es
        "Штурман Нотакики", // ru
        "Kierowniczka Konserwacji Notakiki", // pl
        "Bakım Amiri Notakiki", // tr (real nameplate table - the dic/encyclopedia table backend/public/i18n.js was built from says "Bakım Müdiresi" instead, a discrepancy between the two tables just like Ukahim's Polish one)
        "维修班长诺塔奇奇", // zh

        // Stahlrose: Kabine (solo deck 2) - Accountant Kanerunerk, confirmed real and already
        // uploaded (see backend/public/i18n.js), same reasoning as Notakiki above.
        "Accountant Kanerunerk", // en
        "Buchhalter Kanerunerk", // de
        "Comptable Kanerunerk", // fr
        "Contable Caneruner", // es
        "Бухгалтер Канэрунг", // ru
        "Księgowy Kanerunerk", // pl
        "Muhasebeci Kanerunerk", // tr
        "会计师卡内隆", // zh

        // Steel Rose's real GROUP-instance endboss (3rd deck/bridge, above Notakiki/Kanerunerk in
        // the overall raid - per the user, this whole deck was missing entirely until now, never
        // uploaded before). Nameplate table key "STR_IDRose_Shulack_Top_Ba_63_An2_Nmd" (matches
        // the real in-combat name "Captain Rumakiki", which differs from the
        // client_strings_dic_monster.xml catalog key "Steel Rose Rumakiki" used for icon/name
        // lookup elsewhere - see backend/public/i18n.js's own remarks).
        "Captain Rumakiki", // en
        "Kapitänin Rumakiki", // de
        "Capitaine Rumakiki", // fr
        "Capitana Rumaquiqui", // es
        "Kapitan Rumakiki", // pl
        "Капитан Румакики", // ru
        "Kaptan Rumakiki", // tr
        "船长路玛奇奇", // zh

        // Stahlmauerbastion (Steel Wall Bastion) - English name confirmed directly by the user,
        // every language confirmed against this server's own game install
        // (L10N/<lang>/data/data.pak, a plain zip - unzip -p "<lang>/data/data.pak"
        // strings/client_strings_monster.xml), joined by the client's own stable per-language
        // <name> key "STR_IDF5_TD_DragonRider_Chief_N_65_Al" (not by text - see this class's own
        // remarks style elsewhere). That key has a sibling "STR_BIDF5_..." key with a DIFFERENT
        // translation in fr/es/pl/ru/tr ("Governor ..." rather than "Commander-in-chief ...") even
        // though en/de happen to read identically either way - picked STR_IDF5_... specifically
        // because its de/en text ("Oberbefehlshaber"/"Commander-in-chief") is the one that actually
        // matches the user-confirmed English name's register. "Pashid"/"Paschid" alone is only ever
        // the Pashid Legion's own faction/zone-name adjective elsewhere in these strings ("Pashid
        // Defender", "Pashid Headquarters", etc. - same "legion name != boss's own name" pattern as
        // Sheban/Sheba above), never this boss's own full name/title by itself. ita/plk folders are
        // this server's own known de/plk<->pl/ru mislabeling (see this file's own header remarks).
        // No Chinese entry exists for this NPC at all in the client's own chn/data/data.pak.
        "Grand Commander Pashid", // en
        "Oberbefehlshaber Paschid", // de
        "Commandant en chef Paschid", // fr
        "Comandante en jefe Pashid", // es
        "Dowódca Paschid", // pl (from the "ita" folder - mislabeled, see header remarks)
        "Главнокомандующий Фашид", // ru (from the "plk" folder - mislabeled, see header remarks)
        "Başkomutan Paşid", // tr
    };

    /// <summary>True only for a curated real end-boss name - see this class's own remarks on why
    /// an unrecognized name is deliberately excluded rather than passed through.</summary>
    public static bool IsKnownEndBoss(string name) => EndBossNames.Contains(name);
}
