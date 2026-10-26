# Zustandsframes des Logins und der Spielfenster (Stand 23.10.2026)

Alle Aufbauten wurden an 185 Login-Dumps (Legion, 13 Charaktere), einer 80-Minuten-Aufnahme des Hauptcharakters und Screenshots der Spielfenster geprüft. "Belegt" heißt: die Zahlen des Frames stimmen mit dem Fenster überein. IDs wurden gegen die dekodierten Client-Tabellen (`Content/Data/Table/*.json`) abgeglichen. Alle Zahlen little-endian, Opcode = erste zwei Frame-Bytes (hier als Hex der Bytes), `varint` = LEB128.

**Zuordnung zum Charakter:** Der Listenblock des Logins steht etwa 50 ms **vor** dem Eigen-ID-Frame `0x43e3` (u32 ab Byte 3) und gehört zu dem Charakter, dessen ID danach kommt. Der Dump-Name (`login_dumps.character_name`) ist unzuverlässig (erster gemeldeter Charakter, die Dumps überlappen sich).

Einordnung der Frames gegen den Client: die Familien in `Client/assets/aion2/protocol/opcodes.json` (Daevanion `0x26e2`, Titel `0x7956`, Ausrüstung `0x1156`, Haustiere `0x0090`, Presets `0x92e2`/`0x0051`/`0x0039`/`0x0739` ...) sind schon dekodiert und hier nicht wiederholt.

## Rezepte, Teleporter, Titel

| Frame | Inhalt | Aufbau |
|---|---|---|
| `0x7e8d` | gelernte Herstellungsrezepte | `varint n` dann `n` x (`u32 ID` aus `CraftRecipe`, `u32 0`); 107 Grundstock bis 239 |
| `0x8c8d` | gelernte Wandlungsrezepte | wie `0x7e8d`, IDs aus `ConversionRecipe`, 108 bis 146 |
| `0x3292` | freigeschaltete Teleporter | `varint n` dann `n` x `u32` aus `TeleportArtifact`, 21 bis 70 (70 = alle) |
| `0x7956` | Titel | `varint n`, `n` x `u32` (Tabelle `Title`), 16 Byte Rest (bereits im Client) |
| `0x8f8d` | Konvertierungszähler | `00 00 00 00 10`, dann Paare (`u32 ConversionRecipe-ID`, `u32 Zähler`) |

Die Rezeptzahl ändert sich mit Patches (21.10.2026: acht Rezepte entfernt, sieben Wandlungen dazu). Nicht auf eine feste Zahl bauen.

## Questlog `0x098d`

`varint n`, dann Einträge zu 22 Byte: `u32 Quest-ID` (Tabelle `Quest`), `u8 Status` (4 = abgeschlossen, 2 = laufend), 3 weitere Bytes, 5 Nullbytes, `u16` laufende Nummer, Rest 0. Laufende Quests sind länger (Zähler).

## Erfolge und Saisonmissionen `0x8356`

`varint n`, dann `n` x 17 Byte: `u32 ID`, `u32 Zähler`, `u64 Zeit (ms, letzte Änderung)`, `u8 Extra` (erreichte Stufe). Die ID ist entweder eine Schritt-ID (`AchievementStep.DataId`) oder eine Erfolgs-ID (`Achievement.AchievementId`); bei Erfolgs-IDs liegt der Gesamtzähler (Monster getötet, Gegenstandsstufe ...).

**Saisonmissionen** sind Achievement-Gruppen `Group_Season_G1_L_*` (Abyss, Nightmare, PartyChallenge, Thema, Event, Unlock); `ResetCycleType` None = Saison, Weekly = wöchentlich; die Stufen stehen in `AchievementStep` (Step, Count). Belegt gegen alle Saisonmissions-Fenster, z. B. Monster 2.332 bei Stufen 2000/5000/10000 ("Stufe 2/3, 2332/5000"), Abyss-Zeit in Minuten (726 = 12,1 Stunden bei Stufen 10/30/60 Stunden), Spielertötungen 91 bei 50/100/200/300/500, Wochenpunkte 3.440 (ID 210000100). Die Missionsnamen stehen in `AchievementString`.

## Versorgungsgesuche `0x56e2`

