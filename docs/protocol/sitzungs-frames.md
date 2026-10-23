# Login-, Zonen- und Sitzungsframes (Auswertung vom 23.10.2026)

> Von einem Auswerte-Agenten erstellt (185 Login-Dumps, 31 Debug-Aufnahmen, zwei Aufnahmen vom 23.10.2026), Stichprobe vom Hauptentwickler nachgeprüft (`0x2036` enthält entpackt ein UTF-16-JSON mit den Charakter-Einstellungen). "[B]" = belegt, "[H]" = Hypothese, "[?]" = unbekannt. Enthält keine Zufallsbytes, Token oder IDs. Ergänzt [login-und-fenster-frames.md](login-und-fenster-frames.md).


Grundlage: 185 Login-Dumps (`all.pkl`, alle Frames je Dump), die zwei Aufnahmen cur5/cur7 vom 23.10. und die 31 Debug-Aufnahmen 21.-23.10. (nur die hier betrachteten Opcodes herausgezogen, 2.146 Frames). Alle Zahlen little-endian, Opcode = erste zwei Frame-Bytes (als Hex der Bytes). `varint` = LEB128. Einstufung: [B] belegt (Statistik/Gegenprobe), [H] Hypothese, [?] unbekannt. Keine Charakter-IDs, Namen, Token, GUIDs oder Zufallsbytes im Text (nur Längen).

## 0. Reihenfolge im Login und bei Zonenwechsel [B]

Belegt an cur5/cur7 (4 Logins, 3 Zonenwechsel) und den Debug-Aufnahmen:

Neuer Login nach Neuverbindung (Charakterwechsel, Relog):
`1136` (nur bei neuer TCP-Verbindung) -> `1536` -> `2036` -> `5ae2` -> `538d` -> `62e2` -> `43e3` -> `2136` (Zonenframe) -> **rund 5 s später** (Ladebildschirm) `2336` -> `0140` -> `0240` -> `0143` -> `a856`.

Zonenwechsel innerhalb einer Verbindung: `2136` -> ~1 s -> `2336`/`0140`/`0240`/`0143` (ohne `1136`, `2036`, `5ae2`, `538d`, `43e3`, `a856`).

Zählungen in den Debug-Aufnahmen (31 Dateien): 43e3 = 106, 2036 = 106, 5ae2 = 106, 538d = 106 (also genau einmal je Login), a856 = 94 (je Login, ein paar fehlen, weil sie erst 5-10 s nach dem Login kommen und die Aufnahme dann schon beendet war), 1136 = 64, 2136 = 263, 0140 = 281, 0240 = 265, 2336 = 265, 0143 = 245 (also je Zoneneintritt), 62e2 = 170 (106 im Login + 64 mitten in der Sitzung), 61e2 = 31.

Die Frames 0140/0240/2336/0143 gehören also zum "Zone fertig geladen"-Block, nicht zum Login-Block. Die Verzögerung zum Zonenframe 2136 ist die Ladezeit (Login ~5 s, Zonenwechsel ~1 s).

---

## 1. `0x2036` Client-Einstellungen des Charakters (Tastenbelegung u. a.) [B]

**Nur beim Login (262/262 Frames, 106/106 in den Debug-Aufnahmen), je Charakter verschieden.** 1.247-2.676 Byte.

### Aufbau [B] (Kopf), [H] (Felder X und Trailer)

```
2036 | u16 0 | varint Eigen-Entität (1-2 Byte, ändert sich je Login) | u32 0 | u64 Charakter-DBID (u32 ID, u16 0, u16 Server 1304) |
u32 X | u32 Länge entpackt | zlib-Strom (78 9c ...) | 361 Byte Trailer
```

- Die DBID im Kopf ist dieselbe wie im `43e3` des Logins (Server steht in den obersten 16 Bit: 0x0518 = 1304 Kaisinel).
- "Länge entpackt" stimmt in 221 von 223 verschiedenen Logins exakt mit der Länge des entpackten Puffers überein (die zwei Ausnahmen sind die Frames mit 1-Byte-Entitäts-varint, bei denen meine Offset-Rechnung verrutschte, nicht der Frame) [B].
- `X` (u32): obere 16 Bit je Charakter stabil und ändern sich, wenn sich der Inhalt ändert (z. B. 4482 -> 4488 beim selben Charakter, als sich die Einstellungen änderten; 4347 an anderem Tag) -> sieht nach einer 16-Bit-Prüfsumme/Versionsnummer des Inhalts aus; untere 16 Bit ändern sich bei **jedem** Login bei gleichem Inhalt (Nonce/Zähler) [H].
- Der zlib-Strom endet genau nach dem JSON; danach folgen **immer exakt 361 Byte** (konstante Länge, 262/262) = "Trailer" [B].

