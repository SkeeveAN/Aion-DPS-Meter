# Bewegungs- und Zustandsframes anderer Entitäten (Auswertung vom 23.10.2026)

> Von einem Auswerte-Agenten erstellt (Login-Dumps + 29-Minuten-Aufnahme), Stichproben vom Hauptentwickler nachgeprüft. "[B]" = belegt, "[H]" = Hypothese. Charakternamen sind durch Platzhalter ersetzt. Ergänzt [position-frames.md](position-frames.md).


Datenbasis: 185 Login-Dumps (Server->Client, ca. 370 000 Frames ohne 0036/0238/0438/2b38/1d37/1c37/2a38/0191) plus `cur3.pkl` (29 min, vollständig, 83 388 Frames).
Alle Skripte liegen im Scratchpad (`dec.py` = Referenzdecoder, `d3/d4/d8/d9.py` = Prüfungen). Notation: Opcode = erste 2 Frame-Bytes in Hex, `eid` = LEB128-Varint direkt hinter dem Opcode, `f` = erstes Byte hinter der eid ("Flag-Byte"), alle Zahlen little-endian, `pos` = 3 x f32 (x,y,z), `vel` = 3 x f32, `hd` = u16.
Sicherheit: **[B]** belegt (Statistik/Gegenprobe), **[H]** Hypothese.

## 0. Kurzfassung

1. **Die Kandidaten-Opcodes sind die Bewegungs-/Zustandsframes ANDERER Entitäten (Spieler und NPCs) und widerlegen "Gehen/Fliegen erzeugt keine Positionsframes" für fremde Entitäten.** Für die **eigene** Entität bleibt die Aussage richtig [B] (siehe 6): in 29 Minuten Herumlaufen kommen für die eigene eid (10987) nur 2237 (1x), 4736 (1x) und Effekt-/HP-Frames, nie ein Positions-/Bewegungsframe.
2. **Aufbau (alle [B], Längenregeln treffen zu 100 %, Positionen zu 100 % plausibel):** `eid | Flag-Byte f | optionale Ein-Byte-Felder | pos / Delta | Richtung(en) | Geschwindigkeit`. Das Flag-Byte ist ein Präsenzfeld: je gesetztes Bit kommt ein Byte dazu (Position je Opcode verschieden: vor oder hinter dem Datenblock).
3. **Zwei Frame-Arten, immer paarweise:**
   - **Absolut (Keyframe):** `pos` als f32 (Zentimeter, Weltkoordinaten wie Zonenframe 0x2136): 1a37, 1b37, (1c37), 1e37, 1f37, 2437, 2837, 2937, 2f37, 2e37, 3037, 2b37.
   - **Delta (10 Hz):** x/y/z als **int8-Differenz in cm** zur letzten bekannten Position: 1d37, 2037, 2537, 2a37, 2d37, 3237. Gegenprobe: Summe der int8-Deltas zwischen zwei Keyframes = Ortsänderung (Median-Restfehler 7 cm bei 144 cm Verschiebung, 205 Intervalle, `d4.py`).
4. **Einheiten [B]:** Position in cm (100 = 1 m), Geschwindigkeit in cm/s (gemeldeter vs. aus Positionen berechneter Betrag: Median-Verhältnis 1,00 über 55 730 Positionspaare), Winkel `hd` = u16, 65536 = 360 Grad, **0 = +x (Osten), wächst zu +y (Süden)** (`hd` = atan2(vy,vx) der Geschwindigkeit, Median-Abweichung 0,0 Grad bei 2937, n=299).
5. **Entity-IDs [B]:** 4136 = NPC-Spawn, 4536 = Spieler-Spawn (Name), 3436 = Umgebungsobjekt-Spawn (statisch, nie in Bewegungsframes). Neu: **4736 = Spieler-Entfernen** (Grund-Byte), 4236 = NPC-Entfernen, 3536 = Env-Entfernen. Die Bewegungsframes tragen zu 70-100 % eids, die vorher per 4536 (Spieler) oder 4136 (NPC) angekündigt wurden; Rest = vor Beginn des Dumps gespawnt.
6. **Takt [B]:** Server-Tick 100 ms. Spieler: 2937/2037/2a37/1d37 alle 0,10 s; NPC-Keyframe 1c37 genau alle 1,00 s + 1d37-Deltas alle 0,10 s; Spieler-Keyframes 1a37/1b37 etwa 1 Hz.
7. **Koordinatensystem [B]:** gleiche Welt-Koordinaten wie Zonenframe 0x2136 und NPC-Spawn 0x4136: andere Entitäten in den ersten 6 s nach einem Zonenframe liegen im Median 21 m vom eigenen Spawnpunkt (n=1191, `d7.py`); negative Koordinaten kommen in anderen Zonen vor (z. B. x=-119 099, y=-98 110).