`56e2 | u8 6 | Gruppen`, je Gruppe 22 Byte Kopf (`u32 Gruppen-ID`, `u8 Tab-Nr`, `u64 Beginn ms`, `u64 Ende ms`, `u8 Anzahl`) und `Anzahl` x 12 Byte (`u32 Gruppen-ID`, `u32 Gesuch-ID`, `u32 Fortschritt`). Vier gefüllte Tabs (Notfall/täglich, Wöchentlich, Regulär Herstellung, Regulär Boss), zwei leere Platzhalter-Köpfe (Gruppen-ID 0, Tab 5 und 6) am Ende (44 Byte). Elyos: Gruppen-IDs `54xxxxx`, Asmodier `64xxxxx`. Fristen: Notfall täglich 09:00 bis 09:00, Wöchentlich Mittwoch 09:00 bis 08:00, Regulär ohne Frist.

Die Gesuch-ID steht in `PeriodItemCollectionList` (Titel-Key, `TargetCount`, `TargetItems`, `RewardItems`). Erledigt = Fortschritt >= `TargetCount`. Belegt gegen die Fenster (Wöchentlich 4/10, Herstellung 12/54, Boss mit 6 angefangenen).

### Abgabe eines Versorgungsgesuchs (Änderung während der Sitzung, belegt 23.10.2026)

Bei jeder Abgabe kommen in derselben Millisekunde drei Frames (Beispiel: Notfall-Gesuch 1011116 "5 Silentium" und 1011118 "10 Silentium", Belohnung 980 und 2.240 Abyss-Punkte, 250 und 570 Verzauberungsmünzen):

| Frame | Aufbau | Bedeutung |
|---|---|---|
| `0x3ee2` | `00 00 \| u32 Gruppen-ID \| u32 Gesuch-ID \| u32 Fortschritt (absolut) \| u8 Abschluss (00 Teilstand, 01 abgeschlossen) \| u8 n \| n x Eintrag zu 17 Byte` | **Fortschritt gesetzt**; Eintrag: `u32 uid \| u32 extra \| u32 Item-ID \| u32 abgegebene Menge \| u8 Behälter` |
| `0x3fe2` | `Gruppen-ID \| Gesuch-ID \| 00 00 00 00 \| 02 \| u32 uid1 \| u32 Menge1 \| u32 uid2 \| u32 Menge2` | **Belohnung**: Menge1 = Verzauberungsmünzen (250/570), Menge2 = Abyss-Punkte (980/2.240); stimmt mit `RewardItems` der Tabelle überein |
| `0x1b56` | Inventaränderung (bekannte Familie) | verbrauchte Gegenstände und erhaltene Belohnung |
| `0x8456` | 20 Byte, Zähler `u32` + Zeit + `04` | zählt hoch (105 -> 106), Bedeutung offen (vermutlich Abgabezähler/Erfolg) |

Die **Gruppen-ID des Notfall-Tabs steigt täglich um 1** (22.10.: 5401512, 23.10.: 5401513); Wöchentlich bleibt 5401521 für die ganze Woche. Fortschrittsänderungen der Gesuche lassen sich also live mitlesen.

**Aufbau des Eintrags (belegt 23.10.2026 an Notfall- und Herstellungs-Abgaben):** Ein abgegebener Gegenstand ist ein 17-Byte-Eintrag `uid | extra | Item-ID | Menge | Behälter`. Stapelware (z. B. "Silentium", Item-ID 630840002, Name `Refund_A_s_02a_b`) hat `uid = 0`, `extra = 0` und die abgegebene Menge, Ausrüstung hat die eindeutige `uid`, ein Datenfeld und Menge 1. Die Item-ID steht in der Tabelle `Item` und stimmt mit den akzeptierten Gegenständen des Gesuchs überein (Herstellungs-Gesuch 1013007 "Rüstung/Waffe ... craftCombo": Item 110150014 = `equip_wp_Greatsword_L_r_craftCombo_001a_s`, Gesuch 1013002: Item 110460023 = `equip_wp_Bow_L_c_craft_001a_s`). Frames mit mehreren Gegenständen sind entsprechend länger (35 Byte bei einem Eintrag, 52 Byte bei zwei). Das letzte Byte des Eintrags ist vermutlich die Behälter-Nummer (Stapel im Inventar 1, Notfall-Abschluss 0x0f, Ausrüstung 2), nicht ein Abschlusskennzeichen; Abschluss steht im Byte vor `n`.

