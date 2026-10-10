// Static game knowledge shared by the browser app (i18n.js, app.js) and the server (SEO shells,
// slug backfill) - one file so an instance's English name or boss portrait is never defined twice.
// Plain ESM data, no runtime dependencies. Keys are the literal (English) names as stored in the DB.

export const GAME_NAME_TRANSLATIONS = {
  // Aion 2 instances and bosses in the site's languages (de, fr, es, ru): read from the game client's own
  // text tables (EU client), see Backend/src/data/aion2/*.json _meta.localizedNames.
  "Krao Cave": { en: "Krao Cave", de: "Kraohöhle", fr: "Grotte de Krao", es: "Cueva de Krao", ru: "Пещера крао", pt: "Caverna de Krao", ja: "クラオ洞窟", ko: "크라오 동굴" },
  "Dying Dramata's Nest": { en: "Dying Dramata's Nest", de: "Nest des Toten Dramatha", fr: "Nid du Dramata Mort", es: "Nido del Dramata Muerto", ru: "Гнездо мертвой драматы", pt: "Ninho de Dramata Morto", ja: "死んだドラマタの巣", ko: "죽은 드라마타의 둥지" },
  "Fire Temple": { en: "Fire Temple", de: "Feuertempel", fr: "Temple Divin du Feu", es: "Templo de Fuego", ru: "Храм огня", pt: "Templo Divino do Fogo", ja: "炎の神殿", ko: "불의 신전" },
  "Draupnir": { en: "Draupnir", de: "Draupnir", fr: "Draupnir", es: "Draupnir", ru: "Драупнир", pt: "Draupnir", ja: "ドラウプニル", ko: "드라웁니르" },
  "Vakron Sky Island": { en: "Vakron Sky Island", de: "Vakrons Luftinsel", fr: "Île Céleste de Vakron", es: "Isla Aérea de Vakron", ru: "Парящий остров Вакрона", pt: "Ilha Aérea de Vakron", ja: "バクロンの空中島", ko: "바크론의 공중섬" },
  "Cradle of Nihility": { en: "Cradle of Nihility", de: "Wiege des Nichts", fr: "Berceau du Néant", es: "Cuna de la Nada", ru: "Колыбель пустоты", pt: "Berço do Vazio", ja: "武の揺りかご", ko: "무의 요람" },
  "Submerged Life Temple": { en: "Submerged Life Temple", de: "Versunkener Lebenstempel", fr: "Temple Divin de la Vie Englouti", es: "Templo Divino de Vida Hundido", ru: "Затонувший храм жизни", pt: "Templo Divino da Vida Submerso", ja: "沈んだ生命の神殿", ko: "가라앉은 생명의 신전" },
  "Shattered Arkanis": { en: "Shattered Arkanis", de: "Zersplitterter Arkanis", fr: "Arcanis Fragmenté", es: "Arkanis Fragmentado", ru: "Расколотый Арканис", pt: "Arcanis Despedaçada", ja: "砕けたアルカニス", ko: "조각난 아르카니스" },
  "Deus Research Base": { en: "Deus Research Base", de: "Forschungsbasis Deus", fr: "Base de Recherche Deus", es: "Base de Investigación Deus", ru: "Исследовательская база Деус", pt: "Base de Pesquisa Deus", ja: "デウス研究基地", ko: "데우스 연구기지" },
  "Abyssal Forge: Ludra": { en: "Abyssal Forge: Ludra", de: "Abgrundsveredelung: Ludra", fr: "Creuset de l'Abîme : Ludra", es: "Forja Abismal: Ludra", ru: "Закалка пропасти: Рудра", pt: "Reforja Abissal: Ludra", ja: "深淵の再錬：ルドラ", ko: "심연의 재련 : 루드라" },
  "Hall of Illusion": { en: "Hall of Illusion", de: "Illusionskorridor", fr: "Corridor d'Illusion", es: "Corredor de la Ilusión", ru: "Галерея иллюзий", pt: "Corredor da Ilusão", ja: "幻影の回廊", ko: "환영의 회랑" },
  "Corroded Decontamination Facility": { en: "Corroded Decontamination Facility", de: "Läuterungsstätte der Korrosion", fr: "Sanctuaire de Purification de l'Érosion", es: "Purificatorio de Erosión", ru: "Чистилище скверны", pt: "Posto de Purificação da Erosão", ja: "侵食の浄化所", ko: "침식의 정화소" },
  "Urugugu Canyon": { en: "Urugugu Canyon", de: "Uruguguschlucht", fr: "Canyon Urugugu", es: "Cañón de Urugugu", ru: "Ущелье ауругу", pt: "Desfiladeiro de Urugugu", ja: "ウルググ峡谷", ko: "우루구구 협곡" },
  "Ferocious Horn Den": { en: "Ferocious Horn Den", de: "Grimmhornhöhle", fr: "Grotte de la Corne Féroce", es: "Caverna del Cuerno Feroz", ru: "Логово Свирепого Рога", pt: "Gruta do Chifre Feroz", ja: "獰猛な角岩窟", ko: "사나운 뿔 암굴" },
  "Azure Breath Island": { en: "Azure Breath Island", de: "Insel des Blauatems", fr: "Île du Souffle Azuré", es: "Isla del Aliento Azul", ru: "Остров лазурного дыхания", pt: "Ilha do Sopro Azul", ja: "青い息の島", ko: "푸른 숨의 섬" },
  "Chalice of Muspel": { en: "Chalice of Muspel", de: "Kelch von Muspel", fr: "Calice de Muspel", es: "Cáliz de Muspel", ru: "Чаша Мусфеля", pt: "Cálice de Muspel", ja: "ムスフェルの聖杯", ko: "무스펠의 성배" },
  "Mirror of Scarlet Desire": { en: "Mirror of Scarlet Desire", de: "Roter Spiegel des Verlangens", fr: "Miroir de l'Affection Rouge", es: "Espejo de Deseo Rojo", ru: "Красное зеркало тяготения", pt: "Espelho do Afeto Escarlate", ja: "赤い恋心の鏡", ko: "붉은 연심의 거울" },
  "Nightmare Altar": { en: "Nightmare Altar", de: "Altar der Albträume", fr: "Autel des Cauchemars", es: "Altar de Pesadilla", ru: "Алтарь кошмара", pt: "Altar do Pesadelo", ja: "悪夢の祭壇", ko: "악몽의 제단" },
  "Tyrant's Hideout": { en: "Tyrant's Hideout", de: "Versteck des Tyrannen", fr: "Repaire du Tyran", es: "Escondite del Tirano", ru: "Убежище тирана", pt: "Esconderijo do Tirano", ja: "暴君の隠れ家", ko: "폭군의 은신처" },
  "Sanctum of Loathing": { en: "Sanctum of Loathing", de: "Sanktuarium des Hasses", fr: "Sanctuaire de la Haine", es: "Sagrario del Odio", ru: "Святилище ненависти", pt: "Sacrário do Ódio", ja: "憎悪の聖所", ko: "증오의 성소" },
  "Depository of Fates": { en: "Depository of Fates", de: "Kammer der Laufbahn", fr: "Dépôt des Trajectoires", es: "Depósito de Trayectoria", ru: "Хранилище траекторий", pt: "Depósito de Trajetórias", ja: "軌跡保管所", ko: "궤적 보관소" },
  "Forgotten Repository": { en: "Forgotten Repository", de: "Vergessener Speicher", fr: "Entrepôt Oublié", es: "Repositorio Olvidado", ru: "Забытый тайник", pt: "Repositório Esquecido", ja: "忘れられた保存庫", ko: "잊힌 저장소" },
  "Krao Cave (Conquest)": { en: "Krao Cave (Conquest)", de: "Kraohöhle (Eroberung)", fr: "Grotte de Krao (Conquête)", es: "Cueva de Krao (Conquista)", ru: "Пещера крао (Завоевание)" },
  "Urugugu Canyon (Conquest)": { en: "Urugugu Canyon (Conquest)", de: "Uruguguschlucht (Eroberung)", fr: "Canyon Urugugu (Conquête)", es: "Cañón de Urugugu (Conquista)", ru: "Ущелье ауругу (Завоевание)" },
  "Fire Temple (Conquest)": { en: "Fire Temple (Conquest)", de: "Feuertempel (Eroberung)", fr: "Temple Divin du Feu (Conquête)", es: "Templo de Fuego (Conquista)", ru: "Храм огня (Завоевание)" },
  "Draupnir (Conquest)": { en: "Draupnir (Conquest)", de: "Draupnir (Eroberung)", fr: "Draupnir (Conquête)", es: "Draupnir (Conquista)", ru: "Драупнир (Завоевание)" },
  "Vakron Sky Island (Conquest)": { en: "Vakron Sky Island (Conquest)", de: "Vakrons Luftinsel (Eroberung)", fr: "Île Céleste de Vakron (Conquête)", es: "Isla Aérea de Vakron (Conquista)", ru: "Парящий остров Вакрона (Завоевание)" },
  "Ferocious Horn Den (Conquest)": { en: "Ferocious Horn Den (Conquest)", de: "Grimmhornhöhle (Eroberung)", fr: "Grotte de la Corne Féroce (Conquête)", es: "Caverna del Cuerno Feroz (Conquista)", ru: "Логово Свирепого Рога (Завоевание)" },
  "Root Cellar": { en: "Root Cellar", de: "Wurzelspeicher", fr: "Réserve des Racines", es: "Bodega de la Raíz", ru: "Тайник корней", pt: "Repositório da Raiz", ja: "根の貯蔵庫", ko: "뿌리 저장고" },
  "Dranactus Underkeep": { en: "Dranactus Underkeep", de: "Verlies von Dranactus", fr: "Prison Souterraine de Dranactus", es: "Prisión Subterránea de Dranactus", ru: "Подземная тюрьма Дранактуса", pt: "Calabouço de Dranactus", ja: "ドラナクタスの地下牢獄", ko: "드라낙투스 지하감옥" },
  "Root of the Sacred Tree": { en: "Root of the Sacred Tree", de: "Wurzel des Heiligenbaums", fr: "Racines de l'Arbre Sacré", es: "Raíces del Árbol Sagrado", ru: "Корни священного дерева", pt: "Raiz da Árvore Sagrada", ja: "聖樹の根", ko: "성수의 뿌리" },
  "Crypt": { en: "Crypt", de: "Gruft", fr: "Crypte", es: "Cripta", ru: "Подземный склеп", pt: "Câmara de Pedra Subterrânea", ja: "地下の石室", ko: "지하 석실" },
  "Destruction Archon Underground Fortress": { en: "Destruction Archon Underground Fortress", de: "Untergrundfeste der Zerstörungsarchonen", fr: "Forteresse Souterraine de l'Archon de Destruction", es: "Fortaleza Subterránea de Akan de Destrucción", ru: "Подземная крепость аканов разрушения", pt: "Fortaleza Subterrânea do Arconte da Destruição", ja: "破壊のアルコンの地下要塞", ko: "파괴의 아칸 지하요새" },
  "Blades Hideout": { en: "Blades Hideout", de: "Versteck der Klingenlegion", fr: "Repaire du Clan des Lames", es: "Escondite de la Legión de la Cuchilla", ru: "Убежище клинков", pt: "Esconderijo das Lâminas", ja: "ブレイド団の隠れ家", ko: "칼날단 은신처" },
  "Impetusium": { en: "Impetusium", de: "Impetusium", fr: "Impetusium", es: "Impetusium", ru: "Имфетиус", pt: "Impetusium", ja: "インペトシウム", ko: "임페투시움" },
  "Gatekeeper Pinopi": { en: "Gatekeeper Pinopi", de: "Torwächter Pinopi", fr: "Pinopi le gardien des portes", es: "Portero Pinopi", ru: "Привратник пинопи", pt: "Porteiro Pinopi", ja: "門番ピノピ", ko: "문지기 피노피" },
  "Furious Feruk": { en: "Furious Feruk", de: "Rasender Feruk", fr: "Feruk furieux", es: "Feruk frenético", ru: "Бешеный Ферк", pt: "Feruk em Frenesi", ja: "狂暴なフェルク", ko: "광폭한 페르크" },
  "Fafnir's Poison Blood": { en: "Fafnir's Poison Blood", de: "Fafnirs Giftblut", fr: "Sang toxique de Fafnir", es: "Sangre venenosa de Fafnir", ru: "Ядокров Фафнира", pt: "Sangue Venenoso de Fafnir", ja: "ファフニールの毒血", ko: "파프니르의 독혈" },
  "Wraith Giselle": { en: "Wraith Giselle", de: "Geist Giselle", fr: "Giselle le spectre hostile", es: "Espectro Giselle", ru: "Призрак Жизели", pt: "Espectro Giselle", ja: "亡霊ジゼル", ko: "망령 지젤" },
  "Fortress Guardian Notun": { en: "Fortress Guardian Notun", de: "Festenhüter Notun", fr: "Notun le gardien de la Forteresse", es: "Protector de la fortaleza Notun", ru: "Хранитель крепости Нотун", pt: "Guardião da Fortaleza Notun", ja: "要塞の守護者ノトゥン", ko: "요새 수호자 노툰" },
  "Mutated Gerod": { en: "Mutated Gerod", de: "Mutierter Gerod", fr: "Gerod muté", es: "Gerod mutado", ru: "Мутировавший Герод", pt: "Gerod Aberrante", ja: "変異したゲロード", ko: "변이된 게로드" },
  "Zikel's Apparition": { en: "Zikel's Apparition", de: "Zikels Schemen", fr: "Apparition de Zikel", es: "Ectoplasma de Zikel", ru: "Фантом Джикела", pt: "Aparição de Zikel", ja: "ジケルの思念体", ko: "지켈의 사념체" },
  "Chamber of the Dead": { en: "Chamber of the Dead", de: "Schlafstätte der Toten", fr: "Chambre du Mort", es: "Alcoba de los Muertos", ru: "Опочивальня мертвых", pt: "Dormitório dos Mortos", ja: "死者の寝室", ko: "죽은 자의 침실" },
  "Citadel of the Fallen Daeva": { en: "Citadel of the Fallen Daeva", de: "Zitadelle der gefallenen Daeva", fr: "Citadelle du Daeva déchu", es: "Ciudadela del Daeva Caído", ru: "Цитадель павших даэвов", pt: "Cidadela do Daeva Caído", ja: "堕落したディーヴァの城", ko: "타락한 데바의 성" },
  "Abyssal Horn Den": { en: "Abyssal Horn Den", de: "Abyss-Hornhöhle", fr: "Antre de la Corne abyssale", es: "Guarida del Cuerno Abismal", ru: "Логово рога Бездны", pt: "Covil do Chifre do Abismo", ja: "深淵の角岩窟", ko: "심연의 뿔 암굴" },
  "Pure Spring": { en: "Pure Spring", de: "Quelle der Reinheit", fr: "Fontaine de Pureté", es: "Manantial de Pureza", ru: "Источник чистоты", pt: "Fonte da Pureza", ja: "純粋の泉", ko: "순수의 샘" },
  "Cave of the Stranded": { en: "Cave of the Stranded", de: "Höhle der Gestrandeten", fr: "Grotte des Naufragés", es: "Cueva de los Varados", ru: "Пещера заточенных", pt: "Caverna dos Abandonados", ja: "帰れぬ者の洞窟", ko: "돌아가지 못한 자의 동굴" },
  "Defunct Creion Laboratory": { en: "Defunct Creion Laboratory", de: "Aufgegebene Versuchsstätte der Creion", fr: "Laboratoire Creion désaffecté", es: "Laboratorio de Creion abandonado", ru: "Заброшенная лаборатория крейонов", pt: "Laboratório Creion Desativado", ja: "失敗したクレイオン実験室", ko: "실패한 크레이온 실험실" },
  "Fallen Haven": { en: "Fallen Haven", de: "Gefallene Zuflucht", fr: "Paradis perdu", es: "Refugio Caído", ru: "Павшее пристанище", pt: "Abrigo Caído", ja: "荒廃した安息所", ko: "스러진 안식처" },
  "Filthy Cave": { en: "Filthy Cave", de: "Schmutzige Höhle", fr: "Grotte souillée", es: "Cueva Mugrienta", ru: "Грязная пещера", pt: "Caverna Imunda", ja: "汚い洞窟", ko: "지저분한 동굴" },
  "Cave of the Exiled Ones": { en: "Cave of the Exiled Ones", de: "Höhle der Verbannten", fr: "Grotte des Exilés", es: "Cueva de los Exiliados", ru: "Пещера изгнанников", pt: "Caverna dos Exilados", ja: "追放された者たちの洞窟", ko: "쫓겨난 자들의 굴" },
  "Ruthilis of Pain": { en: "Ruthilis of Pain", de: "Ruthilis des Schmerzes", fr: "Ruthilis de la douleur", es: "Ruthilis del dolor", ru: "Руталис боли", pt: "Ruthilis da Dor", ja: "苦痛のルタリス", ko: "고통의 루탈리스" },
  "Siliator of Deceit": { en: "Siliator of Deceit", de: "Siliator der Täuschung", fr: "Siliator du mensonge", es: "Siliator de falsedad", ru: "Силиатор лжи", pt: "Siliator da Falsidade", ja: "偽りのシリアトール", ko: "거짓의 실리아토르" },
  "Talisra of the Void": { en: "Talisra of the Void", de: "Talisra der Leere", fr: "Talisra du Néant", es: "Talisra del vacío", ru: "Талира пустоты", pt: "Talisra do Vazio", ja: "空虚のターリスラ", ko: "공허의 탈리스라" },
  "Thamon": { en: "Thamon", de: "Thamon", fr: "Thamon", es: "Thamon", ru: "Тамон", pt: "Thamon", ja: "タモン", ko: "타몬" },
  "Tiere": { en: "Tiere", de: "Tière", fr: "Tiere", es: "Tiere", ru: "Тие", pt: "Tiere", ja: "ティエ", ko: "티에" },
  "Vakron": { en: "Vakron", de: "Vakron", fr: "Vakron", es: "Vakron", ru: "Вакрон", pt: "Vakron", ja: "バクロン", ko: "바크론" },
  "Awakened Alturadon": { en: "Awakened Alturadon", de: "Erwachter Althuradon", fr: "Alturadon éveillé", es: "Alturadon despierto", ru: "Пробудившийся Альтурадон", pt: "Alturadon Desperto", ja: "目覚めたアルタラドン", ko: "깨어난 알투라돈" },
  "Expanding Xyrepe": { en: "Expanding Xyrepe", de: "Expandierender Zairpe", fr: "Zairpe expansif", es: "Zairpe efusivo", ru: "Разбухший Заиреф", pt: "Xyrepe em Dilatação", ja: "膨張するザイルフェ", ko: "팽창하는 자이르페" },
  "Wise Baumnute": { en: "Wise Baumnute", de: "Weiser Baumnut", fr: "Baumnute le sage", es: "Baumnut prudente", ru: "Мудрый Ваумнут", pt: "Baumunt Prudente", ja: "賢明なバウムヌート", ko: "현명한 바움누트" },
  "Leader Korin": { en: "Leader Korin", de: "Anführer Korin", fr: "Chef Korin", es: "Korin líder", ru: "Вождь Корин", pt: "Líder Korin", ja: "リーダー コーリン", ko: "우두머리 코린" },
  "Eloen": { en: "Eloen", de: "Eloen", fr: "Eloen", es: "Eloen", ru: "Элойн", pt: "Eloen", ja: "エルロエン", ko: "엘로엔" },
  "Afflicted Bakarma": { en: "Afflicted Bakarma", de: "Gequälter Bakarma", fr: "Bakarma tourmenté", es: "Bakarma agonizante", ru: "Мучающийся Бакрам", pt: "Bakarma Agonizado", ja: "苦悩するヴォカルマ", ko: "고뇌하는 바카르마" },
  "Multiverse Veltiras": { en: "Multiverse Veltiras", de: "Veltiras der Multidimension", fr: "Veltiras multidimensionnelle", es: "Veltiras multidimensional", ru: "Белтирас иных измерений", pt: "Veltiras Multidimensional", ja: "多次元のベルティラス", ko: "다차원의 벨티라스" },
  "Solemn Valkan": { en: "Solemn Valkan", de: "Ehrwürdiger Valkan", fr: "Balkan solennel", es: "Valkan solemne", ru: "Суровый Валкан", pt: "Valkan Solene", ja: "厳粛なバルカン", ko: "엄숙한 발칸" },
  "Basilus the False": { en: "Basilus the False", de: "Basilus der Falsche", fr: "Basilus le fourbe", es: "Basilus, el Falso", ru: "Базилус Лживый", pt: "Basilus, o Falso", ja: "偽悪のバチルス", ko: "위악의 바실루스" },
  "Forbidden Hexbeast Griosa": { en: "Forbidden Hexbeast Griosa", de: "Verbotene Hexbestie Griosa", fr: "Griosa, la bête maudite", es: "Griosa, la Bestia Prohibida de Hex", ru: "Запретный зверь-чернокнижник Гриоса", pt: "Griosa, Hexafera Proibida", ja: "禁忌の魔獣グリオサ", ko: "금기의 마수 그리오사" },
  "Willful Turgen": { en: "Willful Turgen", de: "Willensstarker Turgen", fr: "Turgen l'Indomptable", es: "Turgen, el Obstinado", ru: "Своевольный Турген", pt: "Turgen Determinado", ja: "意志のトゥールゲン", ko: "의지의 투르겐" },
  "Broodkeeper Nayatman": { en: "Broodkeeper Nayatman", de: "Brutpflegerin Nayatman", fr: "Nayatman d'éclosion", es: "Incubador Nayatman", ru: "Наятман инкубации", pt: "Nayatman da Eclosão", ja: "孵化のナヤトマン", ko: "부화의 나야트만" },
  "Decaying Durvati": { en: "Decaying Durvati", de: "Korrumpierte Durvati", fr: "Durvati décomposée", es: "Durvati podrido", ru: "Гнилой Дурбати", pt: "Durvati Putrefata", ja: "腐敗したドゥルバティ", ko: "부패한 두르바티" },
  "Decaying Durvati (Specimen)": { en: "Decaying Durvati (Specimen)", de: "Korrumpierte Durvati (Exemplar)", fr: "Décomposé Durvati (spécimen)", es: "Durvati podrido (espécimen)", ru: "Гнилой Дурбати (образец)", pt: "Durvati Putrefata (Amostra)", ja: "腐敗したドゥルバティ(見本)", ko: "부패한 두르바티 (표본)" },
  "Durvati's Poison Fluid": { en: "Durvati's Poison Fluid", de: "Durvatis Gift", fr: "Venin de Durvati", es: "Ponzoña de Durvati", ru: "Ядовитая жидкость Дурбати", pt: "Veneno de Durvati", ja: "ドゥルバティの毒液", ko: "두르바티의 독액" },
  "Manager Nasium": { en: "Manager Nasium", de: "Verwalter Nasium", fr: "Administrateur Nasium", es: "Administrador Nasium", ru: "Управляющий Назиум", pt: "Gestor Nasium", ja: "管理者ナジウン", ko: "관리자 나지움" },
  "Mutated Malgerre": { en: "Mutated Malgerre", de: "Mutierter Malgerre", fr: "Malgerre altéré", es: "Malgerre mutado", ru: "Мутировавший Малгерре", pt: "Malgerre Aberrante", ja: "変異したマルゲル", ko: "변이된 말게르" },
  "Claudia": { en: "Claudia", de: "Claudia", fr: "Claudia", es: "Claudia", ru: "Клаудия", pt: "Claudia", ja: "クラウディア", ko: "클라우디아" },
  "Atiel": { en: "Atiel", de: "Atiel", fr: "Atiel", es: "Atiel", ru: "Атиэль", pt: "Atiel", ja: "アティエル", ko: "아티엘" },
  "Gelcos": { en: "Gelcos", de: "Gelcos", fr: "Gelcos", es: "Gelcos", ru: "Гелькос", pt: "Gelcos", ja: "ガルコス", ko: "겔코스" },
  "Nazmun": { en: "Nazmun", de: "Nazmun", fr: "Nazmun", es: "Nazmun", ru: "Назмун", pt: "Nazmun", ja: "ナズムン", ko: "나즈문" },
  "Phantasmal Lakshmi": { en: "Phantasmal Lakshmi", de: "Phantom-Lakshmi", fr: "Lakshmi onirique", es: "Lakshmi de ensueño", ru: "Лакшми видения", pt: "Lakshmi Onírica", ja: "夢幻のラクシュミ", ko: "몽환의 라크슈미" },
  "Predator Saraswati": { en: "Predator Saraswati", de: "Verschlinger Saraswati", fr: "Saraswati le Prédateur", es: "Saraswati depredador", ru: "Хищница Сарасвати", pt: "Predador Saraswati", ja: "捕食者サラスワティ", ko: "포식자 사라스와티" },
  "Transcendent Bakarma": { en: "Transcendent Bakarma", de: "Transzendierter Bakarma", fr: "Bakarma transcendé", es: "Bakarma trascendido", ru: "Возвышенный Бакрам", pt: "Bakarma Transcendido", ja: "超越したヴォカルマ", ko: "초월한 바카르마" },
  "Black Blood Blatt": { en: "Black Blood Blatt", de: "Schwarzblut-Blat", fr: "Blat au sang noir", es: "Blatt de sangre negra", ru: "Блат черной крови", pt: "Blat de Sangue Negro", ja: "黒血ブラット", ko: "검은 피 블라트" },
  "Devouring Klawfly": { en: "Devouring Klawfly", de: "Schwelgender Klawfly", fr: "Klawfly prédateur", es: "Klawra devorador", ru: "Пожирающий Ньютра", pt: "Neuthra Devorador", ja: "捕食するニュートラ", ko: "포식하는 뉴트라" },
  "Surveillance Device": { en: "Surveillance Device", de: "Überwachungsapparat", fr: "Dispositif de surveillance", es: "Dispositivo de vigilancia", ru: "Устройство наблюдения", pt: "Dispositivo de Vigilância", ja: "監視装置", ko: "감시 장치" },
  "Terminator Bargott": { en: "Terminator Bargott", de: "Polymerbargott", fr: "Bargott polymère", es: "Bargott polímero", ru: "Полимерный Багот", pt: "Bargott Polímero", ja: "重合体バゴット", ko: "중합체 바고트" },
  "Dhakan": { en: "Dhakan", de: "Dhakan", fr: "Dhakan", es: "Dhakan", ru: "Дхакан", pt: "Dhakan", ja: "ダカン", ko: "다칸" },
  "Gargaum": { en: "Gargaum", de: "Gargaum", fr: "Gargaum", es: "Gargaum", ru: "Гаргаум", pt: "Gargaum", ja: "ガルガウム", ko: "가르가움" },
  "Kapu": { en: "Kapu", de: "Kapu", fr: "Kapu", es: "Kapu", ru: "Капу", pt: "Kapu", ja: "カープ", ko: "카푸" },
  "Revenant Tifus": { en: "Revenant Tifus", de: "Rachegeist Tifus", fr: "Tifus revenant", es: "Aparición de Tifus", ru: "Мстительный дух Тифус", pt: "Espectro Retornado Tifus", ja: "怨魂ティプス", ko: "원혼 티푸스" },
  "Filthy Grollack": { en: "Filthy Grollack", de: "Schmutziger Grollack", fr: "Grollack le crasseux", es: "Grollack mugriento", ru: "Гроллак-грязнуля", pt: "Grollack Imundo", ja: "汚いグロラック", ko: "지저분한 그롤라크" },
  "Karlix": { en: "Karlix", de: "Karlix", fr: "Karlix", es: "Karlix", ru: "Карликс", pt: "Karlix", ja: "カリックス", ko: "칼릭스" },
  "Black Smoke Murute": { en: "Black Smoke Murute", de: "Schwarzrauch Murute", fr: "Murute de fumée noire", es: "Humo negro Murute", ru: "Мурт черного дыма", pt: "Murute da Fumaça Negra", ja: "黒煙モルト", ko: "검은연기 무르트" },
  "Kromede's Desire": { en: "Kromede's Desire", de: "Kromedes Gier", fr: "Désir de Kromede", es: "Ambición de Kromede", ru: "Желание Кромед", pt: "Desejo de Kromede", ja: "クロメデの欲望", ko: "크로메데의 욕망" },
  "Red Spark Ignus": { en: "Red Spark Ignus", de: "Rotflamme Ignus", fr: "Ignus de flamme rouge", es: "Fulgor rojo Ignus", ru: "Игнус красного языка пламени", pt: "Ignus da Chama Vermelha", ja: "赤い炎イグヌス", ko: "붉은불꽃 이그누스" },
  "Silver Blade Rotan": { en: "Silver Blade Rotan", de: "Silberklinge Rotan", fr: "Rotan à la lame d'argent", es: "Rotan de la cuchilla de plata", ru: "Серебряный рокан", pt: "Rotan da Lâmina Prateada", ja: "銀色の刃ロータン", ko: "은빛칼날 로탄" },
  "Enhanced Harcon": { en: "Enhanced Harcon", de: "Verstärkter Harcon", fr: "Harcon amélioré", es: "Harcon mejorado", ru: "Усиленный Харкон", pt: "Harcon Aprimorado", ja: "強化されたハルコン", ko: "강화된 하르콘" },
  "Neglected Gadioton": { en: "Neglected Gadioton", de: "Vergessener Gadioton", fr: "Gadioton délaissé", es: "Gadioton descuidado", ru: "Брошенный Гадиотон", pt: "Gadioton Abandonado", ja: "放置されたガディオトン", ko: "방치된 가디오톤" },
  "Ultimate Berk": { en: "Ultimate Berk", de: "Vollendete Berk", fr: "Berk accomplie", es: "Berk definitivo", ru: "Завершенная форма Берка", pt: "Berk em Forma Final", ja: "完成体ベルク", ko: "완성체 베르크" },
  "Ferocious Horn Nuakum": { en: "Ferocious Horn Nuakum", de: "Nuakum (Grimmhorn)", fr: "Nuakum à cornes féroces", es: "Nuakum del Cuerno Feroz", ru: "Нуакум свирепого рога", pt: "Nuakum do Chifre Feroz", ja: "獰猛な角ヌアクム", ko: "사나운 뿔 누아쿰" },
  "Necromancer Duanka": { en: "Necromancer Duanka", de: "Nekromantin Duanka", fr: "Duanka nécromancienne", es: "Duanka nigromante", ru: "Некромант Дванка", pt: "Necromante Duanka", ja: "降霊術士ドゥアンカ", ko: "강령술사 두안카" },
  "Shining Mau Totem": { en: "Shining Mau Totem", de: "Strahlendes Totem (Mau)", fr: "Totem Mau brillant", es: "Tótem de Mau brillante", ru: "Блестящий тотем оборотней", pt: "Totem Brilhante dos Licanos", ja: "光るライカン トーテム", ko: "빛나는 라이칸 토템" },
  "Watchdog Kwapo": { en: "Watchdog Kwapo", de: "Wachhund Kwapo", fr: "Kwapo le chien de garde", es: "Perro guardián Kwapo", ru: "Сторожевой пес Кухапу", pt: "Cão de Ataque Kwapo", ja: "番犬クハプ", ko: "번견 쿠하푸" },
  "Kromede's Desolation": { en: "Kromede's Desolation", de: "Kromedes Abgrund", fr: "Abîme de Kromede", es: "Abismo de Kromede", ru: "Пропасть Кромед", pt: "Desolação de Kromede", ja: "クロメデの深淵", ko: "크로메데의 심연" },
  "Ordeal of Love": { en: "Ordeal of Love", de: "Prüfung der Liebe", fr: "Épreuve de l'amour", es: "Prueba de amor", ru: "Испытание любви", pt: "Provação do Amor", ja: "愛の試練", ko: "사랑의 시련" },
  "Robstino": { en: "Robstino", de: "Robstino", fr: "Robstino", es: "Robstino", ru: "Робстино", pt: "Robstino", ja: "ロプスティノ", ko: "롭스티노" },
  "Rotar": { en: "Rotar", de: "Rotar", fr: "Lothar", es: "Rothar", ru: "Лотар", pt: "Lothar", ja: "ロータル", ko: "로타르" },
  "Blazing Guardian Sword": { en: "Blazing Guardian Sword", de: "Flammendes Hüterschwert", fr: "Épée du gardien flamboyante", es: "Espada guardiana llameante", ru: "Пылающий меч стража", pt: "Espada Guardiã Flamejante", ja: "炎火の守護剣", ko: "염화의 수호검" },
  "Iscariel": { en: "Iscariel", de: "Iscariel", fr: "Iscariel", es: "Iscariel", ru: "Искариэль", pt: "Iscariel", ja: "イスカリエル", ko: "이스카리엘" },
  "Kaldrix": { en: "Kaldrix", de: "Kaldrix", fr: "Kaldrix", es: "Kaldrix", ru: "Калдрикс", pt: "Kaldrix", ja: "カルドリックス", ko: "칼드릭스" },
  "Mutated Elite Chief": { en: "Mutated Elite Chief", de: "Mutierter Elitekommandant", fr: "Commandant d'élite mutant", es: "Jefe de élite mutado", ru: "Мутировавший элитный командир", pt: "Chefe de Elite Mutante", ja: "変異した精鋭指揮官", ko: "변이된 정예 지휘관" },
  "Divided Aponos": { en: "Divided Aponos", de: "Fragmentierter Aponos", fr: "Aponos entité fendue", es: "Aponos de fragmentación", ru: "Фрагмент Апноса", pt: "Entidade Fendida Aponos", ja: "分裂体アフォノス", ko: "분열체 아포노스" },
  "Glassvein": { en: "Glassvein", de: "Glassbane", fr: "Glassvein", es: "Glassvein", ru: "Глесбейн", pt: "Glassvein", ja: "グラスベイン", ko: "글래스베인" },
  "Nathara": { en: "Nathara", de: "Nathara", fr: "Nathara", es: "Nathara", ru: "Натхара", pt: "Nathara", ja: "ナトハラ", ko: "나트하라" },
  "Nazarak": { en: "Nazarak", de: "Nasarak", fr: "Nasarak", es: "Nasarak", ru: "Насарак", pt: "Nasarak", ja: "ナサラク", ko: "나사라크" },
  "Abyssal Wings Ketu": { en: "Abyssal Wings Ketu", de: "Abgrundsflügel Ketu", fr: "Ketu aux ailes d'Abîme", es: "Alas abismales Ketu", ru: "Крылья пропасти Кету", pt: "Asas Abissais Ketu", ja: "深淵の翼ケトゥ", ko: "심연의 날개 케투" },
  "Central Abyss Fusion Reactor": { en: "Central Abyss Fusion Reactor", de: "Zentraler Fusionsreaktor (Abgrund)", fr: "Réacteur à fusion central d'Abîme", es: "Reactor de fusión abismal central", ru: "Центральное ядро синтеза пропасти", pt: "Reator de Fusão Central Abissal", ja: "中央の深淵融合炉", ko: "중앙 심연 융합로" },
  "Eternal Ludra": { en: "Eternal Ludra", de: "Ludra der Ewigkeit", fr: "Ludra de perpétuité", es: "Ludra de la eternidad", ru: "Рудра веков", pt: "Ludra da Eternidade", ja: "永劫のルドラ", ko: "영겁의 루드라" },
  "Isolation Specialist Rahu": { en: "Isolation Specialist Rahu", de: "Rahu der Isolation", fr: "Rahu de la disjonction", es: "Rahu aislado", ru: "Раху разрыва", pt: "Rahu da Disjunção", ja: "離隔のラフ", ko: "이격의 라후" },
  "Featherstorm Duduri": { en: "Featherstorm Duduri", de: "Federsturmduduri", fr: "Duduri au barrage de plumes", es: "Duduri emplumado", ru: "Пернатый Дудури", pt: "Duduri da Chuva de Penas", ja: "羽根の砲火ドゥドゥリ", ko: "깃털포화 두두리" },
  "Watchful Raptor Dodori": { en: "Watchful Raptor Dodori", de: "Hellwachsamer Raubvogel Dodori", fr: "Dodori rapace aux yeux ouverts", es: "Dodori de corriente torrentosa despierto", ru: "Пробудившийся хищнокрыл Додори", pt: "Ave de Rapina Desperta Dodori", ja: "目を見開いた猛鳥ドドリ", ko: "눈 뜬 맹조 도도리" },
  "Water Spirit": { en: "Water Spirit", de: "Wassergeist", fr: "Esprit de l'eau", es: "Espíritu de agua", ru: "Дух воды", pt: "Espírito da Água", ja: "水の精霊", ko: "물의 정령" },
  "Lamiphedon": { en: "Lamiphedon", de: "Lamiphedon", fr: "Lamiphedon", es: "Lamiphedon", ru: "Ламифедон", pt: "Lamiphedon", ja: "ラミフェドン", ko: "라미페돈" },
  "Mau Boss Tshulaga": { en: "Mau Boss Tshulaga", de: "Anführer Tshulaga (Mau)", fr: "Shulag chef Mau", es: "Líder Mau Schlag", ru: "Лидер оборотней Шлаг", pt: "Shulag Chefe Licano", ja: "ライカン リーダー シュラグ", ko: "라이칸 우두머리 슐라그" },
  "Mau Sentry Soldier": { en: "Mau Sentry Soldier", de: "Wachsoldat (Mau)", fr: "Soldat de guet Mau", es: "Mau vigilante", ru: "Солдат-пограничник оборотней", pt: "Sentinela Licano", ja: "ライカン警戒兵士", ko: "라이칸 경계병사" },
  "Deep Sea Pydeon": { en: "Deep Sea Pydeon", de: "Pydeon der Tiefsee", fr: "Pydeon des profondeurs", es: "Pydeon del mar profundo", ru: "Пайдион пучины", pt: "Pydeon Abissal", ja: "深海のパイディオン", ko: "심해의 파이디온" },
  "Singing Ellyde": { en: "Singing Ellyde", de: "Singende Ellyde", fr: "Ellyde chantant", es: "Ellyde del canto", ru: "Поющий Элайд", pt: "Ellyde Cantante", ja: "歌うエレイド", ko: "노래하는 엘라이드" },
  "Submerged Emon": { en: "Submerged Emon", de: "Versunkener Emon", fr: "Emon englouti", es: "Emon hundido", ru: "Затонувший Эмон", pt: "Emone Submerso", ja: "沈んだエモン", ko: "가라앉은 에몬" },
  "Menoch": { en: "Menoch", de: "Menox", fr: "Menox", es: "Menoch", ru: "Менокс", pt: "Menoch", ja: "メノックス", ko: "메녹스" },
  "Divine Auldor": { en: "Divine Auldor", de: "Heiliger Auldor", fr: "Auldor sacré", es: "Auldor sagrado", ru: "Священный Аулдор", pt: "Auldor Divino", ja: "神聖なアウルドール", ko: "신성한 아울도르" },
  "Guardian Captain Raur": { en: "Guardian Captain Raur", de: "Hüterhauptmann Raur", fr: "Raur le Capitaine de la garde", es: "Capitán de la Guardia Raur", ru: "Капитан охраны Лаур", pt: "Capitão Guardião Raur", ja: "守護隊長ラウル", ko: "수호대장 라우르" },
  "Judge Urahum": { en: "Judge Urahum", de: "Richter Urahum", fr: "Juge Urahum", es: "Juez Urahum", ru: "Судья Урахум", pt: "Juiz Urahum", ja: "審判者ウラフム", ko: "심판자 우라훔" },
};