## 1. Allgemeine Konventionen

- Frame = `opcode(2, big-endian gelesen) | eid (Varint) | Rest`. Varint-eids sind 1 bis 3 Byte lang, daher schwankt die Frame-Länge je nach eid um bis zu 2 Byte.
- `f` (Flag-Byte) steuert, welche **Ein-Byte-Felder** vorhanden sind. Die Zuordnung Bit -> Feld ist **je Opcode verschieden** (Tabellen unten). Alle Opcodes haben eine exakte Längenformel (siehe "Länge").
- **S-Byte-Familie** (1d37, 2a37, 2937, 2837, 1e37): ein Zustandsbyte (Werte 1, 2, 3, 4), das **vorn** steht, wenn `f` Bit 0 gesetzt ist, sonst **hinten**. Beobachtung bei 1d37: S = 3, wenn Bit 0 UND Bit 5 gesetzt sind, sonst 1; gleiche Regel bei 2937 (Bits 0 und 1) und 2a37 (Bits 0 und 4). Bedeutung von S offen [H: Bewegungsmodus/Anzahl Teilzustände].
- Wichtige Konstante: bei `vel`-Frames steht gelegentlich vz = **exakt 1000,0** (1e37 mit f=0x14: 3169 von 3169 Frames; f=0x04: 136 von 136) -> Platzhalter für "steigt/Sprung" [H].

## 2. Familienübersicht

| Opcode | Frames (185 Dumps) | Art | Inhalt | Entitäten | Sicherheit |
|---|---|---|---|---|---|
| 1a37 | 17 226 | absolut | pos + hd (Halt/Resync) | Spieler 9112, NPC 2683 | B |
| 1b37 | 18 165 | absolut | pos + hd (Laufrichtung) + hd2 (Blickrichtung) | Spieler, NPC | B (Layout), H (hd2 = Blick) |
| 1c37 | (nur cur3: 6527) | absolut | wie 1b37, NPC-Keyframe 1 Hz | v. a. NPC (6125 von 6527) | B Layout, H Rolle |
| 1d37 | (nur cur3: 6891) | Delta | dx,dy,dz int8 + hd + hd2 | NPC 4412, Spieler 2479 | B |
| 1e37 | 7 341 | absolut | pos, hd, vel, S | Spieler | B |
| 1f37 | 3 358 | absolut | pos, hd, vel, B | Spieler | B |
| 2437 | 562 | absolut | pos, hd, vel, B | Spieler | B |
| 2837 | 2 753 | absolut | pos, vel, hd, S (+X) | Spieler 2090, NPC 327 | B Layout, H Rolle |
| 2937 | 13 327 | absolut | pos, vel, hd, S | Spieler 10749, NPC 1094 | B |
| 2f37 | 6 909 | absolut | pos, dir (Einheitsvektor), face (f32 Winkel), B | Spieler 4247, NPC 465 | B |
| 2e37 | 2 877 | absolut | **Skill-Cast**: skillId, pos, Blickvektor, face, Wirker-eid | Spieler 2086 | B |
| 3037 | 108 | absolut | pos, hd, hd2, S (ohne Flag-Byte) | Spieler | B |
| 2b37 | 342 | absolut | Pfad: pos + n Wegpunkte (n = 3 oder 4), hd, face | Spieler | B Layout, H Rolle |
| 2037 | 19 327 | Delta | dx,dy,dz + hd + vel | Spieler | B |
| 2537 | 1 450 | Delta | dx,dy,dz + hd + vel | Spieler | B (r ca. 0,9) |
| 2a37 | 11 248 | Delta | dx,dy,dz + vel + hd | Spieler 4805, NPC 4416 | B (r ca. 0,9) |
| 2d37 | 1 982 | Delta | dx,dy,dz + hd + face (f32) | Spieler | B Layout, H Deltas |
| 3237 | 387 | Delta | dx,dy,dz + hd + hd2 (Gegenstück zu 3037) | Spieler | B |
| 2137 | 2 617 | Marker | nur eid: "Bewegung beginnt" | Spieler | B (Statistik), H Name |
| 2237 | 2 572 | Marker | nur eid: "Bewegung endet" | Spieler (auch eigene eid) | B (Statistik), H Name |
| 3c37 | 388 | absolut | Teleport/Sprung/Korrektur: pos + Yaw in GRAD (bekannt als localPosition) | alle | B |
| 4e36 | 976 | Zustand | zwei Winkel in Grad (a, b=aktuelle Ausrichtung) | Spieler | B (b), H (a) |
| 4736 | 6 787 | Entfernen | Spieler-Remove mit Grund-Byte | Spieler | B (Statistik) |
| 4437 | 282 | Zustand | nur eigene eid + 0000 (Zonen-/Ladezustand) | eigene | H |
| 5636 | 927 | kein Bewegungsframe | Login-Datensatz (siehe 4.9) | keine eids | B (kein Bewegungsframe) |
| 3436 | 9 128 | Spawn | Umgebungsobjekt (statisch) | Env | B |
| 3536 | 2 424 | Entfernen | Env-Remove | Env | B |
| 3836/3a36/3b36 | 4 561/4 542/3 177 | Ereignis | Env-Objekt wird von einem Spieler benutzt (Aktor-eid im Frame) | Env + Spieler | B Aktor=Spieler, H Art |
| 6d8d/058d | 2 662/1 026 | Ereignis | nur Env-eid (Begleitframe) | Env | H |
| 748d | 2 750 | Status | eid, 00, 05, Varint-Wert, Typ 1-5 | Spieler | H |

