// Static game knowledge shared by the browser app (i18n.js, app.js) and the server (SEO shells,
// slug backfill) - one file so an instance's English name or boss portrait is never defined twice.
// Plain ESM data, no runtime dependencies. Keys are the literal names as stored in the DB.

// Instance/boss names come from the DB as a single literal string (whatever language the
// operator typed when mapping a boss to a real instance, see backend/README.md) - the UI
// translation table above has no reach into that data. Only verified name pairs go in here (en
// confirmed via Aion Wiki/Codex, de via the actual game client's own strings) - an unmapped name
// is left exactly as the DB has it rather than guessing a translation, same "never guess" rule
// the backend itself follows for instance/boss assignment.
// Verified straight from the game client's own Strings/client_strings_dic_place.xml (all 8
// L10N/<lang>/Data/data.pak, joined by the client's own <id> - see assets/README.md) rather than
// guessed or machine-translated.
export const GAME_NAME_TRANSLATIONS = {
  // Verified two ways: the client's own client_strings_dic_place.xml (id STR_DIC_W_IDRaksha_Whole,
  // mapCode IDRaksha) AND the real English Chat.log itself narrating "You have joined the Raksang
  // region channel." when fighting here - the German client calls the exact same zone "Tahmes".
  Tahmes: {
    en: "Raksang",
    de: "Tahmes",
    fr: "Raksang",
    es: "Tames",
    ru: "Тамарэс",
    pl: "Tahmes",
    tr: "Tahmes",
    zh: "塔梅斯",
  },
  // Verified from client_strings_dic_etc.xml (id STR_DIC_W_IDVritra_Base_all - the internal zone
  // code "Vritra_Base" has no relation to "Sauro" at all, it's purely the in-game display name).
  "Sauro-Kriegsdepot": {
    en: "Sauro Supply Base",
    de: "Sauro-Kriegsdepot",
    fr: "Dépôt de guerre de Sauro",
    es: "Almacén de Guerra de Sauro",
    ru: "Военная база Сауро",
    pl: "Skład Wojenny Sauro",
    tr: "Sauro Savaş Deposu",
    zh: "萨乌洛军需基地",
  },
  // Instance names below verified from assets/places/instances_multilang.json (itself pulled from
  // the client's own client_strings_dic_place.xml) - real DB rows now exist for all four, keyed by
  // their German name to match app.js's pre-staged INSTANCE_IMAGES (see that file's own remarks).
  Katalamize: {
    en: "Infinity Shard",
    de: "Katalamize",
    fr: "Katalamize",
    es: "Cantalonice",
    ru: "Каталамадж",
    pl: "Katalamize",
    tr: "Katalamize",
    zh: "卡塔拉麦兹",
  },
  Ruhnadium: {
    en: "Danuar Reliquary",
    de: "Ruhnadium",
    fr: "Ruhnadium",
    es: "Runadio",
    ru: "Рунадиум",
    pl: "Ruhnadium",
    tr: "Runadyum",
    zh: "鲁纳迪姆",
  },
  "Schutzturm der Ruhn": {
    en: "Illuminary Obelisk",
    de: "Schutzturm der Ruhn",
    fr: "Tour de garde ruhn",
    es: "Torre Protectora de los Run",
    ru: "Защитная башня рунов",
    pl: "Wieża Ochronna Ruhnów",
    tr: "Run koruma Kulesi",
    zh: "符文保护塔",
  },
  // Per the user: the DB row here holds the PVE variant (a mage + two turrets), not the War/PVP
  // fight - "Jormungand-Marschroute" above is that War variant's own German name (mapCode
  // IDLDF5_Under_01_War). Plain "Ophidan Bridge" (IDLDF5_Under_01, no _War) has a genuinely
  // different name of its own - found in client_strings_dic_etc.xml, not
  // instances_multilang.json (no row there for this map code either way). No zh entry - no zh
  // L10N pak on this install to check at all.
  "Ophidan Bridge": {
    en: "Ophidan Bridge",
    de: "Jormungand-Brücke",
    fr: "Pont de Jormungand",
    es: "Puente de Yórmungan",
    ru: "Мост Йормунганда",
    pl: "Most Jormunganda",
    tr: "Jormungand Köprüsü",
  },
  // Boss names below verified from client_strings_dic_monster.xml (Rohuka/Kurmata) and the raw,
  // per-language client_strings_monster.xml (Derakanak - not present in the "dic" table; see
  // assets/README.md's own remarks on that table missing some real bosses). All three joined
  // across languages by the client's own stable <name> string key (STR_..._Drakan_As_65_Ae2_Nmd
  // etc.), not just the raw numeric <id>, which is NOT guaranteed stable across language builds.
  "Wachhauptmann Rohuka": {
    en: "Guard Captain Rohuka",
    de: "Wachhauptmann Rohuka",
    fr: "Capitaine de la garde Rohuka",
    es: "Capitán de la guardia Rojuca",
    ru: "Начальник охраны Рохка",
    pl: "Kapitan Straży Rohuka",
    tr: "Rohuka Nöbetçi Yüzbaşısı",
    zh: "警备队长罗赫卡",
  },
  "Chefkanonierin Kurmata": {
    en: "Chief Gunner Kurmata",
    de: "Chefkanonierin Kurmata",
    fr: "Canonnier en chef Kurmata",
    es: "Jefe artillero Curmata",
    ru: "Главный канонир Курмата",
    pl: "Naczelny Kanonier Kurmata",
    tr: "Topçu Başı Kurmata",
    zh: "炮兵队长库尔玛塔",
  },
  // No zh entry: genuinely absent from this client's Chinese string table (dated 2014 internally,
  // an older content patch that predates this boss) - left untranslated there rather than guessed.
  "Dunkelverschlinger Derakanak": {
    en: "Derakanak the Reaver",
    de: "Dunkelverschlinger Derakanak",
    fr: "Derakanak sombre-glouton",
    es: "Devorador oscuro Deracanac",
    ru: "Поглотитель тьмы Дераканак",
    pl: "Pochłaniacz Ciemności Derakanak",
    tr: "Karanlık Yok Edici Derakanak",
  },
  // The remaining 7 Sauro-Kriegsdepot bosses (the instance has 10 total, not 3) - same
  // client_strings_dic_monster.xml/client_strings_monster.xml sourcing and cross-language <name>-key
  // join as Rohuka/Kurmata/Derakanak above.
  "Stabschef Moriata": {
    en: "Chief of Staff Moriata",
    de: "Stabschef Moriata",
    fr: "Chef d'état-major Moriata",
    es: "Jefe del estado mayor Moriata",
    ru: "Главный советник Мориата",
    pl: "Szef Sztabu Moriata",
    tr: "Alay Başkanı Moriata",
    zh: "参谋长摩里亚塔",
  },
  "Forscherin Teselik": {
    en: "Researcher Teselik",
    de: "Forscherin Teselik",
    fr: "Chercheuse Teselik",
    es: "Investigadora Teselic",
    ru: "Исследовательница Тесерик",
    pl: "Badaczka Teselik",
    tr: "Araştırmacı Teselik",
    zh: "研究专家泰塞里克",
  },
  "Versorgungskommandant Ranodim": {
    en: "Commander Ranodim",
    de: "Versorgungskommandant Ranodim",
    fr: "Commandant chargé de l'approvisionnement Ranodim",
    es: "Comandante de abastecimiento Ranodim",
    ru: "Командир Ланодим",
    pl: "Komendant Zaopatrzenia Ranodim",
    tr: "Tedarik Komutanı Ranodim",
    zh: "兵站指挥官拉诺丁",
  },
  // en is genuinely "Gatekeeper Stranir" in this client's own string table, not a "Slurt"
  // translated/transliterated from any other language - verified against the raw <name> key
  // (STR_IDVritra_Base_Vri_Ba_SN_65_Ae2), not guessed. No zh entry for the same reason as Derakanak
  // above (2014-dated Chinese table predates this boss).
  "Torwächter Slurt": {
    en: "Gatekeeper Stranir",
    de: "Torwächter Slurt",
    fr: "Gardien Slurt",
    es: "Portero Eslurte",
    ru: "Защитник врат Слот",
    pl: "Strażnik Bramy Slurt",
    tr: "Kapı Muhafızı Slurt",
  },
  // Same STR_IDVritra_Base_Vri_As_SN_65_Ae2 key as en "Darkblade Ovanuka", but this client's own
  // German text spells it "Obanuka" (v->b) and uses a different title entirely ("Inspektionsoffizier"
  // = Inspection Officer, not "Dunkelklingen-..."/Darkblade) - not a typo introduced here, the raw
  // client string itself. If the DB row for this boss was actually uploaded from an English client
  // and is keyed "Darkblade Ovanuka" instead, this entry's key needs adjusting to match - couldn't
  // verify the live `bosses.name` value from here. No zh entry, same reason as Slurt above.
  "Inspektionsoffizier Obanuka": {
    en: "Darkblade Ovanuka",
    de: "Inspektionsoffizier Obanuka",
    fr: "Officier inspecteur Obanuka",
    es: "Oficial de inspección Obanuca",
    ru: "Офицер-инспектор Ованка",
    pl: "Oficer Inspekcji Obanuka",
    tr: "Obanuka Teftiş Subayı",
  },
  // STR_IDVritra_Base_Vri_Wi_SN_65_Ae2 - same "Inspektionsoffizier" title pattern as Obanuka above
  // (they're a matched pair in this encounter, same client-side monster family). No zh entry, same
  // reason as Slurt/Obanuka above.
  "Inspektionsoffizier Sayahum": {
    en: "Archmagus Sayahum",
    de: "Inspektionsoffizier Sayahum",
    fr: "Officier inspecteur Sayahum",
    es: "Oficial de inspección Sayaum",
    ru: "Офицер-инспектор Саяхум",
    pl: "Oficer Inspekcji Sayahum",
    tr: "Sayahum Teftiş Subayı",
  },
  // STR_BIDVritra_Base_Boss2 (also duplicated verbatim under STR_IDVritra_Base_Drakan_Fi_B4_65_Ae2_Nmd).
  // Same "Ovanuka/Obanuka"-style spelling drift: this client's German text is "Achradim" (u->c, h
  // dropped), not "Ahuradim" - and translates the title as "Gardenführer" (literally "garden
  // leader"), not "Wachhauptmann"/Guard Captain as the en text says. Confirmed as the client's own
  // real text, not a decode error here - Polish independently carries the same "garden" mistranslation
  // ("Kierownik Ogrodu" = Garden Manager). Same live-DB-key caveat as Obanuka above applies. No zh
  // entry, same reason as Slurt/Obanuka/Sayahum above.
  // Per the user: "(1 Key)"/"(2 Key)" on this and Sheba below distinguish Sauro's two keyed
  // bosses by which door opens them - community shorthand for the Danuar Omphanium Key count, not
  // an official client string (unlike every other word in this table), so translated here rather
  // than extracted.
  "Gardenführer Achradim": {
    en: "Guard Captain Ahuradim (1 Key)",
    de: "Gardenführer Achradim (1 Schlüssel)",
    fr: "Chef de la garde Achradim (1 clé)",
    es: "Líder de guardias Acradim (1 llave)",
    ru: "Командир стражи Ахрадим (1 ключ)",
    pl: "Kierownik Ogrodu Achradim (1 klucz)",
    tr: "Muhafız Lideri Ahradim (1 anahtar)",
  },
  // Stored in the DB as the English name (uploaded from an English client) - translations for the
  // other 7 languages, verified the same way as the Sauro bosses above.
  "Raksha Boilheart": {
    en: "Raksha Boilheart",
    de: "Raksha Kochherz",
    fr: "Raksha Cœur-bouilli",
    es: "Rajsá Pulsohirviente",
    ru: "Разгневанный Ракши",
    pl: "Raksha Serce Kucharza",
    tr: "Rakşa Kaynayan Kalp",
    zh: "愤怒的拉克莎",
  },
  // The rest of Tahmes's bosses (12 more, all stored in the DB as their English name like Raksha
  // Boilheart above) - same client_strings_dic_monster.xml/client_strings_monster.xml sourcing as
  // the Sauro bosses. The four Seal Generators aren't in either monster table at all - they're
  // client_strings_npc.xml entries (STR_OBJ_IDRaksha_1F_Supply1-4), a mechanical object, not a
  // creature, which just happens to take damage and die like one.
  "Alpha Seal Generator": {
    en: "Alpha Seal Generator",
    de: "1. Siegel-Schutzgerät",
    fr: "Générateur du sceau alpha",
    es: "1.er Dispositivo de protección del sello",
    ru: "1-е устройство поддержания печати",
    pl: "1. Ochronne Urządzenie Pieczęci",
    tr: "1. Mühür Koruma Cihazı",
    zh: "第1封印维持装置",
  },
  "Beta Seal Generator": {
    en: "Beta Seal Generator",
    de: "2. Siegel-Schutzgerät",
    fr: "Générateur du sceau bêta",
    es: "2.º Dispositivo de protección del sello",
    ru: "2-е устройство поддержания печати",
    pl: "2. Ochronne Urządzenie Pieczęci",
    tr: "2. Mühür Koruma Cihazı",
    zh: "第2封印维持装置",
  },
  "Gamma Seal Generator": {
    en: "Gamma Seal Generator",
    de: "3. Siegel-Schutzgerät",
    fr: "Générateur du sceau gamma",
    es: "3.er Dispositivo de protección del sello",
    ru: "3-е устройство поддержания печати",
    pl: "3. Ochronne Urządzenie Pieczęci",
    tr: "3. Mühür Koruma Cihazı",
    zh: "第3封印维持装置",
  },
  "Delta Seal Generator": {
    en: "Delta Seal Generator",
    de: "4. Siegel-Schutzgerät",
    fr: "Générateur du sceau delta",
    es: "4.º Dispositivo de protección del sello",
    ru: "4-е устройство поддержания печати",
    pl: "4. Ochronne Urządzenie Pieczęci",
    tr: "4. Mühür Koruma Cihazı",
    zh: "第4封印维持装置",
  },
  // Two spaces between "Chantra" and "Fighter" is the client's own text, not a typo - matches the
  // DB row's name exactly (`bosses.name = 'Chantra  Fighter'`); do not "fix" the spacing here or
  // the translation stops matching.
  "Chantra  Fighter": {
    en: "Chantra  Fighter",
    de: "Tahmes-Beobachter der Chantra",
    fr: "Guerrier de Chantra",
    es: "Observador de Tames de la Chantra",
    ru: "Дракан-воитель 51-го легиона",
    pl: "Obserwator Tahmes Chantra",
    tr: "Çantra Tahmes Gözetleyicisi",
    zh: "第51德拉坎战斗兵",
  },
  "Chantra Guardian": {
    en: "Chantra Guardian",
    de: "Tahmes-Kämpfer der Chantra",
    fr: "Gardien de Chantra",
    es: "Luchador de Tames de la Chantra",
    ru: "Дракан-налетчик 51-го легиона",
    pl: "Wojownik Tahmes Chantra",
    tr: "Çantra Tahmes Dövüşçüsü",
    zh: "第51德拉坎突击兵",
  },
  "Drakan Seal Protector": {
    en: "Drakan Seal Protector",
    de: "Drakan-Siegelbewacher",
    fr: "Protecteur de sceau drakan",
    es: "Guardián del sello dracan",
    ru: "Запечатанный дракан-хранитель 51-го легиона",
    pl: "Stróż Pieczęci Drakan",
    tr: "Drakan Mühür Bekçisi",
    zh: "第51德拉坎封印守护兵",
  },
  // en/DB name "Gatekeeper Melkennis" - the other 7 languages call this same monster (same
  // STR_DIC_M_IDRaksha_0F_DrakanAssassin_kNmd1_57_Ae key) an "Overseer"/"Warden", not a
  // "Gatekeeper" - a real per-language title difference in the client, same as Ovanuka/Ahuradim in
  // the Sauro bosses above, not a translation error introduced here.
  "Gatekeeper Melkennis": {
    en: "Gatekeeper Melkennis",
    de: "Aufseher Mashutanu",
    fr: "Gardien[s] de porte Melkennis",
    es: "Guardián Masutanu",
    ru: "Защитник врат Машутану",
    pl: "Nadzorca Mashutanu",
    tr: "Gözetmen Maşutanu",
    zh: "门将马修塔努",
  },
  "Hellpath Guardian Fireye": {
    en: "Hellpath Guardian Fireye",
    de: "Präfekt Sumeda",
    fr: "Gardien œil-de-braise de la Marche des enfers",
    es: "Prefecto Sumeda",
    ru: "Караульный бездны Сумеда",
    pl: "Prefekt Sumeda",
    tr: "Reis Sumeda",
    zh: "地狱路卫兵苏美达",
  },
  "Kerop Deathguard": {
    en: "Kerop Deathguard",
    de: "Keros-Todeswächter",
    fr: "Garde[s] mortel[s] Keros",
    es: "Guarda mortal Queros",
    ru: "Цербер - хранитель смерти",
    pl: "Strażnik Śmierci Keros",
    tr: "Keros Ölüm Muhafızı",
    zh: "守护死亡的凯洛斯",
  },
  "Kerop Lifesnatch": {
    en: "Kerop Lifesnatch",
    de: "Keros-Lebensentreißer",
    fr: "Arrache-vie[s] Keros",
    es: "Arrebatavidas Queros",
    ru: "Смертоносный цербер",
    pl: "Wyrywacz Życia Keros",
    tr: "Keros Hayat Koparıcısı",
    zh: "夺取生命的凯洛斯",
  },
  "Raksang Rubble": {
    en: "Raksang Rubble",
    de: "Raksha-Trümmer",
    fr: "Gravas de Raksang",
    es: "Escombros de Rajsá",
    ru: "Обломок Тамарэса",
    pl: "Szczątki Rakshy",
    tr: "Rakşa Yıkıntısı",
    zh: "塔梅斯的残骸",
  },
  "Scout Ksellid": {
    en: "Scout Ksellid",
    de: "Späher-Ksellid",
    fr: "Éclaireur Ksellid",
    es: "Sélido explorador",
    ru: "Кселлид-разведчик",
    pl: "Zwiadowca Ksellid",
    tr: "Keşif Eri Ksellid",
    zh: "侦察科塞利德",
  },
  "Vasuki Lifespark": {
    en: "Vasuki Lifespark",
    de: "Vasuki-Lebensfunken",
    fr: "Vasuki Étincelle-de-vie",
    es: "Vasuqui Chispadevida",
    ru: "Реаниматор Паски",
    pl: "Iskra Życia Vasuki",
    tr: "Vasuki Yaşam Kıvılcımı",
    zh: "复活师巴苏奇",
  },
  "Stahlrose: Anlegestelle": {
    en: "Steel Rose Cargo",
    de: "Stahlrose: Anlegestelle",
    fr: "Embarcadère de la Rose d'acier",
    es: "Embarcadero de la Rosa de Acero",
    ru: "Пристань Стальной розы",
    pl: "Pracownica Stalowej Róży",
    tr: "Çelik Gülün İskelesi",
    zh: "铁玫瑰号船舱",
  },
  "Stahlrose: Kabine": {
    en: "Steel Rose Quarters",
    de: "Stahlrose: Kabine",
    fr: "Cabine de la Rose d'acier",
    es: "Cabina de la Rosa de Acero",
    ru: "Пассажирский салон Стальной розы",
    pl: "Kabina Stalowej Róży",
    tr: "Çelik Gülün Kabini",
    zh: "铁玫瑰号船室",
  },
  // Steel Rose's 3rd deck/bridge - the real GROUP-instance zone, above the two solo decks above
  // (Anlegestelle/Kabine). Names from the client's own places_multilang.json
  // (STR_DIC_W_IDShulackShip_02_SZ_3F), same source/method as the two entries above.
  "Stahlrose: Deck": {
    en: "Steel Rose Deck",
    de: "Stahlrose: Deck",
    fr: "Pont de la Rose d'acier",
    es: "Cubierta de la Rosa de Acero",
    ru: "Палуба Стальной розы",
    pl: "Pokład Stalowej Róży",
    tr: "Çelik Gülün Güvertesi",
    zh: "铁玫瑰号甲板",
  },
  // Stahlmauerbastion (Grand Commander Pashid) - names from places_multilang.json
  // (STR_DIC_W_IDLDF5b_TD), same source/method as everywhere else in this file.
  "Stahlmauerbastion": {
    en: "The Eternal Bastion",
    de: "Stahlmauerbastion",
    fr: "Bastion du mur d'acier",
    es: "Bastión del Muro de Acero",
    ru: "Неприступный бастион",
    pl: "Bastion Stalowego Muru",
    tr: "Çelik Duvar Tabyası",
    zh: "铁壁堡垒",
  },
  // Boss NPC names - verified the same way, from Strings/client_strings_dic_monster.xml (a
  // stable per-language ID there, unlike the raw nameplate table client_strings_monster.xml where
  // the same ID can land on a completely unrelated creature in a different language - "Zauberer
  // der Stahlrose"/Steel Rose Sorcerer only exists in that unstable table, so it's deliberately
  // left untranslated rather than risk showing the wrong monster's name).
  "Maintenance Chief Notakiki": {
    en: "Maintenance Chief Notakiki",
    de: "Wartungsleiterin Notakiki",
    fr: "Responsable de la maintenance Notakiki",
    es: "Jefa de mantenimiento Notaquiqui",
    ru: "Штурман Нотакики",
    pl: "Kierowniczka Konserwacji Notakiki",
    tr: "Bakım Müdiresi Notakiki",
    zh: "维修班长诺塔奇奇",
  },
  "Accountant Kanerunerk": {
    en: "Accountant Kanerunerk",
    de: "Buchhalter Kanerunerk",
    fr: "Comptable Kanerunerk",
    es: "Contable Caneruner",
    ru: "Бухгалтер Канэрунг",
    pl: "Księgowy Kanerunerk",
    tr: "Muhasebeci Kanerunerk",
    zh: "会计师卡内隆",
  },
  // Everything below is pre-staged ahead of any real Chat.log/upload evidence - none of these
  // fights have ever been recorded, so there is no `bosses` row for any of them yet and nothing
  // here does anything until one appears. Verified the same way as everything above (client's own
  // data.pak strings, joined by the stable <name> key across all 8 languages), not guessed - see
  // each group's own remarks for the specific keys/zones and any per-language naming quirks found
  // along the way.
  // Sauro-Kriegsdepot's actual two important endbosses, per the user - not the 9 side-room bosses
  // already tracked above (Sheba's bodyguards/staff), and not the other names the client's own
  // Boss_Ent_01..05 room chain or its separate BossN encounter set turned up along the way (Sly
  // Uterunerk, Medical Officer Surkihan, Inquisitor Jardaraka, Jadram the Mad, Chief Medical
  // Officer, Relics Expedition Leader) - none of those are one of the two the user cares about, and
  // none has ever actually been uploaded/fought in a real Chat.log seen so far (see
  // project_sauro_important_bosses in this session's memory for the full picture).
  //
  // "1 Schlüssel" (easier) - already one of the 9 side-room bosses above, same translation.
  // "2 Schlüssel" (harder, better loot) - de/fr/es/ru/pl/tr all call her "Shita", not "Sheba" (the
  // internal client key itself is _Shita_) - same per-language name drift as Ovanuka/Obanuka above,
  // not a mistake. Never actually uploaded/fought yet either.
  // "(2 Key)" - see Gardenführer Achradim's own remarks above.
  "Brigade General Sheba": {
    en: "Brigade General Sheba (2 Key)",
    de: "Brigadegeneralin der 40. Armee Shita (2 Schlüssel)",
    fr: "Général de brigade de la 40e armée Shita (2 clés)",
    es: "General de brigada Sita del 40.º ejército (2 llaves)",
    ru: "Командир 40-го легиона Шитха (2 ключа)",
    pl: "Generał Brygady 40 Armii, Shita (2 klucze)",
    tr: "40. Ordunun Tuğgenerali Şita (2 anahtar)",
    zh: "第40军团长西塔 (2把钥匙)",
  },
  // Steel Rose's actual final boss (all 3 decks combined) - "Maintenance Chief Notakiki" above is
  // explicitly described in the client's own text as "Rumakiki's right-hand man", not the real
  // endboss. Her own third deck/bridge area isn't in our instances table yet (no upload has reached
  // it) - keyed by the client_strings_dic_monster.xml name, which differs from the in-combat
  // nameplate "Captain Rumakiki".
  "Steel Rose Rumakiki": {
    en: "Steel Rose Rumakiki",
    de: "Stahlrose Rumakiki",
    fr: "Rumakiki Rose d'acier",
    es: "Rumaquiqui la Rosa de acero",
    ru: "Румакики Стальная роза",
    pl: "Stalowa Róża Rumakiki",
    tr: "Çelik Gül Rumakiki",
    zh: "铁玫瑰路玛奇奇",
  },
  // Tiamat's Fortress - all 6 named generals plus the instance's real endboss, Tiamat herself.
  // None of this instance's fights have ever been uploaded either - pre-staged the same way as the
  // Sauro chain above.
  "Brigade General Chantra": {
    en: "Brigade General Chantra",
    de: "Brigadegeneral Chantra",
    fr: "Général de brigade Chantra",
    es: "General de brigada Chantra",
    ru: "Легат Джантра",
    pl: "Generał Brygady Chantra",
    tr: "Tuğgeneral Çantra",
    zh: "军团长赞特拉",
  },
  "Brigade General Terath": {
    en: "Brigade General Terath",
    de: "Brigadegeneral Sadha",
    fr: "Général de brigade Sadha",
    es: "General de brigada Sada",
    ru: "Легат Садх",
    pl: "Generał Brygady Sadha",
    tr: "Tuğgeneral Sadha",
    zh: "军团长萨德哈",
  },
  "Traitor Kumbanda": {
    en: "Traitor Kumbanda",
    de: "Verräter Kumbanda",
    fr: "Kumbanda le traître",
    es: "Cumbanda el Traidor",
    ru: "Предатель Кумбанда",
    pl: "Zdrajca Kumbanda",
    tr: "Hain Kumbanda",
    zh: "背叛者昆班达",
  },
  "Brigade General Laksyaka": {
    en: "Brigade General Laksyaka",
    de: "Brigadegeneral Rakshaka",
    fr: "Général de brigade Rakshaka",
    es: "General de brigada Rajsaca",
    ru: "Легат Ракшака",
    pl: "Generał Brygady Rakshaka",
    tr: "Tuğgeneral Rakşa",
    zh: "军团长拉克夏卡",
  },
  "Adjutant Anuhart": {
    en: "Adjutant Anuhart",
    de: "Adjutant Anuhart",
    fr: "Adjudant Anuhart",
    es: "Edecán Anuhart",
    ru: "Офицер Анухарт",
    pl: "Adiutant Anuhart",
    tr: "Emir Subayı Anuhart",
    zh: "副官阿努哈尔特",
  },
  "Brigade General Tahabata": {
    en: "Brigade General Tahabata",
    de: "Brigadegeneral Tahabata",
    fr: "Général de brigade Tahabata",
    es: "General de brigada Tahabata",
    ru: "Легат Тахабата",
    pl: "Generał Brygady Tahabata",
    tr: "Tuğgeneral Tahabata",
    zh: "军团长塔哈巴塔",
  },
  // The instance's real endboss (3-phase fight: Drakan form -> dragon form -> "dying" form, same
  // name throughout).
  "Tiamat": {
    en: "Tiamat",
    de: "Tiamat",
    fr: "Tiamat",
    es: "Tiamat",
    ru: "Тиамат",
    pl: "Tiamat",
    tr: "Tiamat",
    zh: "提亚马特",
  },
  // Beshmundir Temple - considerably more than "4+1": the real client data shows at least 14
  // named room bosses (STR_IDCatacombs_Boss_<type> slots) before the actual endboss, Stormwing.
  // zh is missing for about half of these - a real gap in the extracted Chinese client dump for
  // this late endgame content, not a failed search.
  "Taros Lifebane": {
    en: "Taros Lifebane",
    de: "Taros Lebensbann",
    fr: "Taros Mort-fléau",
    es: "Taros Maldicevidas",
    ru: "Пленный воин Тарос",
    pl: "Klątwa Życia Taro",
    tr: "Taros Yaşam Aforozu",
  },
  "Macunbello": {
    en: "Macunbello",
    de: "Macunbello",
    fr: "Macunbello",
    es: "Macunbello",
    ru: "Темный волшебник Махунбелло",
    pl: "Macunbello",
    tr: "Makunbello",
  },
  "Captain Lakhara": {
    en: "Captain Lakhara",
    de: "Hauptmann Lakhara",
    fr: "Capitaine Lakhara",
    es: "Capitán Lajara",
    ru: "Капитан часовых Ракхара",
    pl: "Kapitan Lakhara",
    tr: "Yüzbaşı Lakhara",
  },
  "The Great Virhana": {
    en: "The Great Virhana",
    de: "Virhana der Große",
    fr: "Virhana le Grand",
    es: "Virhana el Grande",
    ru: "Памятник великому Вирхану",
    pl: "Wielki Virhana",
    tr: "Büyük Virhana",
  },
  "Ahbana the Wicked": {
    en: "Ahbana the Wicked",
    de: "Ahbana die Boshafte",
    fr: "Ahbana la Mauvaise",
    es: "Ahbana el Maligno",
    ru: "Привязанный Ахбана",
    pl: "Złośliwy Ahbana",
    tr: "Kötü Ahbana",
  },
  "Protector Pahraza": {
    en: "Protector Pahraza",
    de: "Beschützer Pahraza",
    fr: "Protecteur Pahraza",
    es: "Protector Pahraza",
    ru: "Задумчивый Фахран",
    pl: "Obrońca Pahraza",
    tr: "Koruyucu Fraza",
  },
  "Judge Kramaka": {
    en: "Judge Kramaka",
    de: "Richter Kramaka",
    fr: "Juge Kramaka",
    es: "Juez Cramaca",
    ru: "Ходатай Краман",
    pl: "Sędzia Kramaka",
    tr: "Hakim Kramaka",
  },
  "Dorakiki the Bold": {
    en: "Dorakiki the Bold",
    de: "Dorakiki der Dreiste",
    fr: "Dorakiki l'Audacieux",
    es: "Doraquiqui el Atrevido",
    ru: "Отважный Тораки",
    pl: "Zuchwały Dorakiki",
    tr: "Cesur Dorakiki",
  },
  "Manadar": {
    en: "Manadar",
    de: "Manadar",
    fr: "Manadar",
    es: "Manadar",
    ru: "Преданный Манадар",
    pl: "Manadar",
    tr: "Manadar",
  },
  "Shadowshift": {
    en: "Shadowshift",
    de: "Schattenschreiter",
    fr: "Crépuscule",
    es: "Pisasombras",
    ru: "Верный Сулаган",
    pl: "Kroczący w Cieniu",
    tr: "Gölge Nöbetçisi",
    zh: "忠诚的苏拉甘",
  },
  "The Plaguebearer": {
    en: "The Plaguebearer",
    de: "Pestbringer",
    fr: "Porte-peste",
    es: "Portapestes",
    ru: "Гигантский Мермук",
    pl: "Przynosiciel Zarazy",
    tr: "Veba Getiren",
  },
  "Flarestorm": {
    en: "Flarestorm",
    de: "Flammensturm",
    fr: "Brûle-tempête",
    es: "Tormentígneo",
    ru: "Фланас",
    pl: "Płomienna Burza",
    tr: "Alev Fırtınası",
  },
  "Thurzon the Undying": {
    en: "Thurzon the Undying",
    de: "Thurzon der Untote",
    fr: "Thurzon le Non-mort",
    es: "Turzon el No Muerto",
    ru: "Бессмертный Софин",
    pl: "Nieumarły Thurzon",
    tr: "Yaşayan Ölü Turzon",
    zh: "不死的斯皮纳特",
  },
  "Isbariya the Resolute": {
    en: "Isbariya the Resolute",
    de: "Isbariya der Entschlossene",
    fr: "Isbariya le déterminé",
    es: "Isbariya el Decidido",
    ru: "Хранитель печати Исбария",
    pl: "Isbariya Zdeterminowany",
    tr: "Kararlı İsbariya",
    zh: "封印守护者伊斯巴里亚",
  },
  // The instance's real endboss.
  "Stormwing": {
    en: "Stormwing",
    de: "Orkanschwinge",
    fr: "Aile-Ouragan",
    es: "Alaciclón",
    ru: "Рудра бури",
    pl: "Skrzydło Huraganu",
    tr: "Kasırga Kanatlı",
    zh: "封印的暴风之鲁德拉",
  },
  // Danuar Reliquary's endboss - en calls her "Enraged Queen Modor" (full name per Codex: Modor
  // Arrownail); the other 6 languages still show an older name, "Grendal" (confirms the "Furious
  // Grendal the Witch" lead) - same boss, different content-patch snapshot per language, not a
  // mismatch introduced here. No zh entry found.
  "Enraged Queen Modor": {
    en: "Enraged Queen Modor",
    de: "Zornige Hexe Grendal",
    fr: "Sorcière Grendal enragée",
    es: "Grendal, la bruja enfurecida",
    ru: "Яростная ведьма Грендаль",
    pl: "Gniewna Czarownica Grendal",
    tr: "Öfkeli Cadı Grendal",
  },
  // Danuar Sanctuary itself - "Zuflucht des Ruhn-Stammes" (Refuge of the Ruhn Tribe), verified from
  // client_strings_dic_etc.xml (STR_DIC_W_IDLDF5_Under_02_all), not instances_multilang.json (that
  // file has no row for this map code at all - see its own generation caveats).
  "Danuar Sanctuary": {
    en: "Danuar Sanctuary",
    de: "Zuflucht des Ruhn-Stammes",
    fr: "Sanctuaire du peuple ruhn",
    es: "Refugio de la Tribu Run",
    ru: "Прибежище рунов",
    pl: "Schronienie Plemienia Ruhnów",
    tr: "Run Kabilesi Sığınağı",
  },
  // Danuar Sanctuary's three co-equal bosses ("Special Research Team" commanders, all three from
  // "Beritra's Fang unit" per their identical dictionary blurb, STR_DIC_M_IDF5_U2_P_Vri*) - NOT two
  // as this entry originally said. Ukahim was missed here at first and, separately, mistaken for
  // this instance's ONLY boss in an earlier pass before the dictionary text (client_strings_dic_etc/
  // dic_monster.xml) turned up all three side by side.
  "Chief Medic Tagnu": {
    en: "Chief Medic Tagnu",
    de: "Oberheilerin Tagnu",
    fr: "Maîtresse soigneuse Tagnu",
    es: "Sanadora superior Tañu",
    ru: "Капитан целителей Такну",
    pl: "Główna uzdrowicielka Tagnu",
    tr: "Yüksek Şifacı Tagnu",
    zh: "医务队长塔格努",
  },
  // The user's lead named this "Staff Officer Syaroka" - the client's real en name is
  // "Warmage Suyaroka".
  "Warmage Suyaroka": {
    en: "Warmage Suyaroka",
    de: "Stabsoffizierin Syaroka",
    fr: "Officier supérieur Syaroka",
    es: "Oficial superior Siaroca",
    ru: "Советница Саярока",
    pl: "Oficer sztabu Syaroka",
    tr: "Binbaşı Syaroka",
    zh: "参谋士官斯亚罗卡",
  },
  "Virulent Ukahim": {
    en: "Virulent Ukahim",
    de: "Schreckensklinge Ukahim",
    fr: "Lame-effroi Ukahim",
    es: "Ucaím Filoterrorífico",
    ru: "Убийца Укахим",
    pl: "Ostrze Strachu Ukahim",
    tr: "Korku Bıçağı Ukahim",
  },
  // Infinity Shard's endboss. No zh entry found.
  "Hyperion": {
    en: "Hyperion",
    de: "Hyperion",
    fr: "Hypérion",
    es: "Hiperión",
    ru: "Гиперион",
    pl: "Hyperion",
    tr: "Hiperion",
  },
  // Illuminary Obelisk's endboss - real en name is "Test Weapon Dynatoum", not "Dynatum
  // Prototype".
  "Test Weapon Dynatoum": {
    en: "Test Weapon Dynatoum",
    de: "Prototyp Dainatum",
    fr: "Prototype de Dainatum",
    es: "Prototipo Dainatum",
    ru: "Тестовое орудие Дайнатум",
    pl: "Prototyp Dainatum",
    tr: "Prototip Dainatum",
    zh: "实验兵器戴纳通",
  },
  // Eternal Bastion - a wave-defense instance, not a classic dungeon: 3x3 wave commanders plus 2
  // base commanders before the real endboss, Grand Commander Pashid. zh missing throughout (same
  // gap as Beshmundir Temple above).
  "Pashid Scout Commander Azute": {
    en: "Pashid Scout Commander Azute",
    de: "Kommandant des Spähtrupps Azut",
    fr: "Commandant de la troupe de reconnaissance Azut",
    es: "Comandante Azut de la tropa de exploración",
    ru: "Командир разведки Азут",
    pl: "Komendant Oddziału Zwiadowczego Azut",
    tr: "Keşif Eri Birliği Azutun Komutanı",
  },
  "Pashid Scout Commander Zest": {
    en: "Pashid Scout Commander Zest",
    de: "Kommandant des 43. Spähtrupps Zest",
    fr: "Commandant de la 43e troupe de reconnaissance Zest",
    es: "Comandante Cest de la 43.ª tropa de exploración",
    ru: "Командир разведки 43-го легиона Зест",
    pl: "Komendant 43 Oddziału Zwiadowczego Zest",
    tr: "43. Keşif Eri Birliği Komutanı",
  },
  "Pashid Scout Commander Sartas": {
    en: "Pashid Scout Commander Sartas",
    de: "Kommandant des 43. Spähtrupps Sartas",
    fr: "Commandant de la 43e troupe de reconnaissance Sartas",
    es: "Comandante Sartas de la 43.ª tropa de exploración",
    ru: "Командир разведки 43-го легиона Сартас",
    pl: "Komendant 43 Oddziału Zwiadowczego Sarty",
    tr: "43. Keşif Eri Birliği Sartasın Komutanı",
  },
  "Pashid Infantry Commander Matuk": {
    en: "Pashid Infantry Commander Matuk",
    de: "Gefechtskommandant der 43. Armee Matuk",
    fr: "Commandant de combat de la 43e armée Matuk",
    es: "Comandante de combate Matuc del 43.er ejército",
    ru: "Командир ударного отряда 43-го легиона Матук",
    pl: "Komendant Walki 43 Armii Matuk",
    tr: "43. Ordu Matukun Çatışma Komutanı",
  },
  "Pashid Assault Commander Badute": {
    en: "Pashid Assault Commander Badute",
    de: "Kommandant des 43. Sturmtrupps Badut",
    fr: "Commandant du 43e escadron d'assaut Badut",
    es: "Comandante Badut de la 43.ª tropa de ataque",
    ru: "Командир ударного отряда 43-го легиона Бадут",
    pl: "Komendant 43 Oddziału Szturmowego Badut",
    tr: "43. Taaruz Birliği Badutun Komutanı",
  },
  "Pashid Assault Commander Katsu": {
    en: "Pashid Assault Commander Katsu",
    de: "Kommandant des 43. Sturmtrupps Kasutu",
    fr: "Commandant du 43e escadron d'assaut Kasutu",
    es: "Comandante Casutu de la 43.ª tropa de ataque",
    ru: "Командир ударного отряда 43-го легиона Касту",
    pl: "Komendant 43 Oddziału Szturmowego Kasutu",
    tr: "43. Taaruz Birliği Kasutunun Komutanı",
  },
  "Pashid Artillery Commander Murat": {
    en: "Pashid Artillery Commander Murat",
    de: "Kommandant des 43. Kanoniertrupps Murat",
    fr: "Commandant de la 43e troupe d'artillerie Murat",
    es: "Comandante Murat de la 43.ª tropa de artilleros",
    ru: "Командир артиллерии 43-го легиона Мурат",
    pl: "Komendant 43 Oddziału Kanonierów Murat",
    tr: "43. Topçu Birliği Muratın Komutanı",
  },
  "Pashid Artillery Commander Kaimdu": {
    en: "Pashid Artillery Commander Kaimdu",
    de: "Kommandant des 43. Kanoniertrupps Kaimdu",
    fr: "Commandant de la 43e troupe d'artillerie Kaimdu",
    es: "Comandante Caimdú de la 43.ª tropa de artilleros",
    ru: "Командир артиллерии 43-го легиона Каимду",
    pl: "Komendant 43 Oddziału Kanonierów Kaimdu",
    tr: "43. Topçu Birliği Kaimdunun Komutanı",
  },
  "Pashid Infantry Commander Nirta": {
    en: "Pashid Infantry Commander Nirta",
    de: "Gefechtskommandant der 43. Armee Nirta",
    fr: "Commandant de combat de la 43e armée Nirta",
    es: "Comandante de combate Nirta del 43.er ejército",
    ru: "Командир артиллерии 43-го легиона Нирта",
    pl: "Komendant Walki 43 Armii Nirta",
    tr: "43. Ordu Nirtanın Çatışma Komutanı",
  },
  "Commander Hakunta": {
    en: "Commander Hakunta",
    de: "Kommandant Hakunda",
    fr: "Commandant Hakunda",
    es: "Comandante Hacunda",
    ru: "Командир цитадели Хакунда",
    pl: "Komendant Hakunda",
    tr: "Komutan Hakunda",
    zh: "据点指挥官哈昆塔",
  },
  "Commander Rakunta": {
    en: "Commander Rakunta",
    de: "Kommandant Lakunda",
    fr: "Commandant Lakunda",
    es: "Comandante Lacunda",
    ru: "Командир цитадели Ракунта",
    pl: "Komendant Lakunda",
    tr: "Komutan Lakunda",
    zh: "据点指挥官拉昆塔",
  },
  // The real endboss of Eternal Bastion.
  "Grand Commander Pashid": {
    en: "Grand Commander Pashid",
    de: "Oberbefehlshaber Paschid",
    fr: "Commandant en chef Paschid",
    es: "Comandante en jefe Pashid",
    ru: "Главнокомандующий Фашид",
    pl: "Naczelny Dowódca Paschid",
    tr: "Başkomutan Paşid",
    zh: "总指挥官帕希德",
  },
  // "Tiamat Treasure Hoard", not "Satra Treasure Hoard" - the internal zone code (IDTiamat_Reward)
  // says Tiamat, not Satra. Two phases of the same dragon.
  "Muzzled Punisher": {
    en: "Muzzled Punisher",
    de: "Gynulash",
    fr: "Vengeur muselé",
    es: "Giniurás",
    ru: "Каратель Гинраш",
    pl: "Gynulash",
    tr: "Ginulaş",
    zh: "惩罚者基纽拉希",
  },
  "Punisher Unleashed": {
    en: "Punisher Unleashed",
    de: "Unkontrollierbarer Gynulash",
    fr: "Vengeur déchaîné",
    es: "Giniurás el Incontrolable",
    ru: "Взбешенный Гинраш",
    pl: "Nieposkromiony Gynulash",
    tr: "Kontrol Edilemeyen Ginulaş",
    zh: "暴走的基纽拉希",
  },
  // Endboss of "Void Cube", not "Void Room" - only the display name differs from the user's lead.
  "Furious Barukan": {
    en: "Furious Barukan",
    de: "Rasender Barukan",
    fr: "Barukan l'insaisissable",
    es: "Barucan el Atroz",
    ru: "Яростный Барукан",
    pl: "Wściekły Barukan",
    tr: "Hızlı Barukan",
    zh: "暴走的巴鲁坎",
  },
  // Tiamat's Shelter's boss.
  "Calindi Flamelord": {
    en: "Calindi Flamelord",
    de: "Calindi Flammenlord",
    fr: "Calindi, Seigneur des flammes",
    es: "Calindi el Señor de las Llamas",
    ru: "Хозяин пламени Каллинди",
    pl: "Pan Płomieni Calindi",
    tr: "Alev Lordu Kalindi",
    zh: "火焰的主人卡林迪",
  },
  // Padmarashka's Cave's boss - Russian calls the same monster "Marissa" instead (a different
  // content-patch snapshot, same pattern as the Danuar Reliquary boss above), not a mistake here.
  "Padmarashka": {
    en: "Padmarashka",
    de: "Padmarashka",
    fr: "Padmarashka",
    es: "Padmarasca",
    ru: "Чуткая Марисса",
    pl: "Padmarashka",
    tr: "Padmaraşka",
    zh: "敏锐的帕德玛夏",
  },
  // Rentus Base's boss.
  "Brigade General Vasharti": {
    en: "Brigade General Vasharti",
    de: "Brigadegeneral Vasharti",
    fr: "Général de brigade de Vasharti",
    es: "General de brigada Vasarti",
    ru: "Легат Васатри",
    pl: "Generał Brygady Vasharti",
    tr: "Tuğgeneral Vaşarti",
    zh: "军团长巴萨尔提",
  },
  // Ophidan Bridge's PVE encounter (1 mage + 2 turrets) - real and confirmed per the client's own
  // EndBossDatabase.cs (fr/es/pl/ru/tr/zh copied straight from there, already verified against
  // Origin's own game install). "Vera" is also just an ordinary human name in English, so a real
  // character named that would misfire this lookup - a risk this one entry carries that no other
  // boss name here does.
  "Vera": {
    en: "Vera",
    de: "Geschütz",
    fr: "Canon",
    es: "Cañón",
    pl: "Działo",
    ru: "Бомбард",
    tr: "Top",
    zh: "投石炮",
  },
  "Beritran Support Magus": {
    en: "Beritran Support Magus",
    de: "Verstärkungsmagier der Reserveeinheit",
    fr: "Mage de renfort de l'unité de réserve",
    es: "Mago de refuerzo de la unidad de reserva",
    pl: "Magik Posiłków Jednostki Rezerwy",
    ru: "Маг резерва",
    tr: "Rezerve Bölüğü Takviye Büyücüsü",
    zh: "预备部队法师支援兵",
  },
  "Surkana Aetherturret": {
    en: "Surkana Aetherturret",
    de: "Surkana-Panzerabwehrätherkanone",
    fr: "Canon à Éther de défense anti-char au Surkana",
    es: "Cañón etéreo de defensa de tanque de surcana",
    pl: "Pancerne Eterowe Działo Obronne Surkany",
    ru: "Мощная пушка сурканы",
    tr: "Surkana Anti Panzer Eter Topu",
    zh: "大战车苏尔卡纳魔力炮",
  },
  // These three were added (migration 0012) without ever getting an entry here - missed at the
  // time, found while adding Tiamats Festung below. English names per the user's own later, more
  // detailed instance list.
  "Beshmundirs Tempel": {
    en: "Beshmundir Temple",
    de: "Beshmundirs Tempel",
  },
  "Rentus-Basis": {
    en: "Rentus Base",
    de: "Rentus-Basis",
  },
  // Not "Dragon Lord's Refuge" (this app's own earlier guess) - the user's own later list calls
  // this one "Tiamat's Hideout".
  "Tiamats Unterschlupf": {
    en: "Tiamat's Hideout",
    de: "Tiamats Unterschlupf",
  },
  // aionriftshade.com's own 4.8 content below - verified against that server's own game install
  // (L10N/deu/Data/data.pak; its English pack was still mid-download at the time, per the user, so
  // only German is client-confirmed here - see app.js's own INSTANCE_IMAGES remarks and the
  // migration that added these rows).
  "Aturam Sky Fortress": {
    en: "Aturam Sky Fortress",
    de: "Aturam-Himmelsfestung",
  },
  // The Drakan boss guarding the Aturam Sky Fortress's control room - client_strings_monster.xml id
  // STR_IDStation_DrakanNinja_NM_58_An. No fr/es/pl/ru/tr/zh L10N pack was available to check yet.
  "Ashunatal Shadowslip": {
    en: "Ashunatal Shadowslip",
    de: "Ashunatal-Schattengleiter",
  },
  // mapCode IDLDF4Re_01 - per the user, "Baruna Research Laboratory" is this instance's real name
  // (not "Linkgate Foundry", this app's own earlier guess - see migration 0015). Matches the lore
  // text found on Belsagos itself below ("Baruna-Forschungslabor"). "Linkgate Foundry" is a
  // genuinely separate, still-unconfirmed instance per the user - not this one.
  "Baruna Research Laboratory": {
    en: "Baruna Research Laboratory",
  },
  // One NPC across 3 escalating named states (client_strings_monster.xml, id prefix
  // STR_IDLDF4_Re_01_BOSS_Fi) - "Belsagos" itself is the proper noun per
  // client_strings_dic_monster.xml; no English phase-adjective text confirmed yet (English pack
  // still downloading), so only the bare name is given here rather than guessed.
  "Belsagos": {
    en: "Belsagos",
    de: "Belsagos",
  },
  // mapCode IDLDF5RE_solo - de "Halle des Wissens" is a direct, unambiguous match for this English
  // name (unlike most entries here, not a guess bridging two differently-drifted names). Per the
  // user's own later, more detailed list this instance actually DOES have scenario-dependent
  // bosses ("Secret Test Subject 48123-A"/"Doomtread Kurores") - neither confirmed in this app's
  // own client data yet, so no boss row exists here so far (see backend/README.md's curation
  // pattern) - not, as first assumed, a classic-endboss-free instance.
  "Hall of Knowledge": {
    en: "Hall of Knowledge",
    de: "Halle des Wissens",
  },
  // mapCode IDTiamat_1 (en "Tiamat Stronghold" per assets/places/instances_multilang.json, though
  // the user's own name below is used as the canonical one here, same as this file's usual
  // practice of preferring the user/community name when it's more specific than the raw dictionary
  // string) - NOT the same instance as "Tiamats Unterschlupf"/IDTiamat_2 below, whose own boss is
  // Tiamat herself.
  "Tiamats Festung": {
    en: "Tiamat's Fortress",
    de: "Tiamats Festung",
  },
  // Tiamats Festung's endboss - client_strings_dic_monster.xml id
  // STR_DIC_M_IDTiamat_Tahabata_Named_60_Ah ("Brigadegeneral Tahabata"), confirmed the same in the
  // real nameplate table and in assets/npcs/npcs_en_4x.json (id 219358, Heroic).
  "Brigade General Tahabata": {
    en: "Brigade General Tahabata",
    de: "Brigadegeneral Tahabata",
  },
  // Everything from here down is taken directly from the user's own 4.8 instance list, NOT
  // independently verified against any client string dump (unlike every entry above - the German
  // L10N pack that made that possible is gone from this server's install, and the English one/its
  // Riftshade-specific override both turned out to be non-plain-zip/obfuscated). English only, on
  // the user's own explicit decision to accept the list as given rather than leave these entries
  // missing. Deliberately NOT added to the C# client's EndBossDatabase.cs allowlist for the same
  // reason - a real upload for any of these still gets rejected by the client until someone
  // verifies the real names.
  "Mantor": { en: "Mantor" },
  "Nasto": { en: "Nasto" },
  "Jormungand's Bridge": { en: "Jormungand's Bridge" },
  "Fugitive Mazikin": { en: "Fugitive Mazikin" },
  // "Escapee Asachin", not "Fugitive Asachin" (this app's own earlier name, per the user's
  // original report) - aion.fandom.com's Ophidan Bridge/Jormungand's Bridge page uses this
  // spelling instead. Corrected here and in the DB (see the migration that renamed this row).
  "Escapee Asachin": { en: "Escapee Asachin" },
  "Velkur": { en: "Velkur" },
  "Linkgate Foundry": { en: "Linkgate Foundry" },
  // Hall of Knowledge itself already has an entry above (added migration 0010, on the ORIGINAL
  // list's claim of "no classic endboss") - just its two scenario-dependent bosses are new here.
  "Secret Test Subject 48123-A": { en: "Secret Test Subject 48123-A" },
  "Doomtread Kurores": { en: "Doomtread Kurores" },
  "Lost Rentus Base": { en: "Lost Rentus Base" },
  "Lost Refuge": { en: "Lost Refuge" },
  "Tiamat's Hidden Space": { en: "Tiamat's Hidden Space" },
  "Makarna": { en: "Makarna" },
  "Beritrakt": { en: "Beritrakt" },
};

