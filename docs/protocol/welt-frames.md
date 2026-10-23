# Welt- und Kampf-nahe Frames: Skill-Ende, Ziel, Stagger, Serveruhr (Auswertung vom 23.10.2026)

> Von einem Auswerte-Agenten erstellt (185 Login-Dumps, 31 Debug-Aufnahmen, zwei Aufnahmen vom 23.10.2026), Stichproben vom Hauptentwickler nachgeprüft (Serveruhr-Takt 10,0 s, Aufladen-Frames). "[B]" = belegt, "[H]" = Hypothese, "[?]" = unbekannt. Ergänzt [combat-frames.md](combat-frames.md) und [skill-frames.md](skill-frames.md).


Datenbasis: 218 Quellen = 185 Login-Dumps, 31 Debug-Aufnahmen (13.-23.10.2026), cur5, cur7; Frames mit `Tools/recording-py/frames.py` rekonstruiert. Skripte `c1*.py` im Scratchpad (alle `python3 -I`, nur gelesen).
Notation: Opcode = Hex der ersten 2 Frame-Bytes. `v` = Varint (LEB128), `u32/u16/f32` little endian. [B] belegt, [H] Hypothese, [?] unbekannt.
Gesamtzahlen (alle Quellen): 0638 350.150 (in der Notiz 16.842 war nur der Dump-Teil), 3b38 59.129, 3d38 59.095, 3438 7.823, 3538 16.003, 05e0 32.671, 4637 1.405, 0336 9.616, 4838 6.426, 1b38 146, 1e38 142.

## Kurzfassung

| Opcode | Ergebnis | Stufe |
|---|---|---|
| 0638 | Skill-Ende: `Akteur v | Skill u32 | Seq u8 | Endcode u8`; Seq = Seq-Byte des zugehörigen 0238 (99,9 % Treffer) | [B] Aufbau, [H] Endcodes |
| 3b38 / 3d38 | **NICHT "Ziel verloren"**: Begleiter der eigenen Skill-Quittung 0138 (Eingabe-/Quittungstakt), Nutzlast u16 | [B] widerlegt/Zuordnung, [?] Zweck |
| 3438 | `00 | u16 Art | Ziel v`: Server bestätigt Zielwahl (Ziel 0 = Abwahl/Ziel tot) | [B] Aufbau, [?] Art-Wert |
| 3538 | `Entität v | 00 | Ziel v`: Ziel **des** gewählten Ziels (Ziel-des-Ziels), 0 = keins/tot | [B] (2 Ereignisse im Detail + Statistik), [H] Rest |
| 05e0 | Stagger-Balken, Aufbau der Doku stimmt; Zusatz: Bruch-Frame `Entität | 00 03 02`, Auffüllung 5,15 s nach dem Bruch | [B] |
| 4637 | `Entität v | f32 z`, z in derselben Einheit wie die z-Koordinate in 3c37 | [B] Aufbau, [H] Auslöser |
| 0336 | Takt alle **10,0 s** (nicht 9): `00 00 | u32 Server-Millisekunden-Zähler | u32 (3 oder 4) | u64 Serveruhr (UTC-ms)` | [B] |
| 4838 | NPC-Bodenfläche (Kreis-Boden-Skills): `Akteur v | Skill u32 | Runde u8 | 01 | 00 00 00 | Index u8 | 00 | x,y,z,Winkel f32 | u16 ms` | [B] Aufbau, [H] Einheiten/Bedeutung der Felder |
| 1b38 | Aufladen beginnt (Quittung `u16 | Skill u32`) | [B] |
| 1e38 | Aufladen losgelassen: `01 | 00 00 | Skill | Stufe u32 | Skill+Stufe u32` | [B] |
| 3600, 0001, 0002, 0004, 0010 | **keine echten Opcodes**: Schnitt-Artefakte der Rekonstruktion nach Lücken im TCP-Strom | [B] (26 von 26) |

---

## 1. 0638 Skill-Ende [B]

Aufbau: `0638 | Akteur (v) | Skill (u32) | Seq (u8) | Endcode (u8)`; Länge 9/10/11 Byte je nach Varint-Länge des Akteurs (1/2/3 Byte). Alle 350.150 Frames parsen exakt (Rest immer 2 Byte).