// Instance photos, keyed by the English instance name. An instance with no entry just renders
// without a photo (icon()'s own onerror-remove handles a bad path the same way) - never guessed.
// Sources: in-game UI screenshots from the Fextralife wiki (the Expedition browser, so some HUD chrome
// survives the poster card's center-crop) and the reveal-article hero images of NCSoft's newsroom
// (about.ncsoft.com), each checked against the article's own text before use.
export const INSTANCE_IMAGES = {
  "Krao Cave": "/images/aion2/instances/krao-cave.webp",
  Draupnir: "/images/aion2/instances/draupnir.webp",
  "Urugugu Canyon": "/images/aion2/instances/urugugu-canyon.webp",
  "Vakron Sky Island": "/images/aion2/instances/vakron-sky-island.webp",
  "Fire Temple": "/images/aion2/instances/fire-temple.webp",
  "Ferocious Horn Den": "/images/aion2/instances/ferocious-horn-den.webp",
  "Cradle of Nihility": "/images/aion2/instances/cradle-of-nihility.webp",
  "Mirror of Scarlet Desire": "/images/aion2/instances/mirror-of-scarlet-desire.webp",
  "Chalice of Muspel": "/images/aion2/instances/chalice-of-muspel.webp",
  "Corroded Decontamination Facility": "/images/aion2/instances/corroded-decontamination-facility.webp",
  "Submerged Life Temple": "/images/aion2/instances/submerged-life-temple.webp",
  "Citadel of the Fallen Daeva": "/images/aion2/instances/citadel-of-the-fallen-daeva.webp",
  "Abyssal Horn Den": "/images/aion2/instances/abyssal-horn-den.webp",
  "Krao Cave (Conquest)": "/images/aion2/instances/krao-cave.webp",
  "Urugugu Canyon (Conquest)": "/images/aion2/instances/urugugu-canyon.webp",
  "Fire Temple (Conquest)": "/images/aion2/instances/fire-temple.webp",
  "Draupnir (Conquest)": "/images/aion2/instances/draupnir.webp",
  "Vakron Sky Island (Conquest)": "/images/aion2/instances/vakron-sky-island.webp",
  "Ferocious Horn Den (Conquest)": "/images/aion2/instances/ferocious-horn-den.webp",
  "Root Cellar": "/images/aion2/bosses/pinopi.webp",
  "Dranactus Underkeep": "/images/aion2/bosses/feruk.webp",
  "Root of the Sacred Tree": "/images/aion2/bosses/fafnirs-giftblut.webp",
  "Crypt": "/images/aion2/bosses/geist-giselle.webp",
  "Destruction Archon Underground Fortress": "/images/aion2/bosses/festenhueter-notun.webp",
  "Blades Hideout": "/images/aion2/bosses/mutierter-gerod.webp",
  "Impetusium": "/images/aion2/bosses/zikels-schemen.webp",
  "Shattered Arkanis": "/images/aion2/instances/shattered-arkanis.webp",
  "Deus Research Base": "/images/aion2/instances/deus-research-base.webp",
  "Sanctum of Loathing": "/images/aion2/instances/sanctum-of-loathing.webp",
  // Ascension Rite instances (the game's dungeon shots, un-squeezed to 16:9):
  "Nightmare Altar": "/images/aion2/instances/nightmare-altar.webp",
  "Tyrant's Hideout": "/images/aion2/instances/tyrants-hideout.webp",
  "Depository of Fates": "/images/aion2/instances/depository-of-fates.webp",
  "Forgotten Repository": "/images/aion2/instances/forgotten-repository.webp",
  "Chamber of the Dead": "/images/aion2/instances/chamber-of-the-dead.webp",
  // Client backgrounds (UT_BG_*BG_*), un-squeezed from 1:1 to 16:9:
  "Consumed Deus Research Base": "/images/aion2/instances/consumed-deus-research-base.webp",
  "Azure Breath Island": "/images/aion2/instances/azure-breath-island.webp",
  "Dying Dramata's Nest": "/images/aion2/instances/dying-dramata-s-nest.webp",
  "Hall of Illusion": "/images/aion2/instances/hall-of-illusion.webp",
  "Abyssal Forge: Ludra": "/images/aion2/instances/abyssal-forge-ludra.webp",
  "Noiran's Hidden Legacy": "/images/aion2/instances/noiran-s-hidden-legacy.webp",
  // World boss areas: landscapes from the client (Agit paintings / Abyss background):
  Verteron: "/images/aion2/instances/verteron.webp",
  Altgard: "/images/aion2/instances/altgard.webp",
  Abyss: "/images/aion2/instances/abyss.webp",
};

