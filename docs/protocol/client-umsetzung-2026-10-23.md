# Im Client umgesetzte Frames und Befunde (Sitzung 23.10.2026)

Ergänzt [login-und-fenster-frames.md](login-und-fenster-frames.md), [skill-frames.md](skill-frames.md) und [bewegungs-frames.md](bewegungs-frames.md). Opcode = die Zahl, die der Decoder liest (`0x7e8d`); die Bytes im Frame stehen vertauscht (`8d 7e`). Alle Aufbauten wurden mit dem Testmodus `aion2-frame-dump <aufnahme> <opcode>` an echten Aufnahmen geprüft (Tabelle am Ende). **Belegt** = an einem echten Frame gelesen und im Selbsttest mit echten Bytes abgesichert.

## Berichtigungen zu älteren Notizen

- **Legionsliste `0x058a` enthält alle Stufen**, nicht nur Stufe 45 (Beispiele: Stufe 21, 22, 29 im selben Frame). Pro Mitglied steht die Stufe als Byte hinter dem Namen.
- **Gear Score und Gegenstandsstufe sind verschiedene Zahlen** (Gegenstandsstufe aus der Legionsliste, Gear Score nur aus dem Inspect-Frame). Die Legionsliste verbessert den Gear-Score-Filter also nicht; stattdessen gibt es den Filter nach Kampfkraft (NC-Daten).
- **`0x338a` (Legion eines sichtbaren Spielers):** `Charakter-ID u32 | 00 00 00 00 | Server u16 | Legionsname (Längenbyte + UTF-8) | 02 00 02 00 ...`. Gemessen an 17.514 Frames zweier Aufnahmen: 909 verschiedene IDs für 329 (Server, Legion)-Paare, jede ID hat genau einen Legionsnamen, eine Legion mehrere IDs, also **eine ID je Spieler**. Die ID (z. B. 24,9 Mio.) liegt in einem anderen Zahlenraum als die der Spielerlisten und der Legionsliste (um 200.000) und steht in der Aufnahme **in keinem anderen Frame** (27 von 28 geprüften IDs nur in `0x338a` selbst; nicht in `0x4536`, `0x058a`, `0x0297`). Einem Namen daher nicht zuordenbar; die Legion eines sichtbaren Spielers kommt ohnehin schon mit dessen Auftauch-Frame (`GuildOf`). Im Client nicht gelesen.
- **Skill-Quittung `0x0138` als Erkennung der eigenen Entität verworfen** (und die Quittung selbst nicht mehr gelesen): in der Aufnahme vom 11.10. (20:03) nannten 53 Quittungen die unbenannte Entität 15981, der Charakter-Datensatz aber Aahz (6343). Ungeklärt, ob dort ein Charakterwechsel war; bis dahin keine Erkennung darüber.
- **Boss-HP:** Client-Tabellen enthalten keine NPC-HP (nur `PcStatLevel` und vier Test-Teile in `MonsterPartsData`). Die echte HP kommt aus dem Spawn-Frame `0x4136` (seit 1.13.6, `bossSpawnMaxHp`), der Server lernt die Referenz je NPC-ID selbst.

## Legionsliste `0x058a` (belegt)

`opcode | 14 Byte Kopf | Mitglied ...`, Mitglied:

`Charakter-ID u32 | 00 00 | Server u16 | Klassencode u32 | Name | Stufe u8 | 00 00 00 | Rang u8 | [Kommentar] | Zeit u64 (ms) | Zone u32 | Gegenstandsstufe u32 | Kampfkraft u32 | 0 u32 | Flag u8 | E u32 | 0 u32`

- **Name:** Längenbyte = Anzahl **Zeichen** (UTF-8), nicht Bytes (`ö` zählt als eins).
- **Kommentar:** nur wenn vorhanden, **ohne Kennzeichen**: Längenbyte (hier in **Bytes**: 0x20 = 32 Byte für 31 Zeichen mit `ö`), der Text, dann **ein weiteres Byte**. Der Client sucht deshalb nach dem Rang die Zeit (eine Epoch-Zahl zwischen 2020 und 2030) und überspringt alles dazwischen.
- Rang: 0 Legionsmeister, 1 Eliteoffizier, 2 Legionsrekrut. Wert E weiter unbekannt.
- Aufnahme 11.10. 20:03: 19 Mitglieder gelesen (Kopfbyte nennt 0x14 = 20, ein Eintrag fehlte der Aufnahme).
- Client: setzt den Heimatserver je Mitglied (wie die Gruppenliste) und schickt Name, Klasse, Stufe, Server, Fraktion, Charakter-ID und Legionsname mit dem Profil-Upload (nur mit dem Schalter „Profile anderer Spieler“).

