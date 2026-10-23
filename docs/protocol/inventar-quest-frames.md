# Inventar-, Beute-, Herstellungs-, Quest- und Shop-Frames (Server -> Client)

Stand 23.10.2026, Auswertung von Debug-Aufnahmen 21.-23.10. und Login-Dumps. Die Auswertung stammt von einem Hilfsagenten, die Kernaussagen (1f56-Schlussbyte, 8456) sind nur dort belegt, nicht einzeln nachgeprüft. Siehe auch `opcode-katalog.md`, `login-und-fenster-frames.md`.


Stand 23.10.2026. Nur gelesen, Python-Auswertung. Skripte liegen in `.../scratchpad/c3/` (`s2.py` bis `s64.py`; Caches `ev.pkl` = Frames der Debug-Aufnahmen, `dev.pkl` = Frames der 185 Dumps, `winev.pkl` = Fenster um 418d/928d/918d).
Notation: Opcode = erste 2 Frame-Bytes in Hex, alle Zahlen little-endian, `varint` = LEB128. [B] belegt, [H] Hypothese, [?] unbekannt. "Charakter A" = Hauptcharakter, "Charakter B" = zweiter Charakter.
Datenbasis: Debug-Aufnahmen 21.-23.10. (6.188 Frames 1e56, 6.086 1f56, 20.859 8456, 11.625 728d, 398 6f8d, 182 1356, 28 1556, 953 108d, 96 418d usw.) plus die Dumps (aus ihnen stammen die Zahlen der Aufgabenstellung).

## 0. Kurzfassung und Korrekturen an den Notizen

| Opcode | Ergebnis |
|---|---|
| 1e56 | [B] Beute erscheint am Boden: `op | n u8 | n x 63-Byte-Satz | Schlussbyte`. Beute-Id, Item-Id (in Tabelle Item: 6458 von 6458), Menge, Besitzer (Charakter-Id), Ablaufzeit. Die Offsets im Client stimmen. |
| 1f56 | [B] Beute verschwindet: `op | n | n x 20-Byte-Satz | Schlussbyte`. **Schlussbyte 1 = vom Besitzer aufgenommen (1b56 folgt sofort), 0 = anders verschwunden. Das ist das Gegenteil der Kommentare in `Aion2Loot.cs` (dort zählt nur 0).** |
| 1356 | [B] "Gegenstand benutzt" (Verbrauchsgut, eine Einheit): Item-Referenz, bit-gleich mit dem Anfang des 1b56-Satzes derselben uid. Header `04 03` = ok, `0b 01` = abgelehnt (1 Fall, kein 1b56). |
| 1556 | [B] wie 1356 plus `u64` Anzahl: N Stück benutzt (immer Stapel weg, Anzahl = alter Bestand). |
| 8f56 / 8c56 / 8e56 | [B] 8f56 = Kaufzähler-Update (ID, gekauft gesamt, Reset), 8c56 = **Kaufmenge dieses Kaufs**, 8e56 mit n=1 ist dasselbe wie 8f56, 8e56 mit n=7 ist die Login-Liste. |
| 6f8d | [B] Herstellungsergebnis: Rezept-Id (Tabelle CraftRecipe) + Liste (Art, Wert, Wert); Bedeutung der Liste [?]. Kommt 1,5 s nach Beginn zusammen mit dem eigenen 728d. |
| 7d8d | [B] Berufsstufe nach Aufstieg (1 Frame). 7c8d (19 Byte) ebenfalls bestätigt. Die XP-Maxima stehen in der Tabelle CraftMastery. |
| 0f8d | [B] Quest abgegeben (Turn-in) mit Instanznummer; Anhang [?]. |
| 118d | [B] Quest angenommen, mit Quest-Id und Instanznummer (auch durch eine Aufträge-Schriftrolle auslösbar). |
| 108d | [B, mit Korrektur] **Kein Saison-Mission-Frame**, sondern der Fortschritt einer angenommenen Quest (Pflicht/Story): Instanznummer u32, Zustand, Schritt, bis zu 6 Zielzähler. Saisonmissionen stehen in **8456**. |
| 918d | [?] 8 Byte, u32 + 00 00; Kill-Fortschritt nicht belegbar. |
| 928d | [H] Spieler-eid (varint) + Zustandsbyte, gehört zu Spielern in Sichtweite. Kein Auftragsfortschritt. |
| 418d | [?] `418d 0000`, keine Nutzlast, Auslöser unbekannt; "Remote-Lager öffnen" nicht belegt. |
| 8456 | [B] Erfolge-Zähler live (Achievement-Fortschritt): Erfolgs-Id, Zähler, Zeit, Stufenbyte. Enthält Saisonmission-Zähler, Versorgungsgesuch-Zähler, Herstellungszähler. |
| 728d | [B/H] Spieler-eid + Kennung + Berufs-Id: "Spieler stellt gerade her". Eigener Charakter: immer gleichzeitig mit 6f8d. |

## 1. 1e56 und 1f56 (Beute am Boden)