// Vertical position (object-position y) of each instance photo in the wide hero banner (16:9 photo, ~1:5 window): puts the boss/subject into view. Default centred.
export const INSTANCE_FOCUS = {
  "Vakron Sky Island": "50% 55%",
  "Consumed Deus Research Base": "50% 44%",
  "Azure Breath Island": "50% 73%",
  "Cradle of Nihility": "50% 48%",
  "Citadel of the Fallen Daeva": "50% 49%",
  "Dying Dramata's Nest": "50% 51%",
  "Draupnir": "50% 41%",
  "Fire Temple": "50% 43%",
  "Ferocious Horn Den": "50% 58%",
  "Krao Cave": "50% 43%",
  "Hall of Illusion": "50% 46%",
  "Urugugu Canyon": "50% 40%",
  "Corroded Decontamination Facility": "50% 36%",
  "Chalice of Muspel": "50% 39%",
  "Abyssal Forge: Ludra": "50% 32%",
  "Shattered Arkanis": "50% 50%",
  "Mirror of Scarlet Desire": "50% 30%",
  "Deus Research Base": "50% 53%",
  "Abyssal Horn Den": "50% 54%",
  "Noiran's Hidden Legacy": "50% 41%",
  "Submerged Life Temple": "50% 45%",
  "Krao Cave (Conquest)": "50% 43%",
  "Urugugu Canyon (Conquest)": "50% 40%",
  "Fire Temple (Conquest)": "50% 43%",
  "Draupnir (Conquest)": "50% 41%",
  "Vakron Sky Island (Conquest)": "50% 55%",
  "Ferocious Horn Den (Conquest)": "50% 58%",
  Verteron: "50% 50%",
  Altgard: "50% 50%",
  Abyss: "50% 50%",
};