### Inhalt nach dem Entpacken [B]

UTF-16LE-Text, ein JSON-Objekt (eingerückt mit Tabs, `\r\n`), nach der schließenden Klammer ein NUL. Beispiel Länge 13.388 Byte = 6.693 Zeichen. Es sind **die serverseitig gespeicherten UI-Einstellungen des Charakters**. 89 verschiedene Schlüssel insgesamt; **dünn besetzt**: nur Werte, die der Spieler je geändert hat, stehen drin (Pflichtschlüssel in allen 262 Frames: `Version`=1, `KeySetting`, `WorldMapFilterSetting`, `MiniMapFilterSetting`, `QuickSlotMacroPreset`, `JournalSelectedQuest`, `bShowCombatPower` u. a.; die übrigen kommen in 14-253 Frames vor).

**`KeySetting`** (die Tastenbelegung) ist ein JSON-String im JSON (doppelt kodiert): `{"<Aktion>": [[Tasten Primär], [Tasten Sekundär]], ...}`. Jede Liste ist eine Kombination aus Modifikator(en) + Taste, Namen sind Unreal-Engine-Tastennamen (`LeftAlt`, `LeftShift`, `LeftMouseButton`, `RightMouseButton`, `F1`..`F10`, `Add`, `Subtract`, `Multiply`, `Apostrophe`, `Tilde`, `Q`, `E`, ...). Es stehen **nur geänderte Belegungen** drin (1 bis 53 Einträge je Charakter; Form (2, 0, 0) = beide leer, d. h. bewusst auf "nicht belegt" gesetzt: 1.050 Fälle; (2,1,0) 685; (2,2,0) 355 für Kombination; (2,1,1) 47 mit beiden Belegungen) [H zur Deutung "nur geändert", B zur Struktur].

Aktions-Namen (alle 82 gesehenen, gruppiert):
- Bewegung: `Move_AutoForward`, `Move_Forward`, `Move_Backward`, `Move_Left`, `Move_Right`, `Move_FlyUp`, `Move_FlyDown`
- Kampf/Ziele: `Combat_HostilePcTarget`, `Combat_PartyAssist`, `Combat_Potion`, `Combat_SubTarget_Party_1`, `DamageAnalyzer`, `Macro`, `SelectTargetMarker1..14` (1,2,3,4,5,6,7,8,9,10,14 gesehen), `BindTargetMarker7..9`
- Schnellleisten: `QuickSlot_Skill_1/2/7/8`, `QuickSlot_Stigma_1..4`, `QuickSlot_Item_1..8`, `PolymorphSkillSlot_1/2`
- Presets: `SkillPreset1..3`, `EquipPreset1..3`, `EquipAllPreset1..3`
- Kamera: `Camera_ZoomIn`, `Camera_ZoomOut`
- Questzeiger: `QuestIndicator_AutoMove_1..9`
- Fenster: `UI_Achievement`, `UI_Arcana`, `UI_BMShop`, `UI_Conversion`, `UI_CraftSkill`, `UI_Daevanion`, `UI_Enchant`, `UI_GatherSkill`, `UI_HudToggle`, `UI_PeriodCollection` (Versorgungsgesuche), `UI_Skin`, `UI_Title`, `UI_TransparentMap`, `UI_VehicleSoul`, `UI_Wing`, `HudEditUndo/Redo`
- Hinweis: `SkillPreset1..3`/`EquipPreset1..3` als belegbare Tasten bestätigen, dass es je drei Skill- und Ausrüstungs-Presets gibt.