### 1e56 (6.188 Frames) [B]
- Länge `3 + 63*n + 1` (n = Byte 2) trifft in 6.188 von 6.188 Frames zu (67, 130, 193, ...). 6.458 Sätze. Mehrsatz-Frames (131) enthalten meist Beute verschiedener Monster (nur 36 von 131 mit gleichem Monster).
- Satz (Offset im Frame = 3 + Offset im Satz):
  - `0..4` Monster-Entität u32 (4.140 verschiedene Werte)
  - `4..16` Position 1, `16..28` Position 2 (je 3 x f32, Zentimeter; Abstand der beiden Punkte Median 127 cm: Quelle und Landepunkt der Beute) [H]
  - `28..32` u32, in 1f56 wiederholt (839 verschiedene Werte) [?]
  - `32..36` **Beute-Id** u32 (Schlüssel für 1f56) [B]
  - `36..40` **Item-Id** u32, 6.458 von 6.458 in Tabelle Item [B]
  - `40..44` **Menge** u32 (5.964 mal 1; 439 mal 0 bei den Feldereignis-Gegenständen 534950001/534960002 `FieldEvent_UseItem_NoEffect`)
  - `44` u8 Art: 2 (6.153), 1 (280), 5 (25) [?]
  - `45..49` **Besitzer = Charakter-Id** u32 [B]. Gegenprobe: 211765 und 266572 sind die Char-Ids der beiden eigenen Charaktere; derselbe Wert steht im Charakter-Frame 3336, im 43e3-Frame und in der Legionsliste 058a. 421 verschiedene Besitzer, nie 0.
  - `49..53` = `u16 0 | u16 Server-Id` (1304 in 3.866 Sätzen, daneben 1303, 1301, 1302, ... auch andere Server; 305 Sätze mit anderer Kennung 213) [H]
  - `53..61` u64 Epoch-ms = **Ablaufzeit**: Median 123 s nach Aufnahmezeit, Maximum 125 s [B]. Beute-Lebensdauer also rund 120 s.
  - `61` u8 (1: 6.433, 3: 23, 2: 2), `62` immer 3 [?]
- Schlussbyte hinter dem letzten Satz: 1 (5.845 Sätze) oder 2 (613). 2 betrifft fast nur Beute fremder Besitzer (nur 18 Sätze mit eigener Char-Id), 1 ist aber nicht auf eigene Beute beschränkt [?].
- Beispiel (n=1, 67 Byte): `1e5601f67300003d0684c4d7d1d0c700004041f52a8dc4d206d1c700003c42c54b0000b8d90800a643de1f0100000002657c03000000180519a3df5ea1010000010301`: Monster 0x73f6, Beute-Id 0x8d9b8 (580024), Item 0x1fde43a6 (534660006), Menge 1, Art 2, Besitzer 0x37c65 (228453), Server 0x0518 = 1304, Ablauf 0x01a15edfa319 ms, Schlussbyte 01.

### 1f56 (6.086 Frames) [B]
- Länge `3 + 20*n + 1` (6.379 von 6.380 Sätzen konsistent, 7 Frames unregelmäßig). Satz: `Monster u32 | u32 (= Satz 1e56 Offset 28) | Beute-Id u32 | Besitzer u32 | u16 0 | u16 Server`. Monster, das u32 und der Besitzer stimmen mit dem 1e56-Satz derselben Beute-Id überein (6.062 gepaart; Besitzer gleich in 5.350 von 5.350 mit Besitzer; 721 Sätze mit Besitzer 0).
- **Schlussbyte (wichtig):**
  - Satz der eigenen Beute (Besitzer = eigene Char-Id): Schlussbyte 1 und ein 1b56 desselben Items innerhalb 1,5 s: **1.394 von 1.394**; Schlussbyte 0 (7 Fälle) ohne 1b56. Zeitabstand 1b56 minus 1f56: für Schlussbyte 1 scharfer Gipfel bei 0,0 s (1.441 Treffer in der 0,5-s-Klasse, Hintergrund ca. 20 je Klasse), für Schlussbyte 0 kein Gipfel.
  - Bestätigt in den 185 Dumps (18.-23.10.): Schlussbyte 1 mit 1b56: 164 von 1.064, Schlussbyte 0 mit 1b56: 26 von 931 (Zufallsrate).
  - Zeit zwischen Erscheinen und Verschwinden: Schlussbyte 1 immer unter 9 s (Median 0,3 s, also Aufnahme), Schlussbyte 0 mit Besitzer Median 0,9 s (bis 118 s, Ablauf), ohne Besitzer bis 178 s.
  - **Schluss: 1 = aufgenommen (vom Besitzer), 0 = verschwunden/anderweitig weg. Der Client (`Aion2Loot.NoteGroundGone`) verlangt `frame[^1] == 0` und beschreibt 0 als "picked up". Nach diesen Daten zählt er die echten Aufnahmen nicht (es müsste `== 1` heißen).** Nur gelesen, nichts geändert. [B]
- 1b56-Treffer fehlen bei Seelen (`VehicleSoul_*`, Behälter 4, kein Inventarplatz) und bei den Feldereignis-Gegenständen (534950001/534960002): dort ist der Abgleich per 1b56 nicht möglich (passt zur Notiz zu Behälter 4).