## 3. Detailaufbau je Opcode

Offsets zählen ab dem Byte hinter der eid. Beispiele sind echte Frames (Dumpname im Kommentar; alle über `dec.py` reproduzierbar).

### 3.1 Absolute Frames mit Geschwindigkeit

**2937** (n=13 327, Länge nach `f` immer 27, 100 % plausibel)
`f | [S] (wenn f&1) | pos(12) | vel(12) | hd(2) | [S] (wenn !(f&1))`, `f` in {01, 02, 03}.
Beispiel (f=03): `2937 8027 03 03 2aae8047 6b85af46 7399f646 | c32da7c4 6b4f3844 899eee43 | 796b` -> pos (65884, 22467, 31565), vel (-1337, 737, 477), hd = 0x6b79 = 27513 -> 151 Grad; atan2(737,-1337) = 151 Grad. [B]
Bedeutung der Flags [H, Statistik, `c8.py`]: f=02 (S hinten): Bodenlauf (Median-Tempo 822 cm/s, vz ca. 0); f=01: Gleiten/Fallen (Tempo 1569, vz -349); f=03: Flug (Tempo 1582, vz +274). Passt zum Aion-Gleiten (Gleitverhältnis ca. 4:1).

**2837** (n=2 753, 100 % Längenregel `27 + ((f>>1)&1)`)
Wie 2937, `f` in {01,03,04,05,07}: Bit 0 = S vorn, Bit 2 = Marker (04 = S hinten), Bit 1 = ein zusätzliches Byte hinten. Rolle [H]: Auslöser eines neuen Bewegungsabschnitts: Vorgänger meist 2937/1a37/2a37/1b37, Nachfolger 2937/2a37; das aus Positionen berechnete Tempo ist hier im Median 1,27 x das gemeldete (p90 = 2,87 x), bei 2937 dagegen 1,01 -> 2837 meldet die Geschwindigkeit am Beginn (Beschleunigung). 96 mal steht 2837 direkt hinter einem 2937 mit identischem pos/vel (gleicher Zustand zweimal gemeldet).

**1e37** (n=7 341, Länge nach `f`: `26 + (f&1) + (1 wenn f&0x14)`, 100 %)
`f | [A] (f&1) | pos | hd | vel | [B] (f&0x14)`. Bekannte f: 11, 14, 01, 04, 05, 19.
Beispiel: `1e37 9807 11 02 518a7b47 7caaa046 19590347 | c31d | b8022a44 184c1844 1e6b9ac1 | 01` (f=11: A=02, B=01) -> pos (64394, 20565, 33625), hd 42 Grad, vel (680, 609, -19).
A = 2 (3243 Frames) = Gehen (Tempo 553, vz -25), A = 4 (619) = schnell (Tempo 1241), B = 3 mit vz = 1000 (f=14) = Steigen [H]. Nur Spieler.