// Boss cards (720x400) for the expedition / transcendence / ascension bosses: the client's boss portrait on a blurred copy of the instance photo. Conquest runs use the same names.
export const BOSS_CARDS = {
  "Afflicted Bakarma": "/images/aion2/bosses/cards/afflicted-bakarma.webp",
  "Phantasmal Lakshmi": "/images/aion2/bosses/cards/phantasmal-lakshmi.webp",
  "Predator Saraswati": "/images/aion2/bosses/cards/predator-saraswati.webp",
  "Transcendent Bakarma": "/images/aion2/bosses/cards/transcendent-bakarma.webp",
  "Ferocious Horn Nuakum": "/images/aion2/bosses/cards/ferocious-horn-nuakum.webp",
  "Necromancer Duanka": "/images/aion2/bosses/cards/necromancer-duanka.webp",
  "Shining Mau Totem": "/images/aion2/bosses/cards/shining-mau-totem.webp",
  "Watchdog Kwapo": "/images/aion2/bosses/cards/watchdog-kwapo.webp",
  "Black Smoke Murute": "/images/aion2/bosses/cards/black-smoke-murute.webp",
  "Kromede's Desire": "/images/aion2/bosses/cards/kromede-s-desire.webp",
  "Red Spark Ignus": "/images/aion2/bosses/cards/red-spark-ignus.webp",
  "Silver Blade Rotan": "/images/aion2/bosses/cards/silver-blade-rotan.webp",
  "Enhanced Harcon": "/images/aion2/bosses/cards/enhanced-harcon.webp",
  "Neglected Gadioton": "/images/aion2/bosses/cards/neglected-gadioton.webp",
  "Ultimate Berk": "/images/aion2/bosses/cards/ultimate-berk.webp",
  "Divine Auldor": "/images/aion2/bosses/cards/divine-auldor.webp",
  "Guardian Captain Raur": "/images/aion2/bosses/cards/guardian-captain-raur.webp",
  "Judge Urahum": "/images/aion2/bosses/cards/judge-urahum.webp",
  "Thamon": "/images/aion2/bosses/cards/thamon.webp",
  "Tiere": "/images/aion2/bosses/cards/tiere.webp",
  "Vakron": "/images/aion2/bosses/cards/vakron.webp",
  "Atiel": "/images/aion2/bosses/cards/atiel.webp",
  "Gelcos": "/images/aion2/bosses/cards/gelcos.webp",
  "Nazmun": "/images/aion2/bosses/cards/nazmun.webp",
  "Ruthilis of Pain": "/images/aion2/bosses/cards/ruthilis-of-pain.webp",
  "Siliator of Deceit": "/images/aion2/bosses/cards/siliator-of-deceit.webp",
  "Talisra of the Void": "/images/aion2/bosses/cards/talisra-of-the-void.webp",
  "Featherstorm Duduri": "/images/aion2/bosses/cards/featherstorm-duduri.webp",
  "Watchful Raptor Dodori": "/images/aion2/bosses/cards/watchful-raptor-dodori.webp",
  "Divided Aponos": "/images/aion2/bosses/cards/divided-aponos.webp",
  "Menoch": "/images/aion2/bosses/cards/menoch.webp",
  "Claudia": "/images/aion2/bosses/cards/claudia.webp",
  "Karlix": "/images/aion2/bosses/cards/karlix.webp",
  "Eloen": "/images/aion2/bosses/cards/eloen.webp",
};