## 2. 1356 und 1556 (Gegenstand benutzt) [B]
- 1356: 16 Byte `13 56 | 04 03 | uid u32 | 00 | x u8 | 00 00 | Item-Id u32` (182 Frames). Der 12-Byte-Block ab Byte 4 ist **bit-gleich** mit dem Anfang des gleichzeitigen 1b56-Satzes (uid, x, Item-Id) [B]: `1356 0403 249e0200 00100000 44da051e` zu `1b56 01 02 00 249e0200 00100000 44da051e ...`.
- 1556: 24 Byte, wie 1356 plus `u64` Anzahl (28 Frames): `15560403 f2af0900 00160000 42da051e 0300000000000000` (3 Stück).
- Gegenprobe mit 1b56 (uid-Vergleich): 1356 gefolgt von 1b56 Art 1 mit Bestand -1 (84 Fälle) oder Art 2, Stapel weg (38 Fälle). 1556 immer Art 2, Anzahl = Bestand davor (18 von 18). Anzahlen 2, 3, 4, 6, 8, 9, 18, 22.
- Gegenstände (alle `EItemType::Usable`): Tränke (HP/FP/MP/Heilung), Schriftrollen, Ticket-Gegenstände `G_event_Recharge_*` (503700031-36), `Charge_DaevanionPoint`/`Charge_SkillPoint`, Arkana-Karten, Quest-Schriftrollen `QuestScroll_Duty_*` (dann folgt 118d, s. u.). 0c61 folgt nur bei 24 von 182 (1356) und 12 von 28 (1556), also bei den Ticket-/Ladeitems (deckt sich mit der Notiz).
- **Abgelehnter Gebrauch** [H]: `13560b01b3661900004d0000a6c0671e` (Trank 510116006, 20:51:36, zwischen zwei erfolgreichen Benutzungen, kein 1b56): Header `0b 01` statt `04 03` = Ablehnung (Abklingzeit?). Nur ein Fall.

## 3. Shops: 8e56, 8f56, 8c56 [B]
- **8e56** Login-Liste: `8e56 | 00 00 | n u8 | n x (ID u32 | Zähler u32 | Reset u64 ms)`, 117 Byte bei n=7. Jetzt **7 Einträge** statt 6: 1169, 1254, 1262, 100100, 100134, 100135, **100141** (neu). Reset-Zeiten (lokal Europe/Berlin): 1169/100100/100134/100135/100141 Mi 28.10. 08:00 (= Mi 16:00 KST, wöchentlich); 1262 (Monatsticket) 01.11. 08:00 (monatlich); 1254 31.12. 16:00.
- **8e56 mit n=1** (21 Byte) ist ein Einzel-Update gleichen Aufbaus (`8e56 00 00 01 | ID | Zähler | Reset`), gleichzeitig mit 8f56 (Beispiel Charakter B 18:05:50: 8e56 [100134, 11] und 8f56 100134/11).
- **8f56** (18 Byte): `8f56 | ID u32 | gekauft gesamt u32 | Reset u64`. 5 Frames. Beispiel `8f56 26870100 14000000 80fd4e81a1010000` = ID 100134, 20, Reset 28.10. 08:00.
- **8c56** (12 Byte): `8c56 | 00 00 | ID u32 | Kaufmenge u32`. **Menge dieses Kaufs**, nicht der Stand: Reihe 18:05:50 (8f56: 11, 8c56: 11), 18:05:57 (18, 7), 18:18:15 (20, 2) passt exakt (11+7=18, 18+2=20).
- 8c56 für IDs ohne Zählerliste: `8c560000c804000002000000` (ID 1224, 2 Stück) und `8c560000c704000001000000` (ID 1223, 1 Stück); 1,65 s bzw. 1,9 s später je ein 1b56 mit neuem Platz und **Menge 2 bzw. 1** der Items 632440001 (`Material_SkinCombine_ProbUp_01a_A_l_b`, Legend) und 632450001 (`..._r_b`, Rare) [B]. 1223/1224 sind Waren-Ids dieses Shops; kein Zähler/Reset (nicht in 8e56).
- Die IDs 1169, 1254, 1262, 100100, 100134, 100135, 100141, 1223, 1224 stehen **nicht** als Ganzzahl in GameShop, GameShopGoods, RandomShopGoods usw. (1223 nur als NpcTalk-Id): serverseitige Waren-/Limit-Schlüssel, keine Client-Tabellen-Ids [B, negativ]. Die Bedeutung von 100134 = Odenergie (Notiz) wurde hier nicht neu geprüft.

## 4. Herstellung: 7c8d, 7d8d, 6f8d, 728d, 8456