**Herstellungs-Reiter belegt (12:33, Gruppe 5403141):** Gesuche 1013002 (Ziel 5), 1013003 (Ziel 5), 1013007 (Ziel 3, zwei Gegenstände in einer Abgabe, Fortschritt 2) und 1013015 (Ziel 3): `0x3ee2` mit Fortschritt 1, 1, 2, 1; der Login-Stand war jeweils 0. Dieselben Frames wie beim Notfall-Tab, keine Belohnung bei Teilständen.

**Was die Daten sicher sagen und was nicht (23.10.2026):**
- Der Listenblock mit `0x56e2` (zusammen mit `0x8356`, `0x0b61`, `0x7e8d`, `0x098d`, `0x43e3`) kommt **nur beim Login und beim Charakterwechsel**. Zonenwechsel (`0x2136` um 11:06 und 11:12) und das Öffnen des Gesuch-Fensters senden ihn **nicht** erneut.
- **Teilabgabe (belegt 23.10.2026, 12:30):** Der Stand beim Login war 0 von 5 bzw. 0 von 10. Es wurden 4 bzw. 8 Stück abgegeben: `0x3ee2` mit **Fortschritt 4 bzw. 8 (absolut)**, abgegebener Menge 4 bzw. 8 und **ohne** `0x3fe2` (keine Belohnung). Gegenüber dem Abschluss unterscheiden sich zwei Bytes: Abschluss `... Fortschritt 01 01 00 ... uid Menge 0f`, Teilabgabe `... Fortschritt 00 01 00 ... uid Menge 01` (erstes Byte nach dem Fortschritt 00 statt 01 = Abschlusskennzeichen; das letzte Byte 01 statt 0f ist vermutlich die Behälter-Nummer des Gegenstands). Dazu kommt `0x1b56` mit der Bestandsänderung des Gegenstands. **Abschluss nach Teilabgabe (12:32):** Gesuch 1011118 von 8 auf 10, abgegeben 2: `0x3ee2` mit Fortschritt 10, `01 01 ... 0f`, gleichzeitig `0x3fe2` (570 Münzen, 2.240 Abyss-Punkte); Gesuch 1011116 von 4 auf 5, abgegeben 1, ebenso. Die abgegebene Menge im Frame ist also der **Zuwachs** dieser Abgabe, der Fortschritt der absolute Stand. Die 15 früher beobachteten Fälle (13 Dumps, 2 heute) waren alle Abschlüsse mit Belohnung, `0x3ee2` und `0x3fe2` gemeinsam.
- Gesuche mit Ausrüstung haben längere Frames (52 und 69 Byte statt 35 Byte, je abgegebenem Gegenstand weitere Felder).
- Abgaben im **Boss-Reiter** liegen in keiner Aufnahme vor; Herstellung ist seit 12:33 belegt (siehe oben).
- Für den Boss-Reiter fehlt weiterhin eine Aufnahme einer Abgabe.

## Dungeon: Garantierte Belohnung `0x1257`

`1257 | u8 n | n x (u32 Dungeon-ID, u16 Zähler)`; Dungeon-IDs aus `Dungeon`: 600001 Krao, 600011/12/13 Urugugu, 600021/22/23 Feuertempel, 600031/32/33 Draupnir, 600071/72 Vakron, 600091/92/93 Grimmhorn; Endziffer 1 leicht, 2 mittel, 3 schwer. Der Zähler der Stufe Mittel ist der Stand des blauen Balkens; leicht ist immer `01 01`. Der Nenner (14/21/28) steht nicht im Frame.

## Tickets und Zähler `0x0b61` / `0x0c61`

Siehe [ticket-frames.md](ticket-frames.md). Beispiele (belegt gegen Fenster): Odenergie (Ticket 60000001) Zähler + Bonus ("60 (+875)/840"), Shugofesta-Schlüssel (Ticket 1) ("4 (+5)/21"), Albtraum-Versuche (Ticket 4) ("6 (+6)/14"), täglicher Dungeon (101), Abyss-Nutzungszeit (Ticket 3, ms).

## Kaufzähler `0x8e56` / `0x078c`

`8e56 | 00 00 | u8 n | n x (u32 Zähler-ID, u32 gekauft, u64 Reset ms)`, kommt beim Öffnen eines Shops. `0x078c`: Münz-Shops (`Shop-ID | n | n x (Waren-ID, gekauft) | Serverzeit`).

