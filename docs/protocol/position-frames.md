# Eigene Position und Weltkoordinaten im Datenstrom

Befund vom 16.10.2026 aus den Debug-Aufnahmen von Aahz (Verteron, Legions-Luftschiff), mit Stillstand-, Lauf- und Flug-Fenstern gegen die Weltkarte des Spiels geprüft.

## Koordinaten

Weltkoordinaten sind `f32`, 100 Einheiten = 1 m. **+x = Osten, +y = Süden** (auf der Weltkarte nach unten), z = Höhe. Die Karte rechnet `Pixel = Offset + Maßstab * Welt` ohne Spiegelung (Client/assets/aion2/maps/maps.json).

Das **Verteron-Legions-Luftschiff** (Zone 902; 901 Altgard, 904 Eltnen, 905 Morheim) liegt in denselben Weltkoordinaten wie die Karte darunter: die Spawn-Position (56 966, 54 706) fiel auf den Pfeil des Schiffs in der Weltkarte von Verteron (z um 102 500). Die Pet-Karte darf dort auf der Karte der Zone bleiben.

## Wo die eigene Position vorkommt

| Quelle | Frame | Wann |
|---|---|---|
| Zonenframe `0x2136` | x, y als f32 bei Byte 18 und 22 | nach jedem Teleport oder Ladebildschirm: **die einzige Position ohne Aktion** |
| Positionsframe `0x3c37` | `Entität (varint) \| 03 \| x \| y ...` | bei Skills, Treffern, Korrekturen (selten) |
| Effekt auf dich `0x2a38` / `0x2b38` | die letzten 12 Byte sind x, y, z des Wirkers | bei jedem Effekt, den du auslöst |
| Mob-Spawns `0x4136` | Position des Mobs | erst wenn ein Mob in Reichweite kommt (auf dem Schiff etwa 20 bis 40 m, draußen nach der Notiz im Code etwa 65 m) |

**Gehen, Laufen und Fliegen erzeugen keine Positionsframes für dich.** Im Stillstand kommen außer dem Takt nur die Serveruhr und der Ticket-Kram.

### Zonenframe `0x2136`

`opcode | 4 Byte | Zone-ID (u32, Byte 6) | ... | x (Byte 18) | y (Byte 22) | z (Byte 26) | ... | Art (u32, Byte 34)`. Beispiel: Zone 1010 (Verteron), Spawn (59 953, 17 085) = Zumionstätt, danach 1010 mit (-110 292, -106 862) = Basis der Legion der Morgenröte; Zone 902 = Verteron-Legions-Luftschiff. Der Client bezieht daraus jetzt Position und Karte (`NoteSpawnPosition`, `Aion2Maps.OfZone`).

### Entitäts-ID

Die Entitäts-ID des eigenen Charakters ist **nicht fest**: sie ändert sich beim Zonenwechsel (4422 -> 14583), bleibt aber über Teleports in derselben Zone gleich. Der Charakter-Datensatz `0x3336` nennt den Namen zur ID.

### Fertigkeiten `0x0238`

Die Fertigkeit trägt **Richtungsvektoren**, keine Weltkoordinaten (z. B. 46,5 / 68,8 / 72,6). Nicht als Position lesen.

## Bewegungen anderer Entitäten

`0x1a37`, `0x1b37`, `0x1c37`: `Entität (varint) | 1 bis 2 Flag-Byte | x | y | z`. Der Versatz der Floats hängt von den Flag-Bytes ab (1 bis 2 Byte: am sichersten über plausible Werte wählen). **Sie kommen nie für die eigene Entität.** Der Server meldet nur, was in Reichweite ist: im Stillstand ohne andere Spieler oder NPCs kommt nichts.

## Rauschen

- `0x0036`: etwa 20 pro Sekunde, der Takt, immer da.
- `0x0336`: alle 9 s, 20 Byte, die Serveruhr (`... 41 a1 01 00 00`, dieselbe wie in den Effekt-Frames).
- `0x1d37`: etwa 5 bis 7 pro Sekunde, 12 Byte, andere Entitäten, Zweck offen.

## Flugenergie und Höhe

- `0x008d` mit **deiner** Entität, 11 Byte: `008d | Entität | 01 01 | Typ | u32`. Typ `06` beim Fliegen, `03` beim Laufen. Etwa 10 pro Sekunde, solange die Anzeige sich ändert. Der Wert sinkt im Flug um 750 pro Frame (7 500 pro Sekunde) und steigt im Stand wieder an: **Flugenergie** (Ausdauer), keine Koordinate.
- `0x4637 | Entität | f32`: die Höhe (z), kommt mit den Ladevorgängen (zusammen mit `4437`, `4236`).

## Offen

- `0x2a37` und `0x4b36` von einer fremden Entität (im Flug etwa 1,5 pro Sekunde) nicht entschlüsselt.
- Ob die Pakete vom Client zum Server (verschlüsselt, etwa 20 pro Sekunde) Bewegung tragen: nicht lesbar.
- Die Konkurrenten: aion2t schreibt in den Release Notes 1.1.42 selbst, dass die Position im leeren Gelände hinterherhinkt oder geschätzt wird; THGL (The Hidden Gaming Lair) liest die Position aus dem Spielspeicher über einen Treiber (kein Weg für uns).

## Siehe auch

Bewegungs- und Zustandsframes **anderer** Entitäten (Positionen, Deltas, Skill-Casts mit Position): [bewegungs-frames.md](bewegungs-frames.md). Für die eigene Entität bleibt die Tabelle oben unverändert (Auswertung vom 23.10.2026 bestätigt: keine laufenden Positionsframes).