### 7c8d und 7d8d [B]
- 7c8d (19 Byte), 2 Frames in den Debug-Aufnahmen (Charakter A, Waffenschmieden): `7c8d 01 35020000 00000000 14000000 00000000` = Beruf 1, XP-Stand 565, Zuwachs 20; der nächste Frame zeigt Stand 585 (`49020000`): Stand + Zuwachs stimmt.
- 7d8d (15 Byte, 1 Frame in den Dumps): `7d8d 01 3300 00000000 00000000 0100`: Beruf 1, Stufe 0x33 = 51 absolut, XP-Stand 0 danach, Zähler 1; gleichzeitig ein 1b56. Passt zur Notiz.
- **XP-Maximum je Stufe** steht in der Client-Tabelle **CraftMastery** (`CraftMasteryExpNeed`, Stufen 1-100, Grad Beginner bis 50, danach Intermediate): Stufe 27 = 151, 30 = 175, 47 = 496, 50 = 0, 51 = 602, 52 = 621, 53 = 639, 54 = 660. Das deckt die Notizpaare (27 = 151, 47 = 496, Fortgeschritten 1 = 602, 2 = 621, 3 = 639) [B]. **Geklärt 23.10.:** Die Stufe der Tabelle ist absolut (Fortgeschritten St. n = Stufe 50 + n). Fortgeschritten 30 = Stufe 80 = 1.735, Fortgeschritten 31 = Stufe 81 = 1.809 (Bildschirm: Kochen St. 31, 989/1.809). Die Tabelle gilt für alle Berufe. Das XP-Feld im Frame `0x7c8d` ist der Stand NACH dem Zuwachs: 989 + 120 = 1.109 und 1.109 + 147 = 1.256 (zwei Kochvorgänge, Beruf 5) [B].

### Herstellung: Erfolg und Fehlschlag (Kochen, 23.10.2026) [B, 4 Versuche]
Test mit 2 Erfolgen und 2 Fehlschlägen (Rezept 514085005, Meister-Variante, Stufe 40; 3x/3x/5x Material). Ablauf je Versuch: `1b56` Materialabzug + `6f8d` zu Beginn, Ergebnis nach ca. 4-9 s.
- **Erfolg:** `7c8d` mit hohem Zuwachs (120 und 147 XP), gleichzeitig `8456`-Zähler (Erfolgs-IDs, u. a. Saisonmissionen) und ein `1b56` mit dem fertigen Gegenstand (Meister-Ergebnis: 2 Stück).
- **Fehlschlag:** nur `7c8d` mit 44 XP (nach 7,6 bzw. 9,2 s), **kein** `8456`, **kein** Gegenstands-`1b56`. Das Material ist trotzdem weg.
- Damit ist ein Fehlschlag am Fehlen von `8456` und Gegenstands-Frame erkennbar. Der XP-Betrag allein ist rezeptabhängig und nicht eindeutig (der Fehlschlagswert 44 gilt nur für dieses Rezept belegt).
- **Serienherstellung (Rollen des Mutes, 23.10., 13:28-13:33):** 16 Starts (`1b56` Material + `6f8d`), 14 Ergebnisblöcke (`8456` + Gegenstands-`1b56`, hier ohne `7c8d`, also keine Berufs-XP). Zwei Starts ohne Ergebnisblock (13:28:49 und 13:32:38) = Fehlschläge. Im Herstellungsfenster steht: "Wirst du während der Herstellung getroffen, schlägt das Handwerk fehl und die Materialien ..." [B, Text], Fehlschlag = getroffen werden [H, nicht gegengeprüft]. Aufgeteilt: Serie 1 (13:28-13:29) 10 Starts, 9 Erfolge, 1 Fehlschlag; Serie 2 (13:32-13:33, 6/6) 5 Erfolge, 1 Fehlschlag. Der User sah 4 Erfolge + 1 Fehlschlag, Herstellung 3/6 verpasst: der Strom zeigt sie als Erfolg [B]. Serie 3 (Fortgeschrittener Seelenstein, 13:34, 3/3, Rezept 314046002): 3 Starts, 3 Ergebnisblöcke, User bestätigt 3 Erfolge / 0 Fehlschläge [B]. Dauer je Herstellung 4-10 s. Ein Fehlschlag lässt sich im Strom am ausbleibenden Ergebnisblock erkennen; bei Berufen mit XP (Kochen, Schmuck) gibt es zusätzlich ein `7c8d` mit kleiner XP-Zahl, ohne `8456`.
- **Serie 4 (Saphirring, Schmuck, Rezept 213036001, 13:35-13:36, 7 Herstellungen):** Reihenfolge 1 normal (76 XP, 1b56 104 Byte), 2 **Kombo/"Strahlender Saphirring"** (98 XP, zusätzlicher `8456`-Zähler 345100900 = Kombo-Zähler 20, 1b56 111 Byte; Screenshot des Users bei 2/7 bestätigt), 3 normal, 4 normal, 5 **Fehlschlag** (nur 22 XP, kein `8456`, kein Gegenstand, Dauer 12,8 s), 6 Kombo (98 XP, Zähler 21), 7 normal. Erkennung: Kombo = Zusatzzähler 345100900 + höhere XP + größerer Gegenstands-Frame; Fehlschlag = nur `7c8d` mit kleiner XP-Zahl (22 bzw. beim Kochen 44), sonst nichts. XP je Ergebnis rezeptabhängig, Verhältnis hier 22 : 76 : 98 (Fehlschlag : normal : Kombo) [B: User bestätigt 1 Fehlschlag, 2 Kombos ("Procs"), 4 normal; Zuordnung 2/7 per Screenshot, übrige aus dem Strom].

### Ablauf einer Herstellung [B]
Szene Charakter A, 12:39:49, Waffenschmieden: `0,00 s` 7c8d (XP) + 7 x 8456 (Erfolgs-Zähler, +0,05 s) + 1b56 (Material); `+1,55 s` 1b56 (Ergebnis) + **6f8d** + **728d** (eigene eid). In der langen Aufnahme (Charakter A, Alchemie): 127 mal 6f8d und genau 127 mal ein 728d der **eigenen** eid mit Abstand < 0,2 s.