## Rekorde und Ränge

- `0x37e2` Albtraum-Rekorde je Boss (53 Byte je Boss: Boss-Gruppen-ID, Stufe, Punkte, Zeit, ...), `0x3193` Transzendenz je Dungeon-Gruppe (50 Byte), `0x3ee3` Aszensusritus.
- `0x2ae3` Arena-Wertung je Modus: `u8 n`, je 14 Byte (`u8 Modus` 1 = 1v1, 2 = 5v5, 3 = 10v10, `u64 Wertung`, `u8 Siege, Niederlagen, Unentschieden, Abbruch, Siegesserie`). Belegt gegen die Fenster.
- `0x578d` Rangprofil je Inhaltstyp (beim Öffnen des Fensters): `578d 00 00 | Typ | Stufe | u32 Rang | ... | u64 Punkte | JSON | i32 Rangänderung | ...`. Typ 1 Abyss, 3 Albtraum, 4 Transzendenz, 5 Arena 1v1, 6 Arena 5v5, 21 Aszensusritus. Stufe 0 = Gesamtrang, Stufe 2 = Klassenrang. Rangänderung -1 = "▼1".
- `0x2ce3` Arena-Verlauf als Klartext-JSON (letzte zehn Spiele je Modus): `{"start_time","winning_team","contents_id","member":[{"team","class","level","server_id","dbid","name"}]}`; `contents_id` 55 = 10 gegen 10. `server_id` ist die NC-Server-ID, `dbid` die 64-Bit-Charakter-ID.

## Ranglisten `0x558d`

`558d 00 00 | Typ u8 | Klasse u8 | varint n | n Einträge`, bis 500 Einträge je Liste (Arena Kooperation je Klasse weniger). Typ wie bei `0x578d`. Klasse: 0 = alle (dann trägt jeder Eintrag seine Klasse), 2 Gladiator, 3 Templer, 4 Waldläufer, 5 Assassine, 6 Beschwörer, 7 Magier, 8 Kleriker, 9 Kantor (Reihenfolge 6/7 nach der Klassenliste angenommen).

Eintrag: `u32 Rang | u32 Char-ID | 00 00 | u16 Server | Name (Längenbyte+UTF-8) | Legion (Längenbyte+UTF-8) | 01 00 2d | 7 x 00 + Klassenbyte | u64 Punkte | JSON (Längenbyte) | i32 Rangänderung | u32 0 | u32 Rangstufe | u32 Rangstufe`. JSON Arena: `play_count`, `win_count`, `lose_count` (Siegesquote im Fenster = Siege / (Siege + Niederlagen), abgerundet); Abyss: `kill_count`, `death_count`, `assist_count`; Albtraum/Transzendenz/Aszensusritus: `best_clear_*`. Die Rangstufe ist das Symbol "Saison-Rang" (Tabelle `SeasonRankingGrade`, Abyss nach Punkten in der Gruppe `ranking_grade_abyss_S2/S3`, 499 von 500 Einträgen stimmen). Alle Server-IDs einer Liste liegen im selben Fraktionsblock (Elyos 1301 bis 1323): die Rangliste gilt je Fraktion und Region, nicht je Server. Belegt gegen zehn Screenshots der Arena-Ranglisten und die Abyss-Rangliste.

## Legion und Freunde

- `0x058a` Legionsliste: nur Mitglieder auf Stufe 45. Eintrag nach Char-ID sortiert: `u32 Char-ID | u16 0 | u16 Server | u32 Klassencode | Name | 0x2d | 3 x 00 + Rang | [Kommentar] | u64 Zeit | u32 Subzone (Region, 0 = offline) | u32 Gegenstandsstufe | u32 Kampfkraft | u32 0 | u8 | u32 E | u32 0`. Rang: 0 Legionsmeister, 1 Eliteoffizier, 2 Legionsrekrut. **Gegenstandsstufe** ist die Zahl der Charakterliste (z. B. 2.712), die **Kampfkraft** die große Zahl (z. B. 142.948, entspricht dem NC-Feld `combat_power`). Wert E ist unbekannt (11 verschiedene Werte bei 23 Mitgliedern).
- `0x74e2` Freundesliste (Anzahl varint, z. B. 18 von 50): je Eintrag Char-ID, Server, Name, Rang/Flags und Zeit, **ohne** Gegenstandsstufe und Kampfkraft (701 Byte für 18 Freunde). Nur die Legionsliste `0x058a` trägt diese beiden Werte.
- `0x82e2` Meldung "Freund oder Legionsmitglied online/offline" (36 Byte).
- `0x338a` Legion eines sichtbaren Spielers: `Char-ID u32 | 00 00 00 00 | 18 05 | Längenbyte+Legionsname | 01 00 | Rang 01/02 | Nullen` (Rang-Byte nicht geprüft). 23.100 Frames in den Dumps.