**Weitere Schlüssel** (nur Namen/Typ, kein Personenbezug):
- Ziel-/Steuerung: `AimFilterType_Mode1` ("EnemyFilter"), `bEnableSkillFollow_Mode1`, `AutoSkillTargetingType_Mode1` ("IgnoreLongPress"/"Off"), `TargetSearchCriteria` ("Camera"/"None"; passt zur Zielwahl Kamera-vs-Tab aus den Notizen), `TargetSearchRule` (Liste `{TargetType: HostilePC/AttackingPC/SkillTarget/..., bEnable}`), `bTargetSearchEnableOption`, `bSetMainTargetByAttack_Mode1`, `bSetMainTargetByLeftClick_Mode1`, `bAimQuickSlotControl_Mode1`, `AimHeight_Mode1`
- Kamera: `PlayerCameraMode`, `CameraSpeedYaw`, `CameraSpeedPitch`, `CameraDistance`, `bCameraAdjustAutoMoveFree`, `bEnableBattleCameraShake`, `bTeleportFadeDark`
- Karten: `WorldMapFilterSetting` (34 Schalter: Battlefield, Village, HeroQuest, Dungeon, TeleportArtifact, Shop, ... Monolith, Boss, Rift, AbyssArtifact, NPCTeleport), `MiniMapFilterSetting` (16 Schalter), `TransparentMap*` (Anzeige, Sperre, Alpha-Werte, Filter)
- Beute/Items: `LootEquipItemGradeFilterFlags`, `LootItemFilterFlags` (255 = alles), `ItemGradeLootType`, `ItemAutoRegisterEachGrades/Settings/Usages`, `bUseAutoDecomposeItem`, `bAutoAnalyzeUseSoulCrystalAll`, `bShowAllAutoUsePotionItemList`
- Herstellung/Lager: `CraftShowRegisteredOnlyFlags`, `bConversionShowRegisteredOnlyFlags`, `bConversionAutoMultiSelectContainWarningFlags`, `bOpenCharacterStorage`, `bCharStorageIncludeStoredItem`, `bStorageIncludeStoredItem`
- Anzeigen: `bShowKeyGuide_Run/Fly/Glide/Dive/Windpath/Racing/Fall/Jumpjump`, `bShowCombatPower`, `bShowServerTime`, `bShowPet`, `bShowPet_OtherPet`, `bShowDamageFloater`, `bSummonNamePlate`, `bUseNetworkStateDisplay`, `AggroRangeOpacityPercent`, `DirectionMarkerDisplayType` ("Front"), `AlarmDamagedSFX`, `AlarmLowHPPercent`, `AlarmLowHPSFX`
- Chat: `bChatAutoTranslation`, `bChatTimeStamp`, `ChatTabSetting_Common`/`_System` (Felder `Version`, `ChatType`, `TabName`, `TabIconKey`, `ChatTypeFilters` als Bitmaske, `CanModifyFiltes`, `EnableSendChatTypes`, `bCanModifyName`, `bCanModifyIcon`; TabName ist der lokalisierte Name, vom Spieler änderbar)
- Inhalte/Saisons: `LatestSeasonBossChallenge`, `LatestSeasonArenaSingle`, `LatestSeasonArenaTeam`, `LatestSeasonPartyChallenge`, `LatestSeasonAbyss`, `LatestSeasonAwaken` (alle 100001 = Season_Main_Live Nr. 1; passen zur Tabelle `SeasonSchedule` ID 100001), `LastAwakenDifficulty`, `LastDailyDungeonSweepCount`, `CheckCompleteBossChallengeList`, `SelectedCategoryName`, `SelectBossChallenge`
- Sonstiges: `JournalSelectedQuest` (Liste `{QuestType District/DutyMission/..., Journal, QuestUID, QuestId}` = im Quest-Tracker gepinnte Quests, die QuestId ist aus der Tabelle `Quest`), `QuickSlotMacroPreset` ({"1": {MacroList: [{QuickSlotId, MacroDelayMS}]}, "2": ...} = Makro-Verzögerung je Schnellleisten-Platz), `PresetSetNum`, `PresetSettings` (Liste von `PresetList` mit 7 Zahlen = gewählter Preset je Fenster/Bereich [?]), `PCSocialActionBookmarkList`, `SkinDecoColorPresets`, `BMShopPurchasingCheckedGoodsMap`, `DaevanionSelectedTabIndex`, `bUseQuickSlotEventSkill_1/_2`, `bUseShockReleaseHotKey`, `bUseSprintWhenInteractionMove`.

### Trailer (361 Byte) [H]
Fest 361 Byte, je Charakter ein eigener Wert (13 verschiedene Trailer bei 13 Charakteren), beginnt immer mit `00 de 02 0c` (13/13), 92 von 361 Positionen sind bei allen konstant. Byte-Verteilung: 24 % Null, 66 % Werte <= 100; häufigste Werte 0, 100, 101, 255, 200, 132, 110. Das sieht nach einem Feld von Optionswerten (Schieberegler 0-100/200, Schalter, 0xff = "aus/leer") aus, vermutlich Grafik/Ton/HUD-Layout. **Nicht entschlüsselt** [?].

Nutzen: Die Tastenbelegung (`KeySetting`) ließe sich auslesen (Hotkeys des Spielers erkennen/Konflikte mit Tool-Hotkeys vermeiden), ebenso `bShowCombatPower`, `TargetSearchCriteria`, gepinnte Quests.

---

## 2. `0x5ae2` "Session-Token" [B Struktur, H Bedeutung]

218 Byte, 262/262, je Login einmalig. **Aufbau ist ein FlatBuffers-Puffer** [B]:

```
5ae2 | u16 0 | varint 212 (= Länge des Puffers) | Puffer(212 Byte):
  u32 Wurzel-Offset 20  ->  Tabelle bei Byte 26
  vtable (14 Byte): 0e00 1800 0400 0800 0c00 1000 1400  = 5 Felder, Tabellengröße 24
  Tabelle: soffset 14, Feld0 u32 (Zufall), Feld1 u32 (Zufall), Feld2 u32 = 144 (Offset -> Byte-Vektor der Länge 32),
           Feld3 u32 = 120 (Skalar? Offset 42+120 trifft keinen Vektoranfang), Feld4 u32 = 4 (Offset -> Byte-Vektor der Länge 128)
  Byte-Vektor Länge 128 (u32 80 00 00 00 + 128 Byte), danach Byte-Vektor Länge 32 (u32 20 00 00 00 + 32 Byte)
```
Beleg: 52 Positionen konstant (Kopf, vtable, Feld2-4, die beiden Längen), alle anderen Positionen (8 + 128 + 32 = 168 Byte) haben Entropie ~7,9 Bit/Byte, d. h. echte Zufallsdaten. Die Vektoren 128 B + 32 B und die zwei u32-Zufallswerte sehen nach Schlüsselmaterial (z. B. Chiffretext 1024 Bit + Hash/Schlüssel 256 Bit) oder Sitzungstoken aus [H]. Inhalt nicht ausgegeben. 223 verschiedene Frames bei 262 (die Doppelten sind überlappende Dumps).

## 3. `0x1136` Verbindungsaufbau [B Aufbau, H Bedeutung]

278 Byte, 57-mal, **steht nur am Anfang einer neuen TCP-Verbindung** (davor `1636` Server-Adresse, danach `1536` Login-Token); in Debug-Aufnahmen 64-mal, nie ohne Neuverbindung.

```
1136 | u16 0 | u32 60000 | varint 256 | 256 Byte (Zufall, Entropie 7,99) | 8 Byte 0 | i32 -7
```
- Bytes 0-9 und 266-277 sind in allen 57 Frames identisch [B]; 256 Byte dazwischen immer verschieden (55 verschiedene Frames bei 57).
- `60000` (0xea60) = vermutlich Zeitüberschreitung/Herzschlag in ms [H]; `-7` unbekannt [?].
- Der 256-Byte-Block (2048 Bit) ist ein Schlüsselblock (öffentlicher Schlüssel/Zufallswert für die Verschlüsselung der Client->Server-Richtung, die im Strom unlesbar ist) [H]. Passt zur Beobachtung, dass der Client-Verkehr verschlüsselt ist.

---

## 4. `0x43e3` Anmeldedatensatz des Charakters [B Aufbau]

66 Byte, 239/239 (genau einmal je Login), 50 ms vor dem Zonenframe.

```
43e3 | u8 n = 3 | n x 21 Byte:
   u64 Charakter-DBID  (= u32 Char-ID | u16 0 | u16 Server-ID, hier 0x0518 = 1304)
   u8  Index (1, 2, 3)
   u32 0
   u64 Zeit (ms seit 1970, UTC)
```
- Alle 239 Frames: n=3, die drei DBIDs sind **identisch**, Indizes immer 1,2,3, das u32 immer 0, die drei Zeiten **identisch** [B]. Das Serverbyte (obere 16 Bit der DBID) ist immer 1304 in diesen Mitschnitten.
- Die Zeit ist die Serverzeit des Frames: sie liegt 57 ms hinter `a5ff` und 157 ms hinter dem Zeit-Feld 1 von `62e2` (konstant, 26 Logins `-57/-157` usw.) und im Mittel 0,9 s neben dem Mitschnitt-Zeitstempel (±3 s Uhrenversatz) = "jetzt" [B].
- Bedeutung der drei Indizes: unbekannt. Alle drei Einträge sind inhaltsgleich bis auf den Index, daher eher drei Teilsysteme (Chat/Gruppe/...) die sich beim Anmelden mit dem Charakter registrieren, oder 3 Kanäle [?]. Der Frame steht direkt hinter `42e3` (Frame mit Zeitstempel-Paaren, 3-288 Byte, Aufbau `u8 n | n x (...)`, vermutlich Effekt-/Buff-Liste mit Start/Ende-Zeit [H], nicht Teil dieser Aufgabe).
- Nutzen: eindeutiger Login-Marker + Charakter-DBID (so schon verwendet).

---

## 5. `0x2136` -> `0x2336`, `0x0140`, `0x0240` (Zone-Fertig-Block)