### 6f8d (398 Frames in den Debug-Aufnahmen, 4 in den Dumps)
- Aufbau [B] (Längenformel trifft in allen Frames): `6f8d | 00 05 | varint K | Rezept-Id u32 | u16 (meist 01 00, auch 00 00, 0d 00, 05 00) | n u8 | n x (Art u16 | Wert u32 | Wert u32) | 28 Byte 00`.
  - **K** = dasselbe varint wie das B-Feld im gleichzeitigen eigenen 728d (127 von 127), z. B. 26577 (`d1cf01`) bei Alchemie, 0 bei Waffenschmieden.
  - **Rezept-Id** u32 steht in der Tabelle **CraftRecipe** (z. B. 314016004 `Recipe_Alchemy_L_ub_Potion_Common_004` -> `Potion_HP_Instant_A_c_01b_f`; 311055001, 315016003, 115016001) [B].
  - Eintrag: Art u16 (1-7) und zweimal derselbe Wert u32 (Beispiele 112, 284, 116, 168, 122, 268, 400, 56, 84, 129). Je Rezeptfamilie gibt es feste Wertpaare je Art (z. B. Alchemie-Tränke: Art 6 -> 112, Art 5 -> 168, Art 3 -> 268 oder 284, Art 1/2 -> 400); pro Herstellung kommt eine **zufällige Teilmenge** (n = 1 bis 12) [B, Statistik]. Bedeutung (Nebenprodukt? Proc? Kombo?) [?]; nicht mit der Produktmenge aus 1b56 in Deckung zu bringen.

### 728d (11.625 Frames Debug, 14.345 mit Dumps) [B/H]
- Aufbau: `728d | eid varint A | varint B | u8 C` (6-8 Byte). A ist eine **Spieler-eid** (Aufnahme 90 min: 1.644 mal per 4536 angekündigter Spieler, 127 mal eigene eid 3336, nie NPC; Dumps: 1.393 Spieler, 1.326 ohne Spawn davor, nie NPC) [B].
- **C = Berufs-Id** [B für den eigenen Charakter]: Alchemie -> C = 4 (127 von 127), Waffenschmieden -> C = 1 (zugleich 7c8d mit Beruf 01). Verteilung aller Frames: C=4 7.804, 3 3.976 (Schmuck), 5 1.304 (Kochen), 1 710 (Waffen), 2 551 (Rüstung). Das stützt die Notiz "Alchemie vermutlich 4" [für Fremde H].
- **B** ist je Beruf eine feste Zahl: Alchemie 26577, Schmuck 28199, Kochen 37604, Rüstung 23215; daneben 0 bei allen Berufen [H] Kennung (Werkzeug/Effekt/Werkstatt?). Nicht in Client-Tabellen.
- Fremde Spieler senden 728d periodisch alle 5-10 s je Spieler (gleiche A-Werte wiederholen sich): sie stellen laufend her. "1,5 s nach dem Herstellen" gilt also nur für die eigene eid; die Masse der 728d sind Herstellungsanzeigen fremder Spieler in der Stadt [B].

### 8456 (20.859 Frames Debug) [B]
- Aufbau: `8456 | n u8 | n x 17-Byte-Satz`, Länge `3 + 17n` in allen Frames (n = 0: `845600`, 91 Frames). Satz: **Erfolgs-Id u32** (Tabelle Achievement/AchievementStep, 20.768 von 20.768 ersten Sätzen gefunden) | **Zähler u32** (aktueller Fortschritt) | **u64 Serverzeit ms** | **u8 Stufenbyte** (Zahl erreichter Stufen laut Server; weicht gelegentlich um 1-2 von der aus AchievementStep berechneten Zahl ab) [H]. Beispiel `845601 a8cf9114 0a010000 75765868a1010000 05` = Erfolg 345101608, Zähler 266 (Typ CraftCategorySuccess).
- **Das ist der Erfolge-Live-Frame, also auch der Ort der Saisonmissionen.** Verteilung nach Achievement.ObjType: KillNpcAll 10.230, KillNpcCreatureType 3.113, BattlePassExp 1.898, CraftSuccess 1.445, DecomposeItem 914, GatherSuccess 620, ElapsedTime 416 usw. Die Saisonmission-Gruppen heißen `Global_BattlePass_Mission_Light_S1_1` und `Global_EventPass_Mission_Light_S1_1`.
- Saisonmission (Beispiel Herstellen): zwei Sätze je Herstellung, `910430900` (BattlePass) und `910431800` (EventPass): Zähler **1** (Stufenbyte 0), gleich danach **0** (Zurücksetzen); 722 mal. KillNpcAll-Missionen 910430700/910431600 laufen bis 10 (`Count` 10). `810433000` (BattlePassExp): Zähler = Season-Pass-EXP (z. B. 69.880, Stufenbyte 41; 101 Stufen, 1.500 je Stufe am Anfang).
- **Versorgungsgesuch-Abgaben:** `PeriodCollectionTask` (300100500, Gruppe Growth_Accomplish) zählt abgegebene Gesuche (8 -> 9) im selben Moment wie 3ee2 (12:32:31, 12:32:34); `PeriodCollectionSupply` 300100700 (Qualität Rare) und 300100900 (Rare, Ausrüstung) zählen abgegebene Gegenstände (8, 9, 10 bei den Abgaben 12:33:12/12:33:15); `GetItem` 230111500 ("AbyssPoint") zählt gewonnene Abyss-Punkte (17.000 -> 17.980) [B]. Das ist der 20-Byte-Frame mit hochzählendem Zähler aus der Notiz.
- Weitere belegte Erfolge: LoginCharacter (301100200: 45, 135), ElapsedTime (301100300: 1.498 -> 1.501 im Minutentakt).