## Weitere Besitz- und Fensterframes

- `0x7256` besessene Flügel (`u8 n`, je u32 Flügel-ID + u8 0), `0x3356` Götterstein-Kombinationen, `0x5b8d` besessene Skins (varint n, je u32 Skin-ID + u32 Zähler; 361 Einträge), `0x378d` Essenzextraktion (`u8 n`, je `u32 GatherSkill-ID`, `u32 Stufe`, am Ende `u32 0`).
- `0x0197` Liste offener Gruppen/Räume (Raum-ID, Raumname, Teilnehmer im Format des Rosters `0x0297`), `0x0195` freigeschaltete Spielinhalte des Servers (36 `u32` aus `ContentsUnlock`, bei allen Charakteren gleich).
- `0x0056`/`0x0556`/`0x8ce2` Schnellleisten-Inhalt (Skills und Items), siehe [skill-frames.md](skill-frames.md).
- `0x1636` Spielserver-Adresse (IP-String, Port 13328), `0x1536` Login-Token (`+Zahl:GUID`).
- `0xaf8a` (24 x ID + Byte), `0x0be3` (3 x ID + Wert), `0x538d` (statische Saison-Tabelle): Bedeutung offen.

## Offen

Wert E der Legionsliste, `0xaf8a`, `0x0be3`, Rangstufen-Regel bei Albtraum/Transzendenz/Aszensusritus, Rang-Byte von `0x338a`, Nenner der garantierten Belohnung, 836 der 2.348 Erfolgs-IDs außerhalb der Schritt-Tabelle (826 davon sind Erfolgs-IDs).

## Asmodier-Gegenprobe (23.10.2026, Aufnahme eines Asmodier-Spielers vom 18.10., Feedback-Anhang)
Die Aufnahme enthält zwei Logins (zwei Charaktere, Stufe 10 und 45, Europa). Bestätigt für Asmodier: der Login-Block (`0x7e8d`, `0x8c8d`, `0x3292`, `0x098d`, `0x56e2`, `0x8356`, `0x0b61`, danach `0x43e3` mit der Eigen-ID) hat dieselben Frames und Längenordnung wie bei Elyos; Rezeptliste `0x7e8d` enthält nur Rassen-Rezepte "Dark" (109 und 123 Stück); Versorgungsgesuch-Gruppen in `0x56e2` liegen bei Asmodiern im Bereich 64xxxxx (z. B. 6401415, 6403131); die Charakterliste `0x2636` trägt wie bei Elyos die 12-stellige Account-Kennung. Nicht in der Aufnahme: `0x058a`, `0x558d`, `0x2ce3` (Legion-, Rang-, Arena-Fenster wurden nicht geöffnet). Der Crafting-Rechner kennt beide Fraktionen (`Visible(elyos)`, Zwillingsrezepte), die Regel "nur gelernte Rezepte" greift für Asmodier direkt.

### Wann kommt die Charakterliste `0x2636`? (23.10.2026, Gegenprobe an drei Aufnahmen)
Nicht beim ersten Login nach dem Spielstart. Sie kommt nur, wenn man im Spiel zur Charakterauswahl zurückgeht, ca. 0,3 s vor dem nächsten Login-Block (Aahz 7.318,3 s nach Aufnahmebeginn, `0x2636` dann `0x0b61`/`0x43e3` bei 7.318,6 s; Asmodier-Spieler 79,5 s nach dem ersten Login, dann der zweite Login). Beim Asmodier-Twink (Spielstart, direkter Login) fehlt sie, obwohl das Tool vorher lief. **Korrektur desselben Tages:** die erste Liste kommt als `0x1536` (gleicher Inhalt, 16 Byte mehr Kopf; Schlüssel bei Byte 26 statt 10) auf der Spielverbindung; die Lobby (Port 13700) trägt nur die Serverliste `0x0939`. Der Client liest `0x1536` ab 500 Byte seitdem mit.
Nachtrag: Ein Asmodier-Twink (ein einziger Charakter auf dem Server) schickte die Liste nach dem Zurückgehen zur Auswahl um 17:46:03; der Schlüssel war derselbe wie am 20.10., Zählbyte `2c`, Fraktion 2: die Strukturprüfung des Schlüssels trifft damit auch auf den Asmodier-Twink zu. Dass nur ein Charakter auf dem Server liegt, ist nicht der Grund für das Fehlen beim ersten Login: Aahz (vier Charaktere) hat sie dort ebenfalls nicht.