**1f37** (n=3 358, `26 + ((f>>1)&1)`, 100 %) `f | pos | hd | vel | [B=01 wenn f&2]`, f in {00, 02}.
Beispiel: `1f37 e00c 02 90227947 8ddd8946 9866f746 | 691d | 7e99c3 43 0a2fac 43 4553be c4 | 01`-> pos (63779, 17647, 31667), hd 41 Grad, vel (391, 344, -1523): fallend.

**2437** (n=562, `26 + (f&1)`, 100 %) wie 1f37, `f` in {00, 01}. Tempo 1200, vz -397 (Gleiten/Fall) [H].

**3037** (n=108) **ohne Flag-Byte**: `pos(12) | hd | hd2 | S(01)`; Beispiel: `3037 9807 b3887d47 c4b5a946 b2820247 | f0b3 | 3301 | 01`... pos (64905, 21723, 33411), hd 339, hd2 73. Wird von 3237 (Delta-Gegenstück) gefolgt, bis der nächste Keyframe kommt (Entität 920, `d2.py`).

### 3.2 Absolute Frames mit Richtung(en)

**1a37** (n=17 226): `f | A (immer vorhanden, meist 00) | pos | hd | Zusatz`. f=00 in 99 % (17 044): 15 Byte nach `f` = A + 12 + 2. Seltene Varianten: f=01 (1 Byte mehr vor pos, n=7), f=0c (+9 Byte hinten, n=160: `01 | u16 (1, 50 oder 1000) | 6 Nullbytes`, Bedeutung offen), f=02 (+18/19 Byte, nur im Dump 060-Charakter-P, 15 Frames, Instanz mit negativen Koordinaten, offen).
Beispiel: `1a37 d34d 00 00 ad1c8147 a2d6a946 00b0f546 | 0e78` -> pos (66105, 21739, 31448), hd 169. Keine Geschwindigkeit; das Heading weicht im Median 7 Grad (p75 23 Grad) von der Bewegungsrichtung ab -> Ausrichtung im Halt [H]. Zeitabstand je Entität im Median 1,1 s = 1-Hz-Resync-Keyframe. In `cur3` folgt 1a37 oft auf 2137 und geht mit 2e37 (Skill) einher.

**1b37** (n=18 165): `f | [A] (f&1) | pos | hd | hd2 | [B] (!(f&1))`, f in {01, 04, 05} (Länge 17 nach `f`, 100 %); f=03: +20 Byte (nur Dump 060, wie 1a37 f=02).
Beispiel: `1b37 a374 05 03 0ac17d47 8a33b946 21faf546 | 2ff7 | 2ff7` -> pos (64961, 23706, 31485), hd = hd2 = 348 Grad.
hd = Laufrichtung (Median-Abweichung zur Positionsänderung 0,5 Grad), hd2 = vorheriger/aktueller Blickwinkel (8,1 Grad Abweichung): bei Spieler 2180 war hd2 des 1b37 gleich dem hd des vorhergehenden 1a37 und hd gleich dem Weg-Winkel (138 Grad gemessen, 137,8 berechnet) [B für hd, H für hd2].

**1c37** (nur cur3, n=6527): gleiches Layout wie 1b37. Zu 94 % NPC (9 NPCs, davon zwei Bosse-ähnliche mit je ca. 1500 Frames); exakt 1,00 s Abstand [B] -> NPC-Keyframe. Beispiel: `1c37 b180 01 04 e88f6847 54ca9846 000efa46 | 0040 | 0040 | 01`.

**2f37** (n=6 909, Länge nach `f`: `28 + ((f>>1)&1)`, 100 %): `f | pos | dir(12) | face(f32) | [B=01 wenn f&2]`, f in {00, 02}.
`dir` ist ein Einheitsvektor der **Laufrichtung** (Betrag Median 1,000; 0 bei Stillstand), `face` ein **Winkel als f32 in u16-Einheiten (0..65535)**, also die Blickrichtung. Beispiel: `2f37 a374 02 1a967e47 2db746 84f7f546 | c9ec093f 06ab57bf 00000000 | 005c5947 | 01` -> pos (65174, 23443, 31484), dir (0,54 / -0,84), face 0x47595c00 = 55644 -> 306 Grad. Der Winkel von `dir` weicht im Median 6,7 Grad vom Blickwinkel ab (p90 113 Grad) [B].