// Per the user: real client loading-screen art (see backend/public/images/instances/, sourced from
// this project's own AION client - Textures/loading/loading_<zone>.dds, decoded/cropped/re-encoded,
// not fabricated) instead of the plain text list this used to be. Keyed by the literal instance
// name, same convention as i18n.js's GAME_NAME_TRANSLATIONS - an instance with no entry here just
// renders without a photo (icon()'s own onerror-remove handles a bad path the same way), it's never
// guessed. Steel Rose's two tracked sub-instances share one image on purpose: the client itself only
// ships a single loading screen for the whole ship (see the 3 identical loading_IDShulack_rose_0N.dds
// files - checked by hash, not assumed).
export const INSTANCE_IMAGES = {
  "Sauro-Kriegsdepot": "/images/instances/sauro.jpg",
  Tahmes: "/images/instances/tahmes.jpg",
  "Stahlrose: Anlegestelle": "/images/instances/steelrose.jpg",
  "Stahlrose: Kabine": "/images/instances/steelrose.jpg",
  "Stahlrose: Deck": "/images/instances/steelrose.jpg",
  // The rest are pre-staged the same way as the Sauro/Tahmes boss-name translations in i18n.js -
  // none of these instances have ever been uploaded yet, so the key (the exact German name a real
  // upload would carry, per assets/places/instances_multilang.json's own "de" field) is provisional
  // until a real row confirms it. "Ruhnadium"/"Jormungand-Marschroute" are the client's real German
  // names, not "Danuar Reliquary"/"Ophidan Bridge" translated - same per-language-name-drift pattern
  // documented throughout i18n.js.
  "Beshmundirs Tempel": "/images/instances/beshmundir.jpg",
  Ruhnadium: "/images/instances/danuar_reliquary.jpg",
  "Schutzturm der Ruhn": "/images/instances/illuminary_obelisk.jpg",
  Katalamize: "/images/instances/infinity_shard.jpg",
  Stahlmauerbastion: "/images/instances/eternal_bastion.jpg",
  "Schlachtfeld der Stahlmauerbastion": "/images/instances/iron_wall_warfront.jpg",
  "Jormungand-Marschroute": "/images/instances/ophidan_bridge.jpg",
  // The PVE variant (the one actually curated so far - a mage plus two named turrets, not the
  // War/PVP siege fight above) - reuses the same loading-screen art, checked by eye (a generic
  // icy-cavern bridge shot, nothing War/PVP-specific in it), same "one photo, several
  // sub-instances" reasoning as Steel Rose's two decks above.
  "Ophidan Bridge": "/images/instances/ophidan_bridge.jpg",
  "Rentus-Basis": "/images/instances/rentus_base.jpg",
  // Real in-game screenshot ("TS - North Wing"), not cinematic loading-screen art - from
  // aion.fandom.com's own Tiamat Stronghold page (static.wikia.nocookie.net), replacing an earlier
  // fortress-skyline art piece per the user.
  "Tiamats Festung": "/images/instances/tiamat_stronghold.webp",
  // Per the user: NOT the same photo as Tiamats Festung above - a real encounter shot of Tiamat
  // herself (the dragon) facing down a Daeva in astral form, not the fortress skyline. Sourced from
  // a Google Images cache link the user provided rather than this project's usual client-file/wiki
  // sourcing, so the original page is unconfirmed - fix the provenance comment once a primary source
  // turns up.
  "Tiamats Unterschlupf": "/images/instances/tiamat_hideout.jpg",
  // These two keyed by English name instead (like "Raksha Boilheart" above) - found via
  // origincdx.com's own map list (IDLDF5Re_03 / IDLDF5_Under_02), but not present under either name
  // in this client's own client_strings_dic_place.xml, so the real German name a German-client
  // upload would actually carry is unconfirmed - fix the key once a real row shows it.
  "Void Cube": "/images/instances/void_cube.jpg",
  "Danuar Sanctuary": "/images/instances/danuar_sanctuary.jpg",
  // These three, plus Ashunatal Shadowslip/Belsagos in BOSS_IMAGES below, are aionriftshade.com's
  // own 4.8 content - photos are the real loading-screen art (Textures/loading/*.dds, a plain
  // uncompressed-DXT1 file, not the encrypted Npcs/World paks), decoded straight from that
  // server's own game install rather than a screenshot or a wiki crop. Filed under
  // linkgate_foundry.png from when this instance was still misnamed "Linkgate Foundry" (see
  // migration 0015) - the real "Linkgate Foundry" is a separate, still-unconfirmed instance, so the
  // file wasn't renamed to avoid implying that one now has a photo too.
  "Aturam Sky Fortress": "/images/instances/aturam.png",
  "Baruna Research Laboratory": "/images/instances/linkgate_foundry.png",
  "Hall of Knowledge": "/images/instances/danuar_mysticarium.png",
  // Real in-game screenshot from aion.fandom.com's own "Raksang Ruins" page, which explicitly
  // confirms "also known as Mantor" - not this project's usual client-file/loading-screen
  // sourcing (that path is blocked for this still-unconfirmed instance), but a real, on-topic
  // screenshot rather than a guess.
  Mantor: "/images/instances/mantor.jpg",
  // Both "Lost" - reuse their own already-covered non-Lost counterpart's real loading-screen
  // photo rather than fetch a lower-quality alternative: same location/reskin, same "one photo,
  // several sub-instances" reasoning as Steel Rose's two decks above.
  "Lost Rentus Base": "/images/instances/rentus_base.jpg",
  "Lost Refuge": "/images/instances/danuar_sanctuary.jpg",
  // Per the user: the same place as Tiamats Unterschlupf (an alliance-size/HM mode of it, not a
  // separate location) - reuses its encounter photo above, NOT Tiamats Festung's.
  "Tiamat's Hidden Space": "/images/instances/tiamat_hideout.jpg",
  // Real official loading-screen art (visible "AION" watermark, bottom-left) of the instance's own
  // icy dragon-like boss in its arena - per the user, via a Google Images cache link, same
  // unconfirmed-original-page caveat as Tiamats Unterschlupf's photo above.
  Makarna: "/images/instances/makarna.jpg",
};