Beleg Zuordnung zu 0238: 0238 hat `Akteur v | 00 | Skill u32 | Seq u8 | Art u8 | ...` (Seq steht bei 7+n, n = Varint-Länge). Join (Akteur, Skill, Seq) 0638 -> 0238 derselben Quelle: **349.796 von 350.150 (99,9 %)**.
Beispiel (cur5, eigener Akteur): `0238 a40e 00 29a5ad00 03 00 ...` und gleichzeitig `0638 a40e 29a5ad00 03 00`; `0238 a40e 00 d85fa900 04 02 ...` -> `0638 a40e d85fa900 04 0c` 0,5 s später.

Zeitabstand 0238 -> 0638:
- Endcode 0, Skill-Typ `System`/Item (269.785 von 324.698 Frames, NPC-Auto-Angriffe wie `SkillString_NPC_Attack`): Abstand 0 s (gleiches Segment). Aktive Skills: pro Skill sehr enge Dauer (Median je Skill, z. B. 11170350: 0,45 s [0,34-0,55]; 11440350: 0,25 s [0,19-0,35]; NPC 1802012: 0,48-0,51 s; Spieler-Skills mit CastingTime 0 trotzdem 0,25-0,6 s). [B]: **0638 kommt am Ende der Skill-Animation/Wirkzeit** (nicht beim Cast); Dauer = skillabhängig, nicht aus Skill.json (CastingTime, NeedCoolTime) ableitbar.
- Endcode 12 (0x0c): 22.294 Frames, fast nur Spieler (22.184 von 22.294), Abstand zum Ende deutlich kürzer als der Normalwert desselben Skills (z. B. 11020140: normal Median 0,35 s, mit 0x0c Median 0,10 s) und in 15.170 von 22.294 Fällen startet im selben 50-ms-Takt (<=0,06 s) der nächste Cast desselben Akteurs (Seq+1). -> [H] 0x0c = vorzeitig beendet, weil der Folge-Skill / Kettenskill / die nächste Eingabe übernimmt. Kettenskills: 11020140 hat 1473 mal 0x0c gegen 565 normale Enden.
- Endcode 2 (1.999): 1.966 von 1.974 aktiven Skills sind NPC-Skills, 1.889 ohne Folge-Cast; Abstand Median 0,65 s -> [H] 0x02 = Monster-Skill abgebrochen (Betäubung/Stagger/Tod o. ä.), nicht geprüft.
- Endcode 3 (523): gemischt (304 NPC, 219 Spieler), Median 0,05 s zum Folge-Cast -> [H] Abbruch durch Bewegung/Ausweichen o. ä., offen.
- Endcode 15 (0x0f, 329): fast nur Spieler (324), Median 0,05 s; sieht aus wie 0x0c + 0x03 (Bitkombination 12|3) -> [H] Bit-Maske (0x01/0x02 = ?, 0x0c = Folgeskill), nicht bewiesen.
- Seq: pro Akteur hochzählend (wie 0238), nicht pro Skill.

Der Client führt 0638 als "skill-end" in `ProtocolHealth.cs`; der alte Verdacht "0638 = DoT" ist damit endgültig erledigt (DoT = 0538).

## 2. 3b38 und 3d38 [B] Zuordnung, [?] Zweck