**2e37** (n=2 877) **Skill-Cast mit Position** [B]: `f(0x73 oder 0x33) | skillId (u32) | 01 | pos(12) | dir(12) | face(f32) | wirkerEid (u32) | vec2 (2 x f32) | 00000000 | 01`.
- skillId: 2877 von 2877 liegen in der dekodierten Tabelle `Skill`; die häufigsten sind Grundangriffe 18000101, 17000101, 14000101, 12000101, 11000101, 15000101, 16000101, 13000101 (xx000101/xx000100, Präfix = Klasse).
- wirkerEid (u32) = eid des Frames in 2860/2877 (99,4 %).
- dir ist der Einheitsvektor von face (Winkelabweichung 0,0 Grad), vec2 = 100 x dasselbe in 2D (Betrag exakt 100,0).
- Zeitlich zusammen mit 0238/2a38/0438 desselben Wirkers (Entität 2180, Sequenz 27:40.101-27:40.204). Der Frame liefert die Position des Wirkers im Moment des Skills (bei Fernkampf/Dash nützlich).

**2b37** (n=342): `f | pos | n | n x pos | hd | face(f32) | B`, n = 3 (226) oder 4 (116); f=07 hat ein Byte mehr. Layout passt bei 257 von 342 exakt (der Rest sind f=07-Frames mit Extra-Byte). Wegpunkte liegen im Median nur 36 cm auseinander (p90 181 cm): vorausberechneter Laufweg (Pfad/Interpolation) [H].

### 3.3 Delta-Frames (10 Hz, int8 in cm)

Gemeinsame Regel: x/y/z-Differenz seit der **letzten bekannten Position derselben eid** (Keyframe oder vorheriges Delta), je ein vorzeichenbehaftetes Byte (cm); fehlende Achse = 0.

**1d37** (cur3, n=6891; beobachtete f: 01-0f und 20-2f; Bit 0 S vorn, Bit 1 dx, Bit 2 dy, Bit 3 dz, Bit 5 Marker):
`f | [S] (f&1) | [dx] (f&2) | [dy] (f&4) | [dz] (f&8) | hd | hd2 | [S] (!(f&1))`.
Beispiele: `1d37 943a 07 01 f10b 5a28 5a28` (S=1, dx=-15, dy=11), `1d37 def501 26 01 0c b53a b53a 01` (dx=1, dy=12, S hinten). Gegenprobe Entität 2180: zwischen zwei 1b37/1a37-Keyframes 6 Deltas mit Summe (-186, +423), tatsächliche Verschiebung (-189, +427) [B]. Median-Restfehler über alle Intervalle in cur3: 7 cm in x, 6 cm in y (Verschiebung 144/134 cm).

**2037** (n=19 327, Länge nach `f`: `15 + (f&1) + ((f>>1)&1) + ((f>>3)&1)`, 100 %): `f | [dx] (f&1) | [dy] (f&2) | dz (immer) | hd | vel | [B=01] (f&8)`. Bit 2 fehlt, dz ist immer da (Regression: pre0=dx r=0,84-0,96, pre1=dy r=0,86-0,93, letztes Vorbyte = dz r=0,95-0,98, Steigung ca. 1,2, `c6.py`). Beispiel: `2037 9807 0b 53 4a 69 | c31d | b8022a44 184418 44 b0293544 | 01`: dx=83, dy=74, dz=105, hd 42 Grad, vel (680, 609, 725). Median-Restfehler 33 cm bei 186 cm Verschiebung (cur3), 10-16 cm bei Verschiebungen 86-222 cm (alle Dumps). Gleiche Bewegung wie 1e37 (Keyframe), daher Delta-Gegenstück zu 1e/1f.

**2537** (n=1 450, Länge `14 + popcount(f & 0xf)`, 100 %): `f | [dx] (b0) | [dy] (b1) | [dz] (b2) | hd | vel | [B] (b3)`. Wie 2037, aber auch dz optional (f=0x0f: 3 Deltas r=0,85/0,93/0,73).

**2a37** (n=11 248, Länge `14 + popcount(f & 0xe) + (1 wenn f&0x11)`, 100 %): `f | [S] (b0) | [dx] (b1) | [dy] (b2) | [dz] (b3) | vel | hd | [S] (!(b0) und b4)`. Delta-Gegenstück zu 2937/2837 (vel vor hd). Regression f=0x1f: dx r=0,95, dy r=0,95, dz r=0,94 (Steigung 1,4-1,5, weil 1d37/1c37 in den Dumps fehlten); Restfehler in cur3 deutlich höher als bei 1d37 (79 von 236 cm), daher **Deltas hier [H] mit r = 0,9**, Layout [B].