// Per the user: real per-boss art, not the instance's own photo reused - found on aion.fandom.com,
// which turns out to keep one dedicated character-model render per named Sauro/Tahmes boss (found
// via its own MediaWiki API, allimages with the boss's exact English title as the filename prefix -
// e.g. "Guard_Captain_Ahuradim.png" - not a guess, confirmed present before use). Keyed the same way
// as INSTANCE_IMAGES: the literal boss name a real upload carries. An entry with no image here falls
// back to the instance photo via BOSS_IMAGES[name] ?? INSTANCE_IMAGES[instanceName] below.
export const BOSS_IMAGES = {
  "Wachhauptmann Rohuka": "/images/bosses/rohuka.jpg",
  "Chefkanonierin Kurmata": "/images/bosses/kurmata.jpg",
  "Dunkelverschlinger Derakanak": "/images/bosses/derakanak.jpg",
  "Stabschef Moriata": "/images/bosses/moriata.jpg",
  "Forscherin Teselik": "/images/bosses/teselik.jpg",
  "Versorgungskommandant Ranodim": "/images/bosses/ranodim.jpg",
  "Torwächter Slurt": "/images/bosses/stranir.jpg",
  "Inspektionsoffizier Obanuka": "/images/bosses/ovanuka.jpg",
  "Inspektionsoffizier Sayahum": "/images/bosses/sayahum.jpg",
  "Gardenführer Achradim": "/images/bosses/ahuradim.jpg",
  "Wartungsleiterin Notakiki": "/images/bosses/notakiki.jpg",
  "Brigade General Sheba": "/images/bosses/sheba.jpg",
  // From the user directly (a real screenshot, not the wiki - Raksha Boilheart has no page there).
  "Raksha Boilheart": "/images/bosses/raksha_boilheart.jpg",
  // aion.fandom.com has its own dedicated character page/render for each of these three.
  "Brigade General Vasharti": "/images/bosses/vasharti.jpg",
  Tiamat: "/images/bosses/tiamat.jpg",
  // Per the user's own report this boss is "Beritrakt", but no such name turns up anywhere on the
  // web - the instance's own wiki page names its endboss "Beritra" instead, and this render is
  // Beritra's. Filed under the DB's own "Beritrakt" key on the assumption they're the same NPC
  // (a plausible mishearing/typo in the original report), not a confirmed match - fix the key if
  // a real upload ever settles which name this server's Chat.log actually uses.
  Beritrakt: "/images/bosses/beritrakt.jpg",
};