### `0x2336` eigene Position nach dem Laden [B]
25-26 Byte (303 x 26, 2 x 25), 305 Frames:
```
2336 | 00 00 00 00 00 00 | f32 x | f32 y | f32 z | u32 U (1..5) | varint V
```
- **x, y, z sind die eigene Weltposition** und stimmen in 288 von 305 Fällen **exakt** mit x/y/z des zuletzt gesehenen Zonenframes `0x2136` überein (Bytes 18/22/26 dort). In den 17 Abweichungen (ohne Zonenframe im Dump oder mit Ruhezeit) hat der Spieler sich während des Ladens bewegt (2336 hat dann Bruchteile, 2136 ganze Zahlen) -> 2336 = **Position zum Zeitpunkt "Laden beendet"** [B]. Damit liefert der Frame nach jedem Zoneneintritt die aktuelle Position (nützlich, wenn 2136 fehlt).
- `U` (1..5) und `V` (varint, 1-2 Byte, bis 16.155): wechseln bei **jedem** Zoneneintritt zufällig (297 Frames), gleiche Position -> andere Werte bei anderem Eintritt; V steht in keiner Client-Tabelle (42 % Zufallstreffer in AdditionalStat/ActInfo wegen Wertebereich), nicht gleich der eigenen Entitäts-ID, keine Korrelation mit Zone, Zeit oder den Feldern von 2136. Kanal/Schicht (1-5) und Instanz-/Raum-Nummer wären denkbar, sind aber nicht belegt (auch in Dungeons tritt U in 1..5 auf) [?].
- Das Trio 0140/0240/2336 hat im selben Millisekundenschlitz denselben Zeitstempel.

### `0x0140` Zustand der Zone [B Aufbau, H Bedeutung]
```
17 Byte (Normalfall, 243 von 281 in Debug): 0140 | u32 Zonen-ID | 00 | u64 0 | 01 | 00
26 Byte (Dungeons, 38 Frames, nur Zonen 600012/600072/600092 = Urugugu/Vakron/Grimmhorn mittel/schwer):
        0140 | u32 Zonen-ID | u8 hatZeit | u64 Zeit (ms, 0 wenn hatZeit=0) | u8 Zustand (1,2,3) | 01 | u8 Art (01/02) | u64 Konstante je Dungeon
```
- Die Zonen-ID ist in 297 von 305 Fällen gleich der Zonen-ID im letzten `2136` (8 Ausnahmen = kein 2136 im Dump) [B].
- Im Normalfall ist der Rest konstant (alle 305 Frames: `00..00 01 00`).
- **Dungeon-Zustand (26 Byte), [B] Beobachtung / [H] Deutung:** Zustand 1 = Zeit 0 (Vorraum, 13 Frames); Zustand 2 = Zeit = **Frame-Zeit + 2 bis 3 s** (18 von 18 Fällen: Delta -2/-3 s, einmal 0) = Start des Laufs nach dem 3-Sekunden-Countdown; Zustand 3 = dieselbe Startzeit wie davor, Frame kommt 37-182 s später (und beim erneuten Eintritt wiederholt) = Lauf beendet [H]. Die 8-Byte-Konstante ist je Dungeon-Zone fest (z. B. 600092 immer derselbe Wert) und bei verschiedenen Läufen identisch -> Kennung des Dungeons, nicht des Laufs [B, Deutung H]. Nutzen: exakte **Startzeit eines Dungeonlaufs** (Zustand 2) und Ende-Markierung (Zustand 3).

### `0x0240` Zonen-Zusatzzustand [B Aufbau, H Bedeutung]
```
8 Byte (304 von 307): 0240 | u32 Zonen-ID | 00 | 01
21 Byte (3 Frames): 0240 | u32 Zonen-ID | 01 | u8 Art | u32 Wert | u64 Zeit | u8 Flag
```
- Zonen-ID wie bei 0140. 8-Byte-Form konstant (265/265 in Debug).
- 21-Byte-Form: nur 3 Frames in alten Dumps (Zone 600031 = Draupnir leicht zweimal mit Art 02, Wert 2 und 4, Zeit = Frame-Zeit + 3,03 s, Flag 0, einmal mitten im Lauf nach Kampfeintritt; Zone 500033 = Geisterangriff-Invasion mit Art 0x33, Wert 1, Zeit = Frame-Zeit - 1,5 s, Flag 1). Das Muster "Zeit = jetzt + 3 s" entspricht dem Countdown von `0140`-Zustand 2. Bedeutung von Art/Wert [?]. In den Debug-Aufnahmen 21.-23.10. kam die lange Form nicht mehr vor (nur 8 Byte).

---