**2d37** (n=1 982, `6 + popcount(f & 7)`, 100 %): `f | [dx] (b0) | [dy] (b1) | [dz] (b2) | hd | face(f32)`. face-Float = hd (Median-Abweichung 0,0 Grad, p90 10,2 Grad): beide Winkel; Drehung auf der Stelle/langsame Bewegung, kommt oft hinter 2b37 (Wegpunktpfad) [H].

**3237** (n=387, `4 + popcount(f & 0xf)`, 100 %): `f | [dx] (b0) | [dy] (b1) | [dz] (b2) | hd | hd2 | [S] (b3)`. Gegenstück zu 3037. Gegenprobe Entität 920: 3037 pos (64905, 21723, 33411) -> drei 3237 f=0f mit (79,-30,18), (74,-28,16), (76,-29,13) -> 1a37 pos (65141, 21631, 33457): Summe (229, -87, 47) gegen (236, -92, 46) [B].

### 3.4 Marker und Zustandsframes

**2137 / 2237** (n=2 617 / 2 572): nur `eid` (4 Byte). Zeitliche Statistik je Entität, 1-Sekunden-Fenster (`d1.py`): um 2137 herum 2,17 Bewegungsframes davor und **4,18 danach** -> "Bewegung beginnt"; um 2237 3,49 davor und **1,15 danach** -> "Bewegung endet" [B Statistik, H Bezeichnung]. 2237 kommt auch für die **eigene** eid vor (182 von 1329 Frames in 95 Dumps mit erkannter eigener eid; in cur3 1 mal kurz vor dem Zonenwechsel).

**4e36** (n=976): `f32 a | f32 b | 00`, beide Winkel in Grad (0..360). b = hd des letzten Positionsframes derselben Entität (Beispiele cur3: b = 121,5 bei hd 121; 267,7 bei 268; 93,9 bei 94 ...), a = vorheriger/anderer Winkel [H]. Auch für die eigene eid (18 Frames, v. a. beim Zonenwechsel).

**4736** (n=6 787): `Grund (1 Byte: 01 / 0a / 02 / 10) | 00 oder Byte`. Nach 4736 kommen für diese Entität keine Bewegungsframes mehr (davor 4,5 pro Sekunde, danach 0,0, `f2.py`), gefolgt oft von 4536 (neuer Spawn): **Spieler-Entfernen** [B Statistik]. 0a = Zonenwechsel/Teleport [H] (eigene eid vor Zonenwechsel: `4736 eb55 0a00`).

**3c37** (n=388, bekannt als localPosition): `f(00 oder 03) | pos | Yaw (f32, GRAD, -180..180) | 00 00 01 | u32 | 5 Byte`. Beispiele: `3c37 d966 03 a694ed46 809314c7 00eac246 | e4922343 (163,6 Grad) | 000001 ec5fa900 e343294269`. Kommt für fremde und eigene eids (eigene: 51 von 223 in Dumps mit eigener eid), selten: Teleport/Dash/Server-Korrektur.

**4437** (n=282): nur `eid | 0000`, immer die eigene eid (281 von 281), meist Bursts von ca. 20/s kurz vor/nach dem Laden [H: Lade-/Weltzustand].

### 3.5 Nicht-Bewegung

- **3436** (n=9 128) = envSpawn: `eid | f(00|01|04|0c) | 00 | envId (u32) | pos(12) | u32 0 | yaw (f32, GRAD, 0..360, in 1/256-Umdrehung quantisiert; 9128 von 9128 im Bereich) | ...`. Statisch: kommt in keinem Bewegungsframe vor. 231 verschiedene envIds, alle in der Tabelle `EnvObjData` (231/231).
- **3836** = `envEid | aktorEid (Varint) | 01/02`, 3a36 = `envEid | aktorEid | 8 Nullbytes`, 3b36/6d8d/058d = nur `envEid`. Aktor löst auf: Spieler 1065, eigene 112, NPC 1 (Rest = vor Dump gespawnt). Häufigstes Objekt: envId 1500000 (1387 Nutzungen, 1596 Spawns), 1100001 (117) -> Benutzen/Ernten eines Umgebungsobjekts durch einen Spieler [H Art].
- **3536** = envRemove: `eid | 3 Byte (010100 / 040300 / ...)`.
- **5636**: Login-Datensatz `varint | b | 5 Byte | u32 | 4 Byte`, z. B. `5636 e59901 00 0000000000 e61c0200 00000000`; die "eid" ist ein ID-Wert (19685, 23376, 3623 u. a.) und taucht in keinem Spawn auf, kein Bewegungsframe.
- **748d** (n=2 750): `eid | 00 | 05 | Varint-Wert | Typ (1-5)`, Werte oft 0, 32842, 17329, 26577; Zugehörigkeit zur HP-/Status-Gruppe 0x8d, mit Tabellen-IDs kein sinnvoller Treffer (die Treffer in ActInfo/AdditionalStat sind bei den Tabellengrößen Zufall) [H].