Verdacht "Ziel verloren/abgewählt" ist **widerlegt**:
- Beide haben 4 Byte, Nutzlast `u16`: 3d38 immer `0000` (59.095); 3b38 `0000` (59.096), `0100` (33).
- Zeitliche Kopplung mit der eigenen Skill-Quittung 0138: 3b38 liegt bei **76 %** innerhalb 0,3 s um eine angenommene 0138, davon 38.372 im selben 50-ms-Takt (Abweichung 0,0 s); 3d38 liegt 0,0-0,1 s dahinter (3d38 kommt nach 3b38 in 97 % binnen 0,1 s; 13.776 Paare im selben Takt). 59.129 3b38 vs 43.133 0138.
- Im selben Takt wie 3b38 stehen 0138 mit Status `000000` (37.535) **und** mit Ablehnungscodes (`001318`, `000418`, `001e18`, `004918` ... zusammen ~1.000), 3b38 hängt also an jedem Skill-Einsatzversuch, nicht am Erfolg.
- Beispiel (cur5): `3b38 0000`, `0138 000000 d85fa900`, 0,05 s später `3d38 0000`; zehnmal in Folge bei jedem Skill der Rotation. Vor dem ersten eigenen Skill (cur5 vor 10:56:19) kommt kein einziges Paar.
- Gegenprobe Zielwahl: beim Tab-Anwählen (10:56:25.178) und beim Abwählen durch Tod des Mobs (10:56:31.228) kommt kein 3b38/3d38; dort kommen 3438/3538/3138.
- 3b38 mit `0100` (33 Frames, 7 Quellen): 7 der 33 stehen im selben Takt wie ein **abgelehntes** 0138 (`00 04 18`, `00 19 18`), die übrigen ohne 0138 im selben Takt. [H] u16 = Status (0 ok, 1 = Besonderheit/Fehler), nicht bewiesen.
- Zwei ungeprüfte Zusatzbefunde: 4.487 von 59.129 3b38 haben keinen 0138 innerhalb 1 s (z. B. Eingaben ohne Quittung); der Zweck (Eingabesperre/globaler Abklingzähler?) bleibt [?].
Die Beobachtung in skill-frames.md ("alle 50 ms am Ende von cur3") ist durch die schnelle Skill-Rotation erklärt, nicht durch einen Dauer-Takt.

## 3. 3438 und 3538 (Zielwahl) [B] Aufbau

**3438**: `00 | u16 Art | Ziel (v)`; Längen 6/7/8 Byte (Ziel mit 1/2/3 Byte-Varint; Ziel 0 = ein Byte `00`).
- Normalfall (7.789 von 7.823): Art `0000`. Seltene Arten: `3a17` (22 Frames, immer mit Ziel 0 = Abwahl), `3c28` (8) und `3c29` (4) (mit Ziel = Auswahl), nur 21./22.10., jeweils dieselben Ereignisse in mehreren Dumps (Beispiel `3438 00 283c d9ee01` gefolgt von `3438 00 173a 00`). [H] Art unterscheidet die Auswahlquelle (Tab/Klick vs. automatisch/Skill) oder ist ein Skill-/Stat-Wert (15400/14871 stehen in keiner eindeutigen Tabelle); [?].
- Beziehung zu 3138 (eigenes Ziel gewechselt) und 3538 (je Ereignis, +-1 s): Ziel != 0: 3009 von 4.531 haben 3138 und 3538 mit derselben ID, 1.168 nur 3538 (Kamera ohne 3138-Treffer), 348 3138 mit anderer ID. Ziel = 0 (3.289): 2.102 mit 3138 Ziel 0.
- Beispiel (cur5): `3438 000000 ebb502` (Anwahl), `3138 0000 ebb502 0000` 1,05 s später (nach dem ersten Skill auf das Ziel), Abwahl `3438 00 00 00 00` 50 ms nach dem Tod des Ziels.

**3538**: `Entität (v) | 00 | Ziel (v)` (Längen 5-9; Ziel 0 = ein Byte).
- [B] Bedeutung **Ziel des gewählten Monsters** (Ziel-des-Ziels): Beispiel cur5 Mob ebb502 (ID 39659): 25.178 `3538 ebb502 00 00` + `3438 ... ebb502` (Anwahl, Mob ohne Ziel); 26.527 der Mob wirkt `0238 ebb502 ... Ziel a40e` (die eigene Entität), 54 ms später `3538 ebb502 00 a40e` (Ziel = eigene ID 1828); 31.178 Mob stirbt (letzter Treffer 0438 hinter 30.178), `3538 ebb502 00 00` und 50 ms später `3438 00 00 00 00`.
- Verteilung des Ziel-Felds in cur5: 190 mal 0, 130 mal eigene ID, 48 mal anderer Spieler. 3538 kommt auch ohne 3438 bei Zielwechsel des Mobs.
- Ob es auch gesendet wird, wenn die Anwahl per Kamera geschah: 3538 hängt an 1.168 3438-Frames ohne 3138; sonst bleibt unklar. 3538 für nicht gewählte Entitäten: nicht beobachtet [?].
- Damit speist 3538 den Block "Ziel des Ziels"; die Quelle "letzter Cast" (0238) war nur die Annäherung.