## 6. `0x0143` / `0x0243` / `0x0343` Luftraumschlacht (Wochenereignis) [B Aufbau, B Zuordnung]

**Zuordnung belegt über die Client-Tabellen `AirspaceBattle` und `Airspace`:** `AirspaceBattle` ID 1 "Airspace_Light_1": `Race Light` (Elyos), `ReadyDuration 600` s, `BattleDuration 600` s, `Days = Wednesday`. `Airspace` hat **5 Einträge** (ID 1-5, alle Karte 1010 Verteron, Kosten 1.000, ab Stufe 5; Zonen: Basis der Morgenröte-Legion, Cantas-Tal, Ellun-Marsch, Zumion-Dorf, Altamia-Schlucht). Der Frame trägt genau diese 5 Einträge und die Zeiten Mittwoch 17:00 Ortszeit (15:00 UTC) -> **Luftraumschlacht (Airspace Battle) der Elyos**; für Asmodier gibt es in den Tabellen noch keinen Eintrag.

Frame `0143` ist der Zustand beim Zoneneintritt, `0243` die Änderungsmeldung des Servers, `0343` das Ende je Luftraum.

```
0143 | u8 da (0 = kein Ereignis: Frame ist dann nur 3 Byte "01 43 00")
     | u8 hatT3 | u8 01 | u32 Phase | u64 T1 | u64 T2 | [u64 T3, wenn hatT3] | u8 n (=5) | n x 20 Byte
0243 | wie 0143 ohne das erste Byte da  (125 Byte ohne T3, 133 mit T3)
Eintrag (20 Byte): u8 01 | u8 Luftraum-Nr (1-5, = Airspace.ID) | u32 Phase (= Phase des Kopfes) | 14 Byte 0
0343 | u8 Luftraum-Nr (1-5) | 17 Byte (Nullen, am Ende c0 cf 24)   (23 Byte, 5 Frames in derselben Millisekunde)
```
Längen 126 (Normalfall, 145 in Debug), 3 (kein Ereignis, 85), 26 (n=0, 12), 134 (mit T3, 3). Varianten je Aufnahme-Zeitraum stabil.

Zeitplan, belegt an der Aufnahme vom 21.10.2026 (T-Werte in Ortszeit): 
- vor dem Ereignis: `0143` Phase 0, T1 = Mi 17:00:00, T2 = Mi 17:10:00 (Fenster 10 min); nach Ende wird sofort der nächste Mittwoch (28.10.) angekündigt.
- 16:59:59.7: `0243` Phase 1 (Bereitschaft), T1 = 17:00:00, T2 = T3 = 17:10:00.
- 17:10:00.3: `0243` Phase 2 (Kampf), T1 = 17:00:00, T2 = 17:10:00,6 (tatsächliches Ende der Bereitschaft), T3 = T2 + 600 s = 17:20:00,6 (Ende des Kampfes). Neuladungen während dieser Zeit (17:15, 17:16) bringen `0143` mit hatT3 = 1 und Phase 2 (134 Byte).
- 17:20:00.9: `0343` fünfmal (Luftraum 1-5), danach 17:20:02.9 `0243` zurück auf Phase 0 mit den Zeiten der Folgewoche (125 Byte, ohne T3).
Phase 0 = angekündigt/idle, 1 = Bereitschaft (ReadyDuration 600 s), 2 = Kampf (BattleDuration 600 s) [B Zeiten, H Zuordnung 1/2 zu Bereit/Kampf]. Die 14 Nullbytes je Eintrag (Zähler/Bewerber/Sieger?) blieben in allen Frames 0, auch im Kampf [?]. Die 3 Bytes `c0 cf 24` im `0343` unbekannt [?]. Eine Teilnahme des Spielers wurde nicht aufgezeichnet.

Nutzen: Termin und Phase der Luftraumschlacht sind ohne Login-Daten live lesbar (Countdown im Client möglich).

---

## 7. `0x62e2` und `0x61e2` [B Aufbau, H Bedeutung]

`62e2`: 120 Byte, 308 Frames. **Nicht nur "Shop öffnet"**, sondern **Teil des Login-Blocks** (steht in 239/239 Logins zwischen `a5ff` und `1d56`, ~50 ms vor `43e3`) und wird **mitten in der Sitzung mehrfach erneut gesendet** (64 von 170 in den Debug-Aufnahmen, in 31 Fällen direkt gefolgt von `61e2`).