## 4. Entity-ID-Auflösung (geprüft, nicht übernommen)

Methode (`b3.py`): pro Dump eid -> letzter Spawn-Typ (4136 NPC, 4536 Spieler, 3436 Env, 3336 "Charakter"), Entfernen löscht (4236/3536).
Ergebnis nach Opcode (Spieler/NPC/unbekannt): 2937 10749/1094/1484, 1a37 9112/2683/5428, 1b37 10159/2632/5374, 2a37 4805/4416/2027, 1e37 5043/1/2294, 2f37 4247/465/2197, 2e37 2086/17/774, 2037 12927/3/6397. "Unbekannt" ist der Anteil, dessen Spawn vor Beginn des Mitschnitts lag.
- **4136 (NPC):** Frame-Layout `eid | 2 Typ-Byte | Flag | npcId u32 | ... x (npcId+6) | y (npcId+10)`; die erste Bewegungs-Position derselben eid liegt bei 787 von 824 Spawns (95 %) innerhalb von 8 m der Spawn-Position (`f6.py`, Positionsversatz +9 hinter der eid).
- **4536 (Spieler):** Name steht im Frame (z. B. `Supito...`, `Hidehamster`), Position hinter dem Namen (versatz hängt von der Namenslänge ab); 1958 von 2263 ersten Bewegungspositionen liegen innerhalb von 8 m davon.
- **3436 (Env):** nie in einem Bewegungsframe.
- Eigene eid: aus 0x008d-Frames (11 Byte) ablesbar; wechselt beim Zonenwechsel (cur3: 10987 -> 1828, `3336` nennt den Namen).
Die Namen im Decoder (npcSpawn 4136, envSpawn 3436) stimmen damit.

## 5. Plausibilitätsprüfung (Stetigkeit, Geschwindigkeit)

- 55 730 aufeinanderfolgende Positionspaare derselben eid (alle Absolut-Opcodes, Abstand 0,01-1,5 s): nur **40** mit berechnetem Tempo > 50 m/s (0,07 %, Teleports/Sprünge), `d9.py`.
- cur3 (vollständig, 68 Entitäten, 1426 Positionen): berechnetes Tempo Median 11,1 m/s, p90 25,4 m/s, p99 29,8 m/s; Verhältnis berechnet/gemeldet Median 1,04 (p10 0,70 / p90 1,29, n=511).
- Alle Dumps, Verhältnis berechnetes/gemeldetes Tempo (Median): 1e37 0,99; 1f37 0,98; 2437 1,01; 2937 1,01; 2837 1,27 (siehe 3.1). Verhältnis `hd` zu Verschiebungsrichtung: 2937 0,0 Grad, 2837 0,0, 1e37 0,2, 1f37 1,5, 1b37 0,5 Grad (jeweils Median).
- z: |dz|/dt Median 0,5 m/s (Boden), p90 7,2 m/s (Flug).
- Länge: für 2937, 2837, 1e37, 1f37, 2437, 2f37, 2037, 2537, 2a37, 2d37, 3237 trifft die Längenformel auf **jedes** Frame zu; Positionen sind bei allen Absolut-Opcodes in 100 % endlich und |v| < 300 000.
- Koordinatenbezug (`d7.py`): vor den Frames der anderen Entitäten kommt der eigene Zonenframe 0x2136 mit Position; in den ersten 6 s liegen 1191 Positionen anderer Entitäten im Median 21 m, p10 6,6 m davon entfernt (p90 976 m kommt von Zonenwechseln im selben Dump).
- Karten/Himmelsrichtung: Aus den Frames allein folgt nur "0 Grad = +x, 90 Grad = +y"; dass +y Süden ist, kommt aus der bestehenden Notiz (Weltkarte, Luftschiff-Test), die diese Frames nicht widerlegen.

## 6. Prüfung der Aussage "Gehen/Fliegen erzeugt keine Positionsframes"