// Per-boss art (Nightmare bosses so far, cut from the in-game boss list); a boss without an entry falls back to its instance's photo.
export const BOSS_IMAGES = {
  "Gatekeeper Pinopi": "/images/aion2/bosses/pinopi.webp",
  "Furious Feruk": "/images/aion2/bosses/feruk.webp",
  "Fafnir's Poison Blood": "/images/aion2/bosses/fafnirs-giftblut.webp",
  "Wraith Giselle": "/images/aion2/bosses/geist-giselle.webp",
  "Fortress Guardian Notun": "/images/aion2/bosses/festenhueter-notun.webp",
  "Mutated Gerod": "/images/aion2/bosses/mutierter-gerod.webp",
  "Zikel's Apparition": "/images/aion2/bosses/zikels-schemen.webp",

  // World bosses (Verteron, Altgard, Abyss): the client's own Agit painting, whole figure on a blurred copy of itself (2400x340, boss centred at ~60% so the 64% object-position of cards and hero frames it).
  "Addicted Hardirun": "/images/aion2/bosses/mob-addshulack-05.webp",
  "Advisor Resana": "/images/aion2/bosses/mob-drakanelite-03-v01.webp",
  "Argo, the Spirit King": "/images/aion2/bosses/mob-eleking-01-v02.webp",
  "Berserker Vargor": "/images/aion2/bosses/mob-varg-03-v01.webp",
  "Black Tentacle Lawa": "/images/aion2/bosses/mob-nagaelite-05.webp",
  "Black Warrior Aed": "/images/aion2/bosses/mob-surawar-05-v02.webp",
  "Blood Warrior Lannar": "/images/aion2/bosses/mob-altknife-04-v02.webp",
  "Bloodfang Pnyn": "/images/aion2/bosses/mob-kalnifelite-01-v01.webp",
  "Blooming Korin": "/images/aion2/bosses/mob-coradon-01-v03.webp",
  "Blue Wave Kelpina": "/images/aion2/bosses/mob-waterele-04-v01.webp",
  "Bodyguard Teegant": "/images/aion2/bosses/mob-neuth-03.webp",
  "Centurion Demiros": "/images/aion2/bosses/mob-undead-01.webp",
  "Chaser Taulo": "/images/aion2/bosses/mob-owlelite-01-cv01.webp",
  "Dark Shadow Vishwada": "/images/aion2/bosses/mob-beritrad-03-v01.webp",
  "Deceiver Trid": "/images/aion2/bosses/mob-nornirvar-02-v02.webp",
  "Desecrator Newbold": "/images/aion2/bosses/mob-altknife-06.webp",
  "Divine Ansas": "/images/aion2/bosses/mob-guardit-03.webp",
  "Drakan Battalion Weapon Guruta": "/images/aion2/bosses/mob-beritrad-04-v01.webp",
  "Eternal Gartua": "/images/aion2/bosses/mob-gartua-01-v02.webp",
  "Executioner Barthien": "/images/aion2/bosses/mob-altknife-07.webp",
  "Executor Argo": "/images/aion2/bosses/mob-eleking-01-v02.webp",
  "Executor Kaira": "/images/aion2/bosses/mob-beritrad-01.webp",
  "Executor Tamasa": "/images/aion2/bosses/mob-radvima-01-cv02.webp",
  "Faithful Rajit": "/images/aion2/bosses/mob-dracowar-01-v03.webp",
  "Forest Warrior Aullamu": "/images/aion2/bosses/mob-owlelite-01-v01.webp",
  "Furious Saursus": "/images/aion2/bosses/mob-ursusselite-01-v01.webp",
  "Guardian Lord Nahma": "/images/aion2/bosses/mob-abboss-01.webp",
  "Harvest Manager Moshav": "/images/aion2/bosses/mob-drakanelite-03-v01.webp",
  "Heretic Layla": "/images/aion2/bosses/mob-surapriest-01-v04.webp",
  "High Commander Lagta": "/images/aion2/bosses/mob-lagta-01.webp",
  "High Overseer Nutah": "/images/aion2/bosses/mob-skurvwar-04-v03.webp",
  "Immortal Gartua": "/images/aion2/bosses/mob-gartua-01-v02.webp",
  "Kernon of the West": "/images/aion2/bosses/mob-keneir-01-v02.webp",
  "Kusan the Mad Gladiator": "/images/aion2/bosses/mob-krallelite-01-v01.webp",
  "Melted Danar": "/images/aion2/bosses/mob-dranavar-02-v01.webp",
  "Neikel of the East": "/images/aion2/bosses/mob-keneir-01-v03.webp",
  "Phantasm Kasia": "/images/aion2/bosses/mob-kasia-01.webp",
  "Predator Garsan": "/images/aion2/bosses/mob-ursusselite-01-cv01.webp",
  "Researcher Setram": "/images/aion2/bosses/mob-beritrad-03-v01.webp",
  "Ritualist Garshim": "/images/aion2/bosses/mob-krallelite-02-v01.webp",
  "Rotten Kutar": "/images/aion2/bosses/mob-bigman-01-v03.webp",
  "Scholar Aulla": "/images/aion2/bosses/mob-owlmagic-01-v01.webp",
  "Sentinel K'nash": "/images/aion2/bosses/mob-beritrad-04-v01.webp",
  "Sharp Shylak": "/images/aion2/bosses/mob-dracoelite-01-v01.webp",
  "Silent Dartan": "/images/aion2/bosses/mob-beritrad-01.webp",
  "Soul Ruler Kashapa": "/images/aion2/bosses/mob-kashapa-01.webp",
  "Special Operations Leader Linx": "/images/aion2/bosses/mob-fighterm-01-v03.webp",
  "Specter Archon Axios": "/images/aion2/bosses/mob-dstrarchone-01.webp",
  "Veteran Shujakan": "/images/aion2/bosses/mob-lycan-05.webp",
  "Visionary Karuka": "/images/aion2/bosses/mob-lycan-04.webp",
  "Watcher Kaira": "/images/aion2/bosses/mob-beritrad-01.webp",
};