## 4. 05e0 Stagger [B]

Layout der Doku bestätigt: 32.536 Stände `05e0 | Entität (v) | 03 01 | Maximum u32 | aktuell u32 | Byte`, 135 Brüche `05e0 | Entität (v) | 00 03 02` (8 Byte bei 3-Byte-Entität). Alle Frames parsen.
- Maximum: 1200 (31.509 von 32.536); sonst 150, 240, 105, 300, 280, 600, 120 ... (Balken je Gegner/Phase).
- Letztes Byte: `02` (32.397), `03` (115), `00` (24). `03`: einzelner Frame, in dem derselbe Stand doppelt kommt (Beispiel `0301 b0040000 5e000000 03`, danach wieder `02`, Wert unverändert) -> [H] Marker "Treffer ohne Abzug/Pause". `00` nur an einem Gegner mit wechselndem Maximum (240/480); [?].
- Beispielverlauf Boss (Dump `D:2026-10-21_20-18-00`): `(1200,24) (1200,22) ... (1200,4)` dann `05e0 ... 00 03 02` 0,55 s später, danach ab 5,15 s `(1200,1200)`: **Aufladung des Balkens 5,15 s (Median 5,148; 10-90 %: 5,10-5,15) nach dem Bruch** (123 Auswertbare). Der Frame mit aktuell = 0 wird nie gesendet; der Bruch ersetzt ihn.
- Nur der Abzug, kein Verursacher (wie in der Doku).

## 5. 4637 Höhe [B] Aufbau, [H] Auslöser

Aufbau: `4637 | Entität (v) | f32 z` (alle 1.405 haben 8 Byte = 2-Byte-Varint; kein Flag-Byte dazwischen). Beispiel: `4637 a40e 00e09e46` -> z = 20336,0.
- Einheit/Skala: Vergleich mit z aus 3c37 (`f | x | y | z | Yaw ...`) derselben Entität: bei Zeitabstand < 2 s (38 Paare) Median Abweichung 5, Quartile 4/12/44; Beispiele: 4637 z=2781 gegen 3c37 z=2785 (1,7 s Abstand), 4677 gegen 4733, 11831 gegen 11809. [B] gleiche Einheit (Spielkoordinate).
- Häufige Werte: 74 (35x), 31448/31451/31455, 31984/31993/32007 (Höhe ~31.4-32k = vermutlich Flughöhe/Zonenobergrenze), 84958, -24220 (Zonenspezifisch).
- Begleiter im selben Takt: 4437 (208), 008d (277), 4236; Entität steht bei 692 von 1.405 auch in 4437 (eigene). Es gibt auch Frames für andere Entitäten (nicht in 4437, nicht Flugenergie-Entität) -> [H] nicht nur die eigene Entität; Auslöser (Landung/Start? Zonenwechsel?) [?].

## 6. 0336 Heartbeat mit Serveruhr [B]