```
62e2 | u8 n1 (3 oder 7) | u8 04 | 02 02 | u64 A (0, selten 2.000.000/6.000.000) | u32 500000000 | u32 0
     | 3 x (u64 Zeit | u8 Nr | u8 Nr | u64 0 | u64 -1)   Nr = 3, 4, 5 (Nr doppelt)   [Eintrag 26 Byte]
     | u64 Zeit4 | u64 Zeit5 | u32 Zähler (4 oder 5)
```
(Zeit-Offsets: 22, 48, 74, 100, 108; Zähler Byte 116; Gesamt 120.) [B Offsets: 25 variierende Positionen = genau diese Felder]

- Zeit der ersten Zeile = Serverzeit des Logins (= Frame-Zeit - 2 s bis 0 s); die Zeile 2 steht für **Account-Ebene**: bei mehreren Logins desselben Accounts am selben Tag bleibt sie auf der Zeit des **ersten** Logins stehen (z. B. 10:23:34 bei allen Charakteren desselben Accounts am 19.10., anderer Wert 11:01:03 bei einem zweiten Account); Zeilen 3 und 4 (Nr. 4/5 bzw. Zeit4) = aktuelle Zeit oder letzter Zeitpunkt [H].
- Zeit5 (Byte 108) = 14.10.2026 11:25:52 bis 18.10., seit 21.10. 11:32-11:34 (jeweils Mittwoch nach der Wartung: Server-/Patch-Zeitpunkt?) und der Zähler am Ende springt von 4 auf 5 gleichzeitig [H: Wartungs-/Patchzähler].
- In der Sitzung kommen identische Wiederholungen (gleiche Zeiten wie im Login): auslösende Ereignisse unregelmäßig (Zoneneintritt 3 Fälle, Item-/Pfandwechsel, Gruppenfenster, Shop ...); in der Charakter-P-Sitzung mehrfach direkt nach Shop-Kaufzähler `8e56`. Der "kündigt das Öffnen an"-Zusammenhang der Notiz ist **nicht allgemein belegt** [H].
- Die konstante Zahl 500.000.000 ist unbekannt (Limit/Obergrenze?) [?].

`61e2`: 4 Byte `61 e2 00 00`, immer gleich, nur mitten in der Sitzung (31 Debug, 44 gesamt), **nie im Login**, steht in 100 % direkt nach einem `62e2` [B]. Keine Nutzlast; Marker "62e2 ist eine Aufforderung / Bestätigung" [H].

---

## 8. `0xa856` [B]
12 Byte, 242/242 identisch (`a856 00 00 00 00 00 00 00 00 00 00`). **Kommt in jedem Login** (94/106 in Debug, im selben Block: `458a` -> `a856` -> `49e3`, ~5-10 s nach `2136`), **nie bei Zonenwechsel**. Der Inhalt ist bei allen Charakteren und Zeiten identisch. Die frühere Einordnung "Aussehen-Editor Laden" ist damit mindestens unvollständig: derselbe 12-Byte-Frame wird automatisch beim Login gesendet (vermutlich "Liste der gespeicherten Aussehen-Slots: leer" bzw. Wiederholungsantwort), das Öffnen des Editors sendet ihn ebenfalls [H, siehe reference_aion2_appearance_frame]. Für den Client ohne Bedeutung als Auslöser (kein Unterscheidungsmerkmal).

## 9. `0x538d` Saison-Tabelle [B Aufbau, H Bedeutung]
1.809 Byte, 261/261 **byteidentisch** (statisch). Aufbau:
```
538d | u8 n = 34 (0x22) | n x Datensatz:
   u8 Nr | 3 x (pstring Gruppenname | u32 Zahl)       (leerer Platz = 5 Nullbytes: Namenslänge 0 + u32 0)
```
- Namen sind exakt die `GroupName`-Werte aus `SeasonSchedule`/`SeasonScheduleGroup`: Season_Main_Live, Season_Main, ArenaStrategy_Season, battlepass_season2/3_1.., battlepass_event_1/2, Global_battlepass_*, Attendance_Group_1-5, Global_Attendance_Group_*, Onboarding_* (33 verschiedene Namen, 100 % in den Tabellen). Die Nummern der Datensätze laufen 1..0x27 mit Lücken (0x14, 0x1d, 0x22 fehlen) -> 34 Datensätze, passt zu n.
- Die Zahlen bei den Namen sind klein (1-8) und entsprechen Saisonnummern bzw. Gruppen-IDs (z. B. ID 2 = `Season_Main_Live` in `SeasonScheduleGroup`). Die ersten 7 Datensätze sind gleich: (Season_Main_Live 1, Season_Main 1, Season_Main_Live 2).
- Deutung: Zuordnung "Inhaltsart -> aktuelle Saison(-gruppe)" (Boss-Herausforderung, Arena Einzel, Arena Team, Gruppen-Herausforderung, Abyss, Erwachen ... passend zu den sechs `LatestSeason*`-Schlüsseln aus 0x2036) [H].
Sonstiges: keine Zeitwerte, kein Personenbezug, ändert sich nur mit Patches.