// What the game's instance window shows next to an instance: players, item level and (Conquest) the star rating.
// Read from in-game screenshots (EU client, 2026-10). A missing entry simply shows nothing.
export const INSTANCE_FACTS = {
  "Krao Cave": { players: "1-5", itemLevel: 200 },
  "Urugugu Canyon": { players: "2-5", itemLevel: 300 },
  "Fire Temple": { players: "1-5", itemLevel: 500 },
  Draupnir: { players: "1-5", itemLevel: 700 },
  "Vakron Sky Island": { players: "2-5", itemLevel: 1400 },
  "Ferocious Horn Den": { players: "1-5", itemLevel: 2100 },
  "Krao Cave (Conquest)": { players: "1-5", itemLevel: 700, stars: 1 },
  "Urugugu Canyon (Conquest)": { players: "2-5", itemLevel: 1400, stars: 2 },
  "Fire Temple (Conquest)": { players: "1-5", itemLevel: 2100, stars: 3 },
  "Draupnir (Conquest)": { players: "1-5", itemLevel: 700, stars: 1 },
  "Vakron Sky Island (Conquest)": { players: "2-5", itemLevel: 1400, stars: 2 },
  "Ferocious Horn Den (Conquest)": { players: "1-5", itemLevel: 2100, stars: 3 },
  "Shattered Arkanis": { players: "2-5", itemLevel: 1600 },
  "Deus Research Base": { players: "2-5", itemLevel: 1600 },
  "Sanctum of Loathing": { itemLevel: 1000 },
};