## 5. Quests: 118d, 208d, 108d, 0f8d, 918d, 928d, 418d

### Instanznummer
Jede angenommene Quest bekommt eine **Instanznummer** (Zähler: Charakter A 404 bis 433, Charakter B 212-218, 89-104). Sie steht in 118d (Byte 14), 208d (Byte 13), 108d (Byte 2) und 0f8d (Byte 4) [B].

### 118d (62 Frames; 24 Byte oder 95-100 Byte)
- `118d | Quest-Id u32 | 02 01 | 6 x 00 | Instanznummer u32 | ...`. Die Quest-Id steht in der Tabelle Quest (`UQ1302016` usw.). Kurzform (24 Byte) bei EQ/HQ/DQ-Quests (z. B. 1810011, 1102240, 1202021), Langform (95-100 Byte) bei Pflichtmissionen `UQ13xxxxx` (Typ `DutyMission`): dahinter `00 00 00 00 00 00 00 05` und eine Belohnungsvorschau (u32 Item, u64 Menge, ...; z. B. gebundenes Kinah 930100001 mit 12.016) [H, teilweise entschlüsselt].
- Auch durch eine Schriftrolle auslösbar: 1356 (Item `QuestScroll_Duty_L1_Verteron_01_03a_b` u. ä.) + 1b56 + 118d im selben Moment (12 Benutzungen dieser Schriftrolle in den Daten).

### 208d (10 Frames, 17 Byte)
`208d 01 00 00 | X u32 | Quest-Id u32 | Instanznummer u32`. X steigt im Lauf eines Tages (939, 933, 941, 963, 966, 979, 987, 999, 996, 1014): fortlaufende Angebots-/Listennummer der Pflichtmissionen [H], nicht in Client-Tabellen.

### 108d (1.055 Frames, 14 Byte, selten 15-18) [B, korrigiert]
- Aufbau: `108d | Instanznummer u32 | Zustand u8 | Schritt u8 | 6 Byte Zielzähler`. Zustand 2 = läuft, 3 = fertig (59 Frames mit Zustand 3). Schritt = aktueller Quest-Schritt (`QuestStep.Order`): bei Pflichtquests immer 1, bei Story-Quests 2, 3, 4, 5.
- **Zielzähler Byte 8..13 = Zähler des 1. bis 6. Ziels** in `GoalList`-Reihenfolge (belegt Byte 8, 9, 10). Beweis (Charakter A, Pflichtquests, Tabelle Quest/QuestStep):
  - UQ1302170 (Instanz 408): Ziele `UseEnvObj x5` und `KillNpc x20`: Byte 9 läuft 1..20, danach Byte 8 1..5, dann Zustand 3 (22:26:41 bis 22:28:24).
  - UQ1302110 (405) `KillNpc x20`: Byte 8 läuft 1..20, dann Zustand 3.
  - UQ1302124 (407) `KillNpc x25`: Byte 8 läuft 1..25.
  - UQ1302016 (404) `ClearMapEvent x1`: Zustand 3 sofort mit Byte 8 = 1.
  - 43 von 59 Fertigmeldungen werden von 0f8d gleicher Instanz gefolgt (0,1 s bis 130 s; der Rest liegt vor Beginn der Aufnahme oder ist abgebrochen).
- Prüfung der Notiz "Mission-ID u16, 00 00, Zustand u8 (2 laufend, 3 fertig), 01, Stufe u8, Zähler u8": Zustand (Byte 6) stimmt. Die ID ist aber eine **u32-Instanznummer** (keine Saisonmission-Id), Byte 7 ist der **Schritt** (nicht fest 01) und Byte 8/9 sind **Ziel 1/Ziel 2**, keine "Stufe/Zähler". Es ist die Live-Anzeige von **Pflichtmissionen und Journal-Quests**, **nicht** der Saisonmissionen im Event-Tab; deren Live-Zähler sind **8456** (s. o.).
- Längere Formen (17 Byte, 10 Frames, Instanz 8 = EQ-Quest): zusätzliche varint-Zähler [?].