## Eigener Rang `0x578d` und Ranglisteneintrag (belegt)

`578d | 00 00 | Typ u8 | Stufe u8 | Eintrag | 12 Byte` (Rest: `u32 Wert`, `u64 Zeit ms`). Der Eintrag ist derselbe wie in den Listen `0x558d`:

`Rang u32 | Charakter-ID u32 | 00 00 | Server u16 | Name | Legion | 01 00 2d | 7 x 00 + Klassen-ID | Punkte u64 | Werte-JSON (Längenbyte) | Änderung i32 | 0 u32 | Rangstufe u32 | Rangstufe u32`

- Typ: 1 Abyss, 3 Albtraum, 4 Transzendenz, 5 Arena 1v1, 6 Arena 5v5, 21 Aszensusritus. Stufe: 0 gesamt, 2 eigene Klasse.
- Beispiel: Abyss, gesamt, Rang 1.764, 92.435 Punkte, Änderung −16, Rangstufe 18, Werte `{"kill_count":11,"death_count":42,"assist_count":61}`, Klassen-ID 2 (Gladiator).
- Kommt nur, wenn das Ranglistenfenster geöffnet wird (nicht beim Login). Client: Reiter „Ranglisten“ der Spielerseite (eigenes Profil, je Typ und Stufe der neueste Stand).

## Ziel-Frames `0x3238` und `0x3138` (belegt)

- `3238 | Akteur (varint) | 00 | Ziel (varint, 0 = keins)`: Ziel eines fremden Akteurs (Spieler **und** Monster). Beispiel: `3238 82 26 00 CD 67` = Akteur 4866 wählt 13261.
- `3138 | 00 00 | Ziel (varint) | 00 00`: eigenes Ziel (Akteur = eigene Entität). `3138 0000 BE7C 0000`.
- 332 und 13 Frames in der Aufnahme 11.10. 20:03. Client: Fokus je Spieler (Zeit auf dem Boss / auf anderem, Zielwechsel) mit der Zeitachse im Upload; der Ziel-des-Ziels-Block nutzt das gewählte Ziel ohne Altersgrenze.

## Skill-Quittung `0x0138` (belegt, im Client nicht mehr gelesen)

`0138 | 00 cc dd | Skill u32` (9 Byte): `00 00 00` angenommen, sonst abgelehnt (`dd` = 0x18, `cc` der Code), nur für die eigene Figur. Der Projektleiter hält abgelehnte Eingaben für irrelevant (23.10.2026): Der Client erfasst und lädt sie nicht mehr hoch, der Server kennt das Feld nicht mehr, Webseite und Dummy-Test zeigen sie nicht.

## Kettenskills `0x1838` / `0x1938` (belegt)

`1838 | von-Skill u32 | 01 | Folgeskill u32` öffnet das Fenster (3 s), `1938 | 01 | Skill u32` schließt es (benutzt oder abgelaufen). Tabelle: `Skill.json`, Feld `ChainSkillPrevSkillId`, Fenster `ChainSkillAvailableTime` (3000 ms bei 1.124 von 1.158); 1.158 Ketten → `skill_chains.json` (Client-Asset und Backend). Client: Assist-Hinweis „Kettenskill bereit“, Tooltip in der Rotation, Dummy-Test. Server: Kettenquote je Spieler/Skill (Tabelle `encounter_chain_usage`, beim Speichern der Zeitachse berechnet).

## Herstellung

- **`0x7e8d` gelernte Rezepte:** `n (varint) | n x (Rezept-ID u32 | 0 u32)`; 210 Rezepte je Fraktion in der Aufnahme (beide Fraktionen gezählt: 420 von 512 sichtbaren). Die ID ist die aus `CraftRecipe`.
- **Regel „Auto-Rezepte nach Stufe“ (belegt an der Login-Liste):** bei allen fünf Berufen endet die Liste der gelernten `Auto`-Rezepte unter der kleinsten noch nicht erreichten Stufe (Schmieden bekannt bis 35, unbekannt ab 51; Schneiderei bis 25, ab 30 unbekannt; Kochen bis 75, ab 90 unbekannt; Alchemie alle). Rezepte werden nur beim Stufenaufstieg gelernt.
- **`0x7d8d` Stufenaufstieg:** `Beruf u8 | Stufe u16 (absolut) | ...`. Beruf 1 Schmieden (Waffen), 2 Rüstung/Schneiderei, 3 Schmuck, 5 Kochen sind belegt, **4 Alchemie nur aus der Reihenfolge angenommen**. Die „Fortgeschritten“-Rezepte (Stufe 50 und 100) sind nicht Teil der Auto-Rezepte.
- Client: Filter „Nur meine Rezepte“ im Crafting-Tool (Login-Liste plus Aufstieg). Der Schalter wurde später durch die Grundregel „immer nur gelernte (und markierte) Rezepte, solange eine Login-Liste bekannt ist“ ersetzt (siehe `docs/offene_aufgaben.md` 2.0b).