- **Eigene Entität:** bestätigt [B]. 95 Dumps mit erkannter eigener eid: 2237 (182 Frames), 4437, 3c37 (51), 4736 (145), 4e36 (33) tragen die eigene eid, aber nur 3c37 (Teleport/Dash/Korrektur) und 4437/2237 (Marker) beim Laden/Zonenwechsel, nie fortlaufend. In cur3 (29 min mit Herumlaufen) kommen für die eigene eid vor dem Zonenwechsel nur 1x 2237, 1x 4736, 2a38/008d/0238/0638/... (Effekte, HP). Der Rest der Treffer in 1a37/1b37/2a37... sind eid-Wiederverwendungen (andere Zone/Entität).
- **Andere Entitäten:** widerlegt. Spieler und NPCs in Reichweite werden mit 10 Hz (Delta) bzw. 1 Hz (NPC) / ca. 1 Hz (Spieler-Keyframe) fortlaufend gemeldet; Position, Richtung, Tempo, Blickrichtung, Skill-Cast-Position (2e37). Die Notiz "1a37/1b37/1c37 sind Bewegungen anderer Entitäten: Entität | 1 bis 2 Flag-Byte | x|y|z" stimmt im Kern; der Versatz der Floats folgt exakt den Regeln oben (kein "plausible Werte wählen" nötig).
- Nutzung: eigene Position lässt sich daraus nicht ableiten (keine Referenz zur eigenen eid), wohl aber die Position jedes Mitspielers/NPCs; die Pet-Karte könnte NPC-Positionen laufend statt nur beim Spawn nehmen.

## 7. Referenzdecoder (Pseudocode, in `dec.py` als Python ausgeführt)

```
eid, p = varint(frame, 2); f = frame[p]
2937/2837: q = p+1+(f&1); pos=f32x3(q); vel=f32x3(q+12); hd=u16(q+24); S = f&1 ? frame[p+1] : frame[-1]
1e37:      q = p+1+(f&1); pos=f32x3(q); hd=u16(q+12); vel=f32x3(q+14)
1f37/2437: q = p+1;       pos=f32x3(q); hd=u16(q+12); vel=f32x3(q+14)
1a37:      q = p+2+(f&1); pos=f32x3(q); hd=u16(q+12)
1b37/1c37: q = p+1+(f&1); pos=f32x3(q); hd=u16(q+12); hd2=u16(q+14)
2f37:      q = p+1;       pos=f32x3(q); dir=f32x3(q+12); face=f32(q+24)
2e37:      skill=u32(p+1); pos=f32x3(p+6); dir=f32x3(p+18); face=f32(p+30); caster=u32(p+34)
3037:      pos=f32x3(p); hd=u16(p+12); hd2=u16(p+14)       # kein Flag-Byte
Deltas:    1d37: q=p+1+(f&1); dx=s8 if f&2, dy=s8 if f&4, dz=s8 if f&8; dann hd, hd2
           2037: dx if f&1, dy if f&2, dz immer, dann hd(2), vel(12)
           2537/3237/2d37: dx if f&1, dy if f&2, dz if f&4, dann Rest
           2a37: S wenn f&1, dann dx(f&2) dy(f&4) dz(f&8), dann vel(12), hd(2)
Winkel: u16 / 65536 * 360 Grad, 0 = +x, wächst nach +y; Einheiten cm, cm/s
```

## 8. Offene Punkte

1. S-Byte und die Marker-Bits (Bedeutung: Bewegungsmodus? Werte 1-4): Statistik spricht für Bodenlauf/Gehen/Gleiten/Flug, nicht abgesichert.
2. hd2 (1b37/1c37/1d37/3037) = Blickrichtung (H); Unterschied zwischen 1b37 und 1c37 (gleiches Layout): 1c37 = NPC-Keyframe 1,00 s, 1b37 = Spieler/andere.
3. Warum 2937 und 2837 sowie 1e37/1f37/2437 getrennte Opcodes sind (nur Statistik: 2837 = Beginn/Beschleunigung, 1e37 f=14 = Steigen).
4. Sonderlayout in 1a37 f=02/1b37 f=03/1a37 f=0c (Zusatzblock 18-20 Byte bzw. 9 Byte mit Wert 1/50/1000).
5. 2a37/2537/2d37: Delta-Zuordnung nur r ca. 0,9 (Steigung > 1, weil 1d37/1c37 in den Dumps fehlen; mit vollständiger Aufnahme prüfen).
6. 748d, 5636, 4437, 6d8d/058d: Zweck nicht geklärt. 1d37/1c37 sind in den Dumps nicht enthalten (Extraktionsfilter), nur in cur3 geprüft.