### Wann die Charakterliste im Login steht (Stand 23.10.2026, Abend, drei Beobachtungen)
Die Liste kommt in drei Fällen: (1) als `0x2636` bei jedem Zurückgehen zur Charakterauswahl, also bei jedem Charakterwechsel (belegt an 20 Wechseln); (2) als große `0x1536` beim Spielstart nach einem frischen Start (belegt an einem Fall, der Auslöser „nur ein Charakter auf dem Server“ ist eine Vermutung); (3) als große `0x1536` beim Wechsel auf ein anderes Konto oder einen anderen Server (belegt an einem Fall, 9.961 Byte). Ein weiterer Login desselben Kontos auf demselben Server nach einer neuen Verbindung bringt nur eine kurze `0x1536` mit 80 Byte (Aufnahmen vom 22. und 23.10., zusammen über 30 Logins) und keine Liste. Ob (2) auch für ein Konto mit mehreren Charakteren gilt, ist offen: ein frischer Spielstart mit einem Mehr-Charakter-Konto liegt in keiner Aufnahme vor. Der Client liest `0x2636` und die große `0x1536` (ab 500 Byte) als Liste.


### Arkana im Login-Frame 0x1156 (Stand 26.10.2026, an fünf Spielfenstern geprüft)
Jede der fünf Arkana ist ein Ausrüstungseintrag (Plätze 41-45: Gral, Pergament, Kompass, Glocke, Spiegel), gebaut wie die übrigen Einträge: Item-ID, `01 00 00 00`, 4 Nullbytes, `0b`, Platz, Nullen, dann **Plusstufe (1 Byte) und direkt dahinter die Verstärkungs-EP (u32)**; der Eintrag hat 14 Byte hinter der Plusstufe sein Größenbyte (`0x52`, beim Pergament `0x53`). Ab dem Größenbyte (`s`):
- `s+28`: `01`, `s+29`: Gott-ID (7 Nezekan/Gerechtigkeit, 8 Vaizel/Freiheit, 9 Kaisinel/Illusion, 10 Yustiel/Leben, 11 Siel/Zeit), `s+31`: Götterwert (u32, 22 bzw. 23), danach 2 Bytes.
- `s+37`: Skill-Liste 1: Anzahl (4), je Skill-ID (u32) + Grundstufe (u8); danach Liste 2 mit denselben IDs und den Zusatzstufen (gewürfelt). Summe beider = "St. +N" im Tooltip.
- Danach der Besitzerblock wie bei normalen Einträgen.
Die EP-Obergrenze steht nicht im Eintrag, sie kommt aus der Spieltabelle `ArcanaEnchant` (Unique: 3000, 6000, 12000, 24000, 48000, 0 = letzte Stufe; Tools/aion2-dat/build_arcana.py). Der Set-Effekt steht ebenfalls nicht im Eintrag: Set = die letzten vier Ziffern der Item-ID (810130001 = Set 1 "Lebenskraft des Urlichts"), Texte aus `SkillAbnormalString_40001011` (2 Teile, Angriffskraft (PvE) +60) und `..._40002011` (4 Teile, +150).
Skill-Liste `0x0051`: je Skill Gesamtstufe, Basisstufe und ein dritter Wert (weitere Quelle). **Gesamtstufe = Basisstufe + Skillboni der Ausrüstung + Skillboni der Arkana + dritter Wert**, bei allen 17 Skills der Arkana exakt aufgegangen. In Inspect-Frames anderer Spieler ist das nicht geprüft (keine Aufnahme mit Arkana).