`0336 | 00 00 | u32 T | u32 K | u64 UhrUTC_ms` (20 Byte, alle 9.616 gleich lang).
- Takt: **10,0 s** (Median 10,00005, 10-90 %-Quantil 9,99-10,01 s; über alle Quellen), nicht 9 s.
- u64 = dieselbe Serveruhr wie `0036` (UTC-Epoch ms), Beispiel `0336 0000 632a770b 04000000 b656df67a1010000` und der nächste vorherige `0036 a656df67a1010000` (Differenz 16 ms).
- u32 T = Millisekunden-Zähler seit Serverstart (Prozess): Zuwachs je Frame 10.000 (Median; gegen Uhr 10.001, Differenzen 0/-1/-2 ms), `UhrUTC_ms - T` ist je Spielserver-Lauf konstant (10 verschiedene Werte in 214 Quellen = Neustart-Zeitpunkte der Server, z. B. 2026-09-01 09:00, 2026-09-05 08:00, 2026-10-21 03:00 UTC). Beispiel: T = 0x0b772a63 = 192.555.619 ms.
- u32 K: 3 (164 Quellen, alle Server mit Start Anfang September) bzw. 4 (50 Quellen, alle Server mit Start 21.10. 03:00 UTC). [H] Versions-/Build- oder Generationszähler des Serverprozesses (steigt mit dem Neustart nach dem Patch 21.10.); [?].
- Nutzen: Uhr-Abgleich Client <-> Server und Server-Laufzeit; Zeitzone steckt nicht drin.
- Hinweis zur Notiz `reference_aion2_server_clock_frame.md`: dort ist "Opcode 0x3600, 0e 00 36" gemeint, in unserer Schreibweise `0036` (Bytes `00 36`, 10 Byte Frame + Längenpräfix).

## 7. 4838 NPC-Bodenfläche [B] Aufbau

`4838 | Akteur (v) | Skill (u32) | Runde (u8) | 01 | 00 00 00 | Index (u8) | 00 | x f32 | y f32 | z f32 | Winkel f32 | u16`; Rest nach dem Akteur immer 29 Byte (alle 6.426 Frames parsen, 20 Quellen; in den Dumps nur 5, in den Debug-Aufnahmen 15).
- Skill-IDs sind NPC-Skills mit `SkillGroundType = Location`, `SkillGroundShapeType = Circle` (Skill.json) bei **allen** geprüften Top-15 (1226350 `SkillString_NPC_Attack` 4.196x, 1800142 718x, 1601000 261x, 1606931 210x ...). [B]
- Gleichzeitig mit dem 0238 desselben (Akteur, Skill): 4110 von 4110 im selben Takt (Dump `D:2026-10-21_20-18-00`). Mehrere Frames je Cast = Index 1..n (Anzahl der Flächen); Runde 1..5 zählt Wiederholungen derselben Fläche-Folge (Runde 1: 5.505, 2: 549, 3: 212, 4: 92, 5: 68).
- Beispiel: `4838 a19601 6eb61200 01 01 000000 01 00 5fc03a46 a080a345 00f02d45 8c4dfa42 2e09` = Akteur 19233, Skill 1226350, x 12208, y 5200, z 2814, Winkel 125,15, u16 = 2350.
- z passt zur Spielerhöhe: im gleichen Moment eigene z (3c37) 2785, Abstand x/y ca. 1.700 -> [H] Weltkoordinate des Flächenmittelpunkts (Spielkoordinate), z-Plausibilität nur an einem Beispiel gezeigt.
- Winkel: Werte meist 0, 90, -90, 180, ±120 ... -> [H] Ausrichtung in Grad (Skills mit Kreisfläche ändern nichts); [?].
- u16: Vielfaches von 50, 100-4.700 (je Skill unterschiedlich, z. B. 1218020: immer 600; 1227150: 400/900/1400; 1226350: 2050-3950) -> [H] Verzögerung bis die Fläche auslöst (ms), je Index gestaffelt. Gegenprobe mit 0438-Treffern misslang (360 Zuordnungen, Streuung -2 bis +89 s: Zuordnung per Skill-ID zu ungenau), daher nur [H].

## 8. 1b38 / 1e38 Aufladen [B]

