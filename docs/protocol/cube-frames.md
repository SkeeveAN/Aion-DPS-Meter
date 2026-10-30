# Hidden Cube im Datenstrom (Stand 30.10.2026)

**Quelle:** Die Deutung stützt sich auf die Befunde des Open-Source-Projekts **dr89mh/aion2-cube-radar** (https://github.com/dr89mh/aion2-cube-radar, `docs/FINDINGS.md` B5 bis B12, Stand 18.10.2026), die wir an unseren eigenen Aufnahmen (5. bis 29.10.2026, 36 Cube-Sichtungen) nachgeprüft haben. Es wurde kein Code von dort übernommen, nur Wissen über das Protokoll.

| Frame | Inhalt | Beleg |
|---|---|---|
| `0x3436` Spawn | Entität (varint), Art u16, **Env-ID** u32 (dort „Template"), x y z f32 (z = Höhe), f32, Blickrichtung f32, 2 Byte, **Zustand** u8 (+28), **Entstehungszeit** u64 Unix-ms (+29) | eigene Aufnahmen: Entstehungszeit ist gültig; Zustand 6 = der Cube ist 1 bis 2 s vorher entstanden, Zustand 1 = er stand schon, als er in Reichweite kam |
| `0x3836` | Spieler beginnt zu öffnen: Objekt, Akteur (varint), 01 | dr89mh B6 |
| `0x3a36` | Öffnen fertig: Objekt, Akteur, Anzahl u32 = 1, Gegenstand u32 = 630900003 (`0x259AC523`, „Schlüssel: Versteckter Kubus (Gebunden)") | dr89mh B6; Item-ID steht in `item_names.json` |
| `0x3536` | Entfernt: Grund `04` = geöffnet/zerstört, `01` = außer Reichweite (steht vermutlich noch) | dr89mh B6, eigene Aufnahmen (Austritt 19:05:52 am 28.10.) |

**Reichweite:** Der Server meldet einen Cube ab etwa **100 m** (Ebene). Eigene Messung mit Cubes, die schon standen (Zustand 1) und einer genauen eigenen Position unter 1 s daneben: 94 m und 103 m; dr89mh misst 97 bis 105 m (vier Werte). Der frühere Kommentar im Client („40 bis 60 m") war falsch.

**Im Client:** `Aion2FrameDecoder.DecodeEnvSpawn/DecodeEnvRemove/DecodeEnvOpenStart`, `Aion2EntityDirectory` (Zustand, `CubeDetails`, `CubeDepartures`), Klang „außer Sicht" (`assets/sounds/cube-left.wav`, Tools/make_cube_left_sound.py), Testmodus `aion2-cube-check <aufnahme.jsonl>` (wie lange ein Cube in Sicht war und wie oft die eigene Position dabei aktuell war).

**Annäherungston:** Die Skala läuft jetzt von 100 m (2,5 s Abstand) bis 3 m (0,15 s); vorher endete sie bei 60 m, sodass zwischen der Meldung bei 100 m und 60 m der Takt gleich blieb. Die eigene Position kommt nur ereignisweise: in den Aufnahmen war sie während der Cube-Sichtung bei 0 bis 10 von 10 bis 30 Sekunden nicht aktuell. Der letzte Takt läuft deshalb bis zu 8 s weiter.

**Nicht gebaut:** Der „Ring-Fix" von dr89mh (beim Eintritt in die Reichweite steht der Spieler etwa 100 m vom Cube) verbessert nur einen Augenblick und nur Cubes mit Zustand 1; der Gewinn gegenüber dem weiterlaufenden Takt ist klein. Gegner-Anzeige mit PvP-Rang (`0x4536`): nicht gewünscht.