### 0f8d (46 Frames, 9-25 Byte) [B/H]
- `0f8d | 00 00 | Instanznummer u32 | n u8 | n x u32` (9 + 4n Byte). Kommt 0,0-0,1 s nach der Fertigmeldung 108d (Zustand 3) und zusammen mit einem 1b56 Art 2, das einen Quest-Gegenstand (Items 5319000xx, 5325500xx) verbraucht: **Quest abgegeben (Turn-in)**.
- Anhang: erster Eintrag immer 0, weitere aufsteigend aus {1, 2, 3, 4} ([0], [0,1], [0,2], [0,1,3], [0,1,2,3], [0,2,4], ...). Bedeutung [?] (kein Bezug zur Zielanzahl: Quest mit 2 Zielen [0,1], Quest mit 1 Ziel mal [0], mal [0,1]; möglicherweise gewählte Belohnungsplätze).
- Pflicht-Abgaben bei Charakter A (22:28 bis 22:39): 4 von 5 mit [0,1], 1 mit [0].

### 928d (42 Dump-Frames, 44 in den Debug-Fenstern) [H]
- `928d | eid varint | Zustand u8` (4-6 Byte): 5 Byte = eid 2 Byte + Zustand (1, 2, 0x18, 0x19), 6 Byte = eid 3 Byte + Zustand 06, 4 Byte = eid 1 Byte. Die eid ist eine **Spieler-eid** (28 von 42 per 4536 bekannt, 1 NPC, 13 unbekannt). Kommt in Wellen zusammen mit 528d/218d derselben eid, wenn Spieler in Sichtweite erscheinen (Szene Hauptstadt 22:46:50: `928d fff001 06`, 0,2 s später `528d fff001` und `218d fff001 00 01`); 0x18/0x19 bei Gruppen von 8 Spielern gleichzeitig, Wechsel 01 <-> 18 beim selben Spieler. **Kein Auftrags-Fortschritt** (Verdacht der Notiz nicht gestützt) [H].

### 918d (11 Dump-, 9 Debug-Frames) [?]
- `918d | u32 | 00 00` (8 Byte), Werte 1.520.071 bis 1.726.921. Weder Kinah (kein Treffer in 1b56) noch eid noch Kampfkraft (die ist 95-140 Tsd.). Kein Bezug zu Kills erkennbar (Fenster zeigen nur 218d/6d8d/0390 in der Nähe). Offen.

### 418d (96 Debug-, 131 Dump-Frames) [?]
- Immer exakt `418d 0000`. Abstände unregelmäßig: 51 s bis 70 min, teils 1-2 s hintereinander (3 in 3 s um 20:29:05-08). Nur 1 von 86 Fällen liegt neben einem Zonenframe 2136, also nicht einfach "Zonenwechsel"; teils kurz nach Roster-/Spawn-Frames, teils mitten in 1b56-Salven. **"Remote-Lager öffnen" bleibt unbelegt** (Notiz sagt "verworfen") [?].

## 6. Abgleich der Notizen: was stimmt, was nicht

- `reference_aion2_inventory_storage_frames` (1b56): unverändert; 1356/1556 teilen den uid/Item-Block von 1b56 (Abschnitt 2).
- `reference_aion2_shop_purchase_counters`: 8f56 stimmt (ID, Anzahl, Reset); **8c56 ist nicht "Echo", sondern die Kaufmenge**; 8e56 hat jetzt 7 Einträge (neu 100141); 8e56 mit n=1 ist ebenfalls ein Update-Frame. Reset Mi 16:00 KST = 28.10. 08:00 MEZ (Zeitumstellung am 25.10.). 1223/1224: Waren des Aussehens-Shops (8c56 ohne Zähler).
- `reference_aion2_crafting_skill_frames`: 7c8d (19 Byte) und 7d8d bestätigt; XP-Maxima stehen in **CraftMastery**.
- `reference_aion2_quest_accept_frames`: 118d + 208d stimmt; der "laufende Zähler" ist die Instanznummer; **Kill-Fortschritt x/20 ist 108d, nicht 918d/928d**; 0f8d ist der Abschluss (Turn-in), nicht ein "Instanz-Zähler".
- Beute-Layout (Fork-Analyse, `Aion2Loot.cs`): 1e56-/1f56-Offsets stimmen; **die Schlussbyte-Deutung von 1f56 ist umgekehrt** (1 = aufgenommen).
- Hinweise für einen Einbau (ohne Änderung): 1) `Aion2Loot.NoteGroundGone`: Bedingung prüfen (`frame[^1] == 1`); 2) 108d als Quest-Fortschritt nutzbar (Instanznummer aus 208d/118d, Zielzahlen aus QuestStep.GoalList); 3) 8456 als Live-Quelle für Saisonmission- und Versorgungsgesuch-Zähler (Erfolgs-Id + Stufentabelle aus AchievementStep).