// Minimum character level to enter, by English instance name - the "Entry requirements" of the game's own
// instance window (EU client list, 2026-10-02). Instances whose level is not known yet have no entry:
// no suffix and no change to their sort position, never a guessed number.
export const INSTANCE_MIN_LEVEL = {
  "Krao Cave": 20,
  "Urugugu Canyon": 28,
  "Fire Temple": 35,
  Draupnir: 45,
  "Ferocious Horn Den": 45,
  "Vakron Sky Island": 45,
  "Krao Cave (Conquest)": 45,
  "Urugugu Canyon (Conquest)": 45,
  "Fire Temple (Conquest)": 45,
  "Draupnir (Conquest)": 45,
  "Vakron Sky Island (Conquest)": 45,
  "Ferocious Horn Den (Conquest)": 45,
  "Root Cellar": 45,
  "Dranactus Underkeep": 45,
  "Root of the Sacred Tree": 45,
  "Crypt": 45,
  "Destruction Archon Underground Fortress": 45,
  "Blades Hideout": 45,
  "Impetusium": 45,
};

/** "Draupnir (Conquest)" -> { base: "Draupnir", stars: "★" } (star rating from INSTANCE_FACTS); null for every other name. */
export function splitConquest(name) {
  const m = /^(.*) \(Conquest\)$/.exec(name ?? "");
  const stars = m ? INSTANCE_FACTS[name]?.stars : undefined;
  return m && stars ? { base: m[1], stars: "★".repeat(stars) } : null;
}