## Erfolge und Wochenpunkte `0x8356`

`n (varint) | n x (ID u32 | Zähler u32 | Zeit u64 ms | Stufe u8) | Rest` (Rest bei 39.693 Byte: 130 Byte nach dem letzten Eintrag, unbekannt). Eintrag-ID `210000100` = Wochenpunkte der Saisonmissionen (Aufnahme: 8000 = Höchststand, Schwellen 1.500/3.000/4.500/6.000/8.000). Client: Zeile im Wochenteil der Checkliste; ein Stand, den das Spiel vor über einer Woche änderte, zählt als 0.

## Besitzlisten

- `0x7956` Titel: `n (varint) | n x u32 | ...` — 214 in der Aufnahme.
- `0x7256` Flügel: `n (u8) | n x (u32 | 0)` — 11.
- `0x3292` Teleporter: `n (u8) | n x u32` — 70 (alle in `TeleportArtifact`, die Tabelle hat aber 555 Zeilen: freigeschaltete Zahl ≠ Tabellenzahl, daher kein „von y“).
- Client: Zeile im Charakterfenster.

## Positionen (Messung an 46.446 Meldungen, 4 Aufnahmen)

Die eigene Position meldet der Server nur sparsam (Effekt-Frames `0x2a38`/`0x2b38` mit den letzten 12 Byte, Korrekturen `0x3c37`). Vergleich der letzten genauen Meldung mit der Mitte der Entitäten um den Spieler (Spawns `0x4136` und Bewegungsframes `0x..37`), Fehler Median/90 % in Metern:

| Alter der letzten Meldung | letzte Meldung | Mitte der in den letzten 5 s bewegten Entitäten |
|---|---|---|
| unter 1 s | 0,5 / 4,9 | 10,6 / 26,8 |
| 1 bis 2,5 s | 5,1 / 13,5 | 14,1 / 33,5 |
| 2,5 bis 5 s | 13,6 / 29,7 | 15,0 / 49,4 |
| 5 bis 15 s | 24,0 / 88,2 | 12,1 / 32,9 |
| über 15 s | 19,2 / 195,4 | 11,5 / 30,7 |

Mittelwert schlägt Median und gekappte Mitte; ältere Entitäten (60 s, 120 s) verschlechtern das Ergebnis (bis 30 m). Die frühere Notiz „9,9 m mit allen lebenden Entitäten“ ließ sich nicht reproduzieren. Regel im Client: die genaue Meldung 4 s lang, danach der Mittelwert der Entitäten, die in den letzten 5 s (sonst 20 s) erschienen oder sich bewegten. Testmodus: `aion2-position-check`.

## Umgebungsobjekte `0x3836` / `0x3a36` / `0x3b36`

Weiter nur Hypothese ("Objekt wird von einem Spieler benutzt"); Reihenfolge `3836 | Env-ID | Akteur` → `3a36` → `6d8d` → `3b36 | Env-ID`. Keine Anzeige im Client, solange die Bedeutung nicht belegt ist (Vorschlag: gezielter Test im Spiel mit Uhrzeit, dann Abgleich mit der Aufnahme).

## Aufnahme-Dateien und Testmodi

| Aufnahme | Wofür |
|---|---|
| `aion2-2026-10-11_20-03` | Legionsliste, eigener Rang, Ziel-Frames, Besitzlisten, Wochenpunkte, Rezeptliste |
| `aion2-2026-10-11_16-14`, `aion2-2026-10-09_20-01` | Boss-Effekte (Effekte auf dem Boss), Kampfpositionen |
| `aion2-alleports-2026-10-21_12-26` | enthielt keine der neuen Login-Frames |

Testmodi: `aion2-frame-dump`, `aion2-position-check`, `aion2-opcode-census`, `aion2-stock-check` (zeigt gelernte Rezepte und Besitzzahlen), `aion2-upload-dryrun` (`AION_DRYRUN_BUFFS=1`). Ab Windows-Exe aus WSL kommen Umgebungsvariablen nur mit `WSLENV=NAME` an. Der Recorder `aion2-record <datei> --points` meldet Frames mit Opcodes ohne Beschreibung (Liste der bekannten: `Client/assets/aion2/protocol/known_opcodes.json`, neu bauen mit `Tools/aion2-dat/build_known_opcodes.py`).