## Nachtrag 23.10.: Journal, Pflichten, Aufträge
- Im Journal, Reiter Pflichten, Untertab Aufträge, steht "Laufende Pflicht-Quests 7/20". Die 20 ist also das Limit gleichzeitig laufender Pflicht-Quests (Abyss-Aufträge aus Schriftrollen), kein Shop-Kaufzähler.
- Ein Auftrag zeigt je Ziel einen Zähler (z. B. 3/3 und 12/15, 0/5 und 0/25), Basisbelohnung (Kinah, Abyss-Punkte) und "Zufällige Belohnungen 1-5". Die Zielzähler entsprechen den bis zu 6 Zählern in `0x108d`.
- Beim Öffnen des Journals kommt `0x1c8d` (1.411 Byte): Kopf mit zwei u32 (0, 0), danach Anzahl 15 und 15 Einträge zu je 96 Byte, je Eintrag die Quest-ID (UQ13xxxxx, z. B. 1302123) und Belohnungsvorschau. Das sind die angebotenen Pflichtmissionen, nicht die 7 angenommenen Aufträge [H, Aufbau grob].
- **Nachweis an den Mitschnitten (22.-23.10., Aufträge/DutyScroll):** Die 20 Auftrags-Schriftrollen vom Abyss-Händler wurden 23.10. 00:33-00:36 angenommen (20 x `0x118d`, Instanznummern 414-433, Quest-IDs 1309012-1309036, Typ `DutyScroll`). Abgaben (`0x0f8d` mit denselben Instanznummern): 6 um 00:59, 7 um 11:07 = 13 abgegeben; offen blieben die Instanzen 421, 423, 424, 425, 426, 428, 431 = 7. Das deckt sich mit der Anzeige des Users (13 von 20 erledigt, "Laufende Pflicht-Quests 7/20"). Die Anzeige "7/20" ist also: laufende Pflicht-Quests von höchstens 20 gleichzeitig [B]. Instanznummern werden wiederverwendet (414-425 kamen schon am Abend davor mit 12 Aufträgen, die bis 00:32 alle abgegeben wurden), eine Quest ist daher nur über (Annahmezeit, Instanznummer) eindeutig.
- **`0x1c8d` beim Journal-Öffnen (Bestätigung des Users):** Es ist die Liste der angebotenen Pflichtmissionen (15 Einträge, daraus wählt man täglich 5). Die Belohnung lässt sich gegen Kinah neu auswürfeln; am Ende bekommt man eine zufällige Menge der angezeigten Belohnungen (1/5 bis 5/5). Eintrag: Quest-ID, danach Belohnungssätze (Währungs-/Gegenstands-ID, z. B. 930100001 gebundenes Kinah, 930100023, Gegenstände 610xxxxxx, dazu Werte wie 100.000.000 / 25.000.000 / 33.000.000, vermutlich Gewichte/Wahrscheinlichkeiten in 1e-8) [H, Aufbau grob].
- **Anhang von `0x0f8d` (Abgabe) = vermutlich die erhaltenen Belohnungsplätze:** 23.10. 11:07: Abgaben mit [0,2], [0,3], [0,1,2], [0,1,3], [0,1,2,3]; der erste Eintrag war in allen Beobachtungen 0, die weiteren aufsteigend aus {1..4}. Passt zu "x von 5 Belohnungen" [H, nicht gegen Inventar belegt, 1b56 verschmilzt gestapelte Gegenstände].
- Die Pflicht-Aufträge mit offenen/abgegebenen Quests sind in der Checkliste bereits umgesetzt; die Frames hier ergänzen nur die Auswertung.
- **Reroll der Auftrags-Rolle = `0x148d` [H, stark]:** Beim Kauf/Auswürfeln der Aufträge am Abend des 22.10. (22:13-22:15, 12 Annahmen) kamen 28+ Frames `0x148d` (96-101 Byte) im Abstand von ca. 1-2 s. Aufbau: `148d | 00 00 | Quest-ID u32 (UQ13025xx) | 02 01 00 00 ... | Belohnungsvorschau` (wie die Einträge in `0x1c8d`). Das Annehmen kommt danach als `0x1356` (Rolle benutzt) + `0x118d` im selben Moment (Abstand 3-8 s zwischen Annahmen). Die angenommene Quest-ID war meist eine der zuvor gewürfelten, aber nicht immer die zuletzt gewürfelte; ob man aus mehreren Würfen wählt oder die Anzeige vorher wechselt, ist offen. Der Kinah-Abzug je Wurf ist nicht gesondert gesucht worden. User-Auskunft: Eine Quest kann auch genau so angenommen werden, wie sie vorgeschlagen wurde (ohne Reroll). Das erklärt Annahmen ohne `0x148d` unmittelbar davor (z. B. 22:14:19-22:14:43, 3-8 s Abstand); der erste Vorschlag einer Rolle kommt demnach nicht als `0x148d`, sondern nur ein Reroll [H].
- Ohne eigenen Frame zeigte der Zeitraum 00:33-00:36 am 23.10. nur Annahmen; die Würfe dazu stehen in der Aufnahme `aion2-2026-10-22_23-27-08-Aahz`.
- **Regeln der Auftrags-Rolle (User, 23.10.):** Jede Rolle bietet einen Vorschlag mit 5 zufälligen Belohnungen. Bei Nichtgefallen kann man 10-15 mal neu würfeln (Kinah), nicht zurück, annehmbar ist nur der aktuell angezeigte Vorschlag (vor dem ersten Wurf oder nach einem Wurf). Bei Abgabe erhält man 1 bis 5 der 5 Belohnungen; der Anhang von `0x0f8d` ([0], [0,2], [0,1,3] ...) sind vermutlich die Indizes der erhaltenen Plätze 0-4 [H]. Offen: warum die angenommene Quest in `0x118d` nicht immer der letzte `0x148d` davor ist (Zeitangaben dort sind Sekunden nach 22:14 am 22.10.).