## 10. `0x2292` Spieler-Datensatz mit GUID [B Aufbau grob, H Rest]
Nur 3 Frames in den Dumps (alle aus einer Sitzung, 143-168 Byte; in den Debug-Aufnahmen 44 Frames, 143-168 Byte), kommt zusammen mit den Aussehen-Frames `0e92`/`1c92` anderer Spieler im Blickfeld.
```
2292 | u8 A | u8 B | u32 ID1 | u16 Server (0x0518) | df 10 | pstring GUID (Länge 36)
     | u64 DBID (u32 Char-ID | u16 0 | u16 Server) | pstring Name | 22 oder ähnliche u32 | Zahlenblock (Paare 0xb2c9-ähnlich, Werte < 4.000) | 0-Block (~30 Byte)
     | 3 x f32-artig (Position?) | u8 07 | pstring Legionsname | 01 00 00 00 | 2d (Stufe 45) | 00 .. | u16 | ...
```
- Belegt: GUID-Form `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` (pstring, Länge 0x24) an fester Stelle (Byte 12); danach DBID mit Server 1304 und Spielername; später ein Legionsname und die Stufe 0x2d.
- `A`/`B` (03/04, 03/02, 07/03) und `ID1` (u32 klein: 1.635, 4.202, 2.195, d. h. wohl Legions-ID oder Entität) bleiben offen [?]. Der Frame hat also dieselbe Art Daten wie die Legions-/Namens-Frames `0x338a`/`0x558d` (Name, Legion, Stufe 45) plus GUID [H].
- Die GUID ist je Charakter eindeutig (Notiz) und könnte der Account-/Charakter-Fingerabdruck anderer Spieler sein; nicht ausgewertet.

---

## 11. Zusammenfassung der Bedeutung

| Opcode | Bedeutung | Stufe |
|---|---|---|
| `2036` | UI-Einstellungen des Charakters (JSON UTF-16 in zlib, Tastenbelegung `KeySetting`) + 361 Byte Binär-Trailer | [B] Inhalt, [?] Trailer |
| `5ae2` | FlatBuffers-Puffer 212 Byte mit 168 Zufallsbytes (Handshake/Token) | [B] Aufbau, [H] Sinn |
| `1136` | Verbindungsaufbau: 60000, 256 Zufallsbyte, -7 | [B]/[H] |
| `43e3` | 3 x (DBID, Index 1-3, Serverzeit) beim Login | [B] Aufbau, [?] Index |
| `2336` | eigene Position (x,y,z) nach dem Laden + zufällige Zahlen U (1-5)/V | [B] Position, [?] U/V |
| `0140` | Zonen-ID; in Dungeons: Zustand 1/2/3 + Startzeit | [B]/[H] |
| `0240` | Zonen-ID (+ selten Zusatz mit Zeit jetzt+3 s) | [B]/[?] |
| `0143` | Luftraumschlacht (Mittwoch 17:00 Ortszeit, 2x10 min, 5 Lufträume) | [B] |
| `0243` / `0343` | Änderungsmeldung / Ende der Luftraumschlacht | [B] |
| `62e2` / `61e2` | Zeitstempel-Zustand (Login + Wiederholungen), 61e2 = leerer Marker | [B]/[H] |
| `a856` | 12 Nullbytes, in jedem Login | [B] |
| `538d` | statische Saison-Zuordnung, 34 Datensätze | [B] Aufbau, [H] Sinn |
| `2292` | Spieler mit GUID, DBID, Name, Legion | [H] |

## 12. Offene Punkte
- `2036`: Trailer (361 Byte) entschlüsseln (Vergleich mit Änderungen im Spiel nötig); Bedeutung von X (hi16 Prüfsumme?, lo16 Zähler).
- `2336`: U (1-5) und V sind weder Kanal noch Entität nachweisbar.
- `43e3`: Bedeutung der Indizes 1-3.
- `0143/0243/0343`: Bedeutung der 14 Nullbytes je Eintrag, der Bytes `c0 cf 24`, Zustand während des Kampfes (Zähler, Sieger?); Test mit Aufnahme einer Teilnahme.
- `0140` Zustand 3 (Ende?) und die 8-Byte-Dungeon-Kennung; `0240`-Langform.
- `62e2`: Auslöser der Wiederholungen mitten in der Sitzung; Bedeutung von 500.000.000 und der Zähler 4->5.
- `5ae2` / `1136`: nur Aufbau, kein Klartext möglich (Verschlüsselung).