// Cards and thumbnails use the 800 px copies (name-sm.webp) that sit next to the full-size instance and
// boss-banner photos; the wide heroes keep the full file. Other images have no small copy and stay as is.
const HAS_SMALL = /^\/images\/aion2\/(instances|bosses)\/(?!mob-)[^/]+\.webp$/;
export function smallPhoto(url) {
  return url && HAS_SMALL.test(url) && !url.endsWith("-sm.webp") && !url.includes("/cards/") && Object.values(INSTANCE_IMAGES).includes(url)
    ? url.replace(/\.webp$/, "-sm.webp")
    : url;
}

// Server names as the game's own text spells them in each language it ships (ServerName_<id>_desc in the
// client's L10NString, checked 2026-10-07). Only the names that differ from the English one are listed;
// the database and the uploads always carry the English name, the site shows the reader's language.
export const SERVER_NAMES = {
  "Siel": {
    "ru": "Сиэль"
  },
  "Nezekan": {
    "ru": "Неджакан"
  },
  "Vaizel": {
    "ru": "Байзел"
  },
  "Kaisinel": {
    "ru": "Кайсинель"
  },
  "Yustiel": {
    "ru": "Юстиэль"
  },
  "Ariel": {
    "ru": "Ариэль"
  },
  "Fregion": {
    "ru": "Фрегион"
  },
  "Meslamtaeda": {
    "ru": "Мирастад"
  },
  "Hithanya": {
    "ru": "Хитани"
  },
  "Nania": {
    "ru": "Нания"
  },
  "Tahavatha": {
    "fr": "Tahabata",
    "ru": "Тахабата"
  },
  "Luteros": {
    "ru": "Лутерс"
  },
  "Phernos": {
    "ru": "Фернос"
  },
  "Daminu": {
    "ru": "Дамину"
  },
  "Kasaka": {
    "fr": "Kasika",
    "ru": "Касака"
  },
  "Bakarma": {
    "ru": "Бакрама"
  },
  "Tsenka": {
    "de": "Hogalum",
    "ru": "Ченгарун"
  },
  "Kochi": {
    "de": "Kochilum",
    "ru": "Кочирун"
  },
  "Ishtar": {
    "ru": "Иштар"
  },
  "Tiamat": {
    "ru": "Тиамат"
  },
  "Gauss": {
    "ru": "Гаус"
  },
  "Lamuatan": {
    "ru": "Рамуатана"
  },
  "Israphel": {
    "ru": "Израфель"
  },
  "Zikel": {
    "ru": "Джикел"
  },
  "Triniel": {
    "ru": "Триниэль"
  },
  "Lumiel": {
    "ru": "Румиэль"
  },
  "Marchutan": {
    "ru": "Маркутан"
  },
  "Azphel": {
    "ru": "Асфель"
  },
  "Ereshkigal": {
    "ru": "Эрискаль"
  },
  "Beritra": {
    "ru": "Бритра"
  },
  "Nemon": {
    "ru": "Немон"
  },
  "Hadala": {
    "ru": "Хадала"
  },
  "Ludra": {
    "fr": "Eldra",
    "ru": "Рудра"
  },
  "Ulgorn": {
    "ru": "Ульгорн"
  },
  "Munin": {
    "ru": "Мунин"
  },
  "Odar": {
    "ru": "Одар"
  },
  "Zemurru": {
    "de": "Zenqaka",
    "ru": "Земурру"
  },
  "Kromede": {
    "ru": "Кромед"
  },
  "Quai": {
    "fr": "Nai",
    "ru": "Квайринг"
  },
  "Baba": {
    "de": "Babalum",
    "ru": "Бабарунг"
  },
  "Fafnir": {
    "ru": "Фафнир"
  },
  "Indnath": {
    "ru": "Инднах"
  },
  "Agnita": {
    "ru": "Агнита"
  },
  "Atiel": {
    "ru": "Атиэль"
  }
};
