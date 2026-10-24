# Trefferarten im Treffer-Frame `0438`

Befund aus den Debug-Aufnahmen vom 11./12.10.2026 (Charakter „Aahz“, drei Aufnahmen mit zusammen rund 170.000 Treffern). Die Deutung der Flag-Bytes kam aus der Analyse eines anderen Meters (nur Protokollfakten, kein Code); hier wurde sie an unseren eigenen Aufnahmen **gegen den Schaden geprüft**.

## Layout (Treffer mit Schadensblock, Flag-Byte `& 0x04`)

```
opcode | Ziel (varint) | Flags (varint; unteres Halbbyte = Art 4..7) | 1 Byte | Akteur (varint) | Skill-ID (u32 LE)
       | 1 Byte (Folge) | Schadensart (1; 2 = normal, 3 = Krit)
       | wenn Art 4: 8 Byte               (einfacher Treffer, nichts weiter)
       | wenn Art 5..7: Bits (1) | 1 Byte | Richtung (1) | 8 Byte
       | Trefferzahl (u32, 1..9; liegt in diesen 8 Byte) | Skalar (varint) | Schaden (varint) | ...
```

- **Bits** (`Art 5..7`): `2` Parade, `4` perfekt, `8` doppelt. Weitere Bits (`32`, `64`, `128`) sind unbekannt und werden ignoriert.
- **Richtung**: `1` Rücken, `2` Front, `0` keine Angabe.
- **Bit `0x20` (Lebensraub-Treffer, 1.049 von 115.426 Treffern in vier Aufnahmen vom 21.-23.10.):** Das Byte nach den Bits ist dann ein **varint (1-3 Byte) mit der zurückgeholten Lebenspunkte-Menge**, die Richtung steht erst dahinter (immer unmittelbar vor den 8 Byte mit der Trefferzahl). Ohne dieses Bit ist es 114.377 von 114.377 Mal das feste Byte. Der alte feste Zugriff las bei diesen Treffern Richtungen 3-7 (Unsinn), der neue nur 0-2. Im Client behoben (`ReadHitKinds`, noch nicht veröffentlicht).
- **Bits `0x10`, `0x40`, `0x80`:** Waffle nennt `0x10` Standhalten, `0x80` Rücken. Bit `0x80` steht bei 27 % der Treffer, das Richtungs-Byte `1` bei 40 %; die beiden hängen nur lose zusammen (52 % gegen 35 %), und weder Bit `0x80` (×1,06 über 69 Skills) noch Richtung `1` (×0,97) ändert den Schaden. Welches von beiden "von hinten" heißt, bleibt offen und braucht den Testmitschnitt unten.
- **Trefferzahl**: 2..5 bei Multi-Treffern (mehrere Schläge in einem Frame).
- Frames **ohne** Schadensblock (Flag-Byte ohne `0x04`) sind Begleit-Meldungen und tragen keine Trefferart; Heilungen und DoT-Ticks haben keine (`Kinds = null`).

## Prüfung gegen den Schaden

Für jedes Flag: über alle Skills mit mindestens acht Treffern mit und ohne das Flag der Median des Verhältnisses „Median-Schaden mit / ohne“ (`aion2-replay` gibt das am Ende aus):

| Flag | 11.10. 21-01 | 12.10. 16-47 | 11.10. 23-43 | Befund |
|---|---|---|---|---|
| Krit (bekannt) | ×1,87 | ×1,82 | ×1,82 | Gegenprobe: stimmt |
| **Doppelt** | ×2,15 | ×2,01 | ×2,01 | **bestätigt:** doppelter Schaden |
| **Perfekt** | ×1,11 | ×1,08 | ×1,08 | **bestätigt:** kleiner, gleichbleibender Zuschlag |
| **Parade** | ×0,44 | ×0,33 | ×0,43 | **bestätigt:** deutlich weniger Schaden |
| Rücken | ×1,12 | ×0,99 | ×1,05 | **nicht bestätigt:** kein gleichbleibender Schadensunterschied |
| Front | ×0,96 | ×1,04 | ×0,98 | **nicht bestätigt** |
| Multi (Trefferzahl > 1) | ×0,98 | ×1,00 | ×1,00 | erwartungsgemäß neutral (der Schaden ist der des ersten Schlags) |

Das Richtungs-Byte gibt es also, aber ob `1`/`2` wirklich „Rücken/Front“ im Sinne der Geometrie heißt, ist **nicht belegt** - in einer der Aufnahmen steht bei etwa der Hälfte aller Treffer „Rücken“. Die Anzeige trägt deshalb den Hinweis, dass Rücken/Front noch nicht am Spiel geprüft sind.

## Was noch fehlt: Testmitschnitt (Aktion für Ralf)

3 Minuten mit eingeschalteter Debug-Aufnahme an einer Trainingspuppe (oder einem ruhenden Mob), jede Minute nur aus **einer** Richtung angreifen, mit denselben Skills: 1 Minute nur von **hinten**, 1 Minute nur von **vorn**, 1 Minute nur von der **Seite**. Dazu die Uhrzeiten notieren (oder jeweils kurz pausieren). Dann lässt sich das Richtungs-Byte eindeutig zuordnen, und Perfekt/Doppelt lassen sich mit der Anzeige im Spiel abgleichen (Bildschirmfoto der Trefferzahlen).