Beide gehören zum Aufladen von Fertigkeiten mit Aufladestufen (Tabelle `SkillCharge`: z. B. 14010240, Klasse 14 = Ranger-Skill, Stufen bei 350/700/1050 ms mit Skill-IDs 14010241/42/43, `ChargeMinTime` 210 ms).
- `1b38 | u16 Status | Skill u32` (8 Byte, 146 Frames): Quittung "Aufladen beginnt"; Status `0000` (145), einmal `04 18` (Ablehnung im Stil der 0138-Codes `cc 18`). Skills: 14010240 (139), 17060340 (4), 14010030 (3). [B]
- `1e38 | 01 | 00 00 | Skill u32 | Stufe u32 | Skill+Stufe u32` (17 Byte, 142 Frames): Loslassen. Beispiele `1e38 01 0000 80c7d500 00000000 80c7d500` (Stufe 0) und `... 80c7d500 01000000 81c7d500` (Stufe 1). Prüfung: `Skill2 - Skill1 == Stufe` in **142 von 142**.
- Haltedauer 1b38 -> 1e38 je Stufe: Stufe 0: 0,25-0,45 s (Median 0,30), Stufe 1: 0,30-0,56 (0,40), Stufe 2: 0,70-0,80, Stufe 3 (17060340): 0,90-0,95. Passt grob zu den Tabellenschwellen (350/700/1050 ms), die Zeitstempel haben nur 50-ms-Auflösung und die Laufzeit zum Server schiebt. [B]
- 1b38 ohne 1e38: 146 gegen 142 (Abbruch/ Loslassen ohne Auswirkung).
- Nur eigener Charakter (kein Akteur-Feld), kommt im selben Takt wie 0638/3b38/0438 des Skills.

## 9. 3600, 0001, 0002, 0004, 0010: Schnitt-Artefakte [B]

Alle 26 Vorkommen (3600: 1, 0001: 13, 0002: 2, 0004: 4, 0010: 6) liegen **ausschließlich in Login-Dumps** (14 Dateien), nie in den 31 Debug-Aufnahmen oder cur5/cur7. Jedes steht 0-55 Frames hinter einer Neu-Synchronisation von `frames.py`, die durch eine Lücke in der TCP-Sequenz (`Seq` springt, z. B. +2.784, +12.088, +23.232 Byte, Segmente kommen vertauscht) ausgelöst wird: `iter_frames` verwirft den Puffer (`buf = b''`) und beginnt mitten in einem Frame zu schneiden.
- Beispiel (Dump 046, 19:30:54.733): Segment mit Seq 3952163530 (1.452 Byte) endet, das nächste Segment hat Seq 3952177070 (+13.540 Byte voraus, das fehlende kommt erst danach, Seq 3952164982 - vertauschte Reihenfolge). Das voraneilende Segment beginnt mit `00 10 5a 16 00 03 42 00 10 2f 11 00 01 02 00 17 5d 9a ...`, das ist der Inhalt mitten in Frames, daher Pseudo-Opcodes `0010`, `0001`.
- Die "Frames" haben Längen 9-13.334 Byte und Inhalte wie `00 10 88 05 00 10 89 05 00 10 8a 05 ...` (wiederholte Muster von Folge-IDs, also viele echte kleine Frames, die zu einem verschmolzen sind). 3600 `3600 01 7f` (4 Byte, Dump 184) steht 20 Frames hinter einer Lücke (+6.880).
- Folgerung: das sind keine Opcodes. Der Client-Decoder sollte sie ohnehin ignorieren; wer sie auswertet, ist beim Resync-Fehler. Vorschlag (nicht umgesetzt): in `frames.py` nach einem Reset die Bytes verwerfen, bis ein plausibler Opcode (aus der bekannten Liste) und eine zum Frame passende Länge kommt.
- Die Zahl `3600` in der Serveruhr-Notiz ist der Opcode `0036` (Byte-Reihenfolge), kein eigener Opcode.

## Offen / nächste Schritte
1. 3b38/3d38: Zweck. Idee: Aufnahme mit Eingabe-Takt (Skill gedrückt halten ohne Ziel) und Vergleich mit den Client-Eingaben; Nutzlast `0100` einzeln klären.
2. 0638-Endcodes 2, 3, 15 gegen echte Ereignisse (Betäubung, Tod, Bewegung) testen.
3. 3438-Art-Wert `3a17/3c28/3c29`: Auswahlquelle klären (Aufnahme mit Tab, Klick, Assist, Kamera).
4. 4838: u16 und Winkel gegen die Fläche in der Replay-Karte prüfen; Auslösezeit gegen den Schaden-Frame mit exakter 0438-Zuordnung (Feld-Layout von 0438 erst sauber entschlüsseln).
5. 4637: Auslöser (Landung/Start?) und ob nur eigene Entität.
6. 0336: Bedeutung der Konstante 3/4.
