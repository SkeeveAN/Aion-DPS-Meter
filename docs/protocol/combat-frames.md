# Kampf-Frames: Windup, Cast, Buffs, Max-HP

Befund aus den Debug-Aufnahmen des Clients (`%APPDATA%\Aion DPS Meter\Debug`, 11./12.10.2026, Charakter „Aahz“, 162.243 Segmente der Datei `aion2-2026-10-11_21-01-01`). Die Opcode-Schreibweise ist die unseres Decoders (`0x0938` = Bytes `09 38`, Opcode big-endian, vor dem Opcode steht nur der Längen-Varint, der beim Zerlegen entfällt).

Die Kandidaten kamen aus der Analyse eines anderen Meters (nur Protokollfakten, kein Code); **jeder Wert unten ist in unseren Mitschnitten selbst nachgeprüft** und in `SelfCheckCombatOverlay` mit den echten Bytes festgehalten.

## Häufigkeit in der geprüften Aufnahme

| Opcode | Anzahl | Bedeutung |
|---|---|---|
| `0x0938` | 404 | **Windup** (Skill wird aufgeladen, Dauer bekannt) |
| `0x0238` | 126.765 | Skill benutzt (Spieler und Monster), Ziel bekannt, **keine** Dauer |
| `0x2a38` / `0x2b38` | 9.281 / 150.228 | Effekt auf einer Entität: neu / erneuert |
| `0x2c38` | 6.863 | Effekt(e) beendet |
| `0x008d` | 21.610 | HP; Art 0 = aktuell, **Art 7 = Maximum** |
| `0x0e38`, `0x0638`, `0x0338`, `0x4738` | 731 / 4.917 / 2.773 / 96 | bewegte Fläche, Skill-Ende, Treffer-Position, Abklingzeiten (noch nicht entschlüsselt) |
| `0x05e0`, `0x4838`, `0x1b38`, `0x1e38` | 0 | in dieser Aufnahme nicht vorhanden (Stagger, Fläche, Aufladen) |

## Windup `0938` (belegt)

```
opcode | Akteur (varint) | 1 Byte | Skill-ID (u32 LE) | 1 Byte | Ziel-Flags (1) | Ziel (varint) | Blickwinkel (f32, Grad)
       | wenn Ziel-Flags & 2: x, y, z (je f32) | Dauer in ms (varint) | Rest
```

- Alle 404 Frames lesen bis zum Ende; Dauer 500–1.900 ms (häufig 1.500, 1.000, 800, 1.900), Blickwinkel immer in ±720, Ziel-Flags immer `0x02` (mit Position).
- Der **Schaden** desselben Skills (gleiche Skill-ID, gleicher Akteur, gleiches Ziel wie im Windup) folgt im Frame `0438` etwa **0,6 s nach Ablauf** der Dauer (gelernt: 650 ms, 552 ms, 749 ms, 153 ms, 250 ms je Skill).
- Beispiel: `0938 C89501 02 D8DD1200 01 02 F5C102 46E29042 934E3F47 05C6B846 001D0347 DC0B 01904E` = Akteur 19144, Skill 1236440, Ziel 41205, Blick 72,4°, Position (49.998 | 23.619 | 33.565), 1.500 ms.
- Skill-IDs der Monster haben 7 Stellen (1236440); der Name im Client heißt bei den meisten schlicht „Angriff“/„Attack“.

## Cast `0238` (belegt für Akteur und Ziel)

Dasselbe Layout wie der Windup bis zum Ziel, ohne Dauer; Ziel-Flags meist `0` (keine Position), Selbstziel = Akteur. Dient nur dazu zu merken, **wen ein Akteur zuletzt anvisiert hat** (Grundlage für „Ziel des Ziels“: Ziel des letzten Casts, Windups oder Treffers, höchstens 10 s alt). Für einen **fremden Spieler** gibt es keinen eigenen Frame „aktuelles Ziel“; die Antwort kommt aus dessen letzter Skill-Benutzung.

## Effekte `2a38` / `2b38` / `2c38` (belegt)

```
2a38 (neu):       Ziel (varint) | 01 | Art (1) | Instanz (varint) | Effekt-ID (u32) | Dauer ms (u32) | bleibt-Flag (u32) | 8 Byte (Serverzeit) | Wirker (varint) | … | x, y, z (f32) am Ende
2b38 (erneuert):  wie oben ohne das 01 hinter dem Ziel
2c38 (Ende):      Ziel (varint) | Anzahl (1) | je: Art (0 oder 7) | Instanz (varint) | Grund (1) | bei Art 7: von (varint), Skill (u32), 4 Byte
```

- Skill des Effekts = Effekt-ID / 10. Bleibt-Flag ≠ 0 heißt: ohne Ablaufzeit (bis zum Ende-Frame).
- Beispiel (2a): Ziel 5.174, Instanz 55, Effekt 200 (Skill 20), 300 ms, Wirker 5.174.
- **Debuff** = Wirker ist ein Monster (`IsKnownMonster`); das ist eine Näherung, kein Feld des Frames.

## HP `008d`, Art 7 (belegt als Maximum, mit Vorbehalt)

Die 9-Byte-Einträge (Art + i64) enthalten neben Art 0 (aktuell) gelegentlich Art 7. Der Wert liegt bei 236 von 340 Entitäten nicht unter dem höchsten gesehenen aktuellen Wert und ändert sich mit Effekten, die die Max-HP erhöhen (z. B. 17.954 → 18.158 → 18.271 bei einem Spieler). Die Anzeige nimmt Art 7 nur, wenn sie nicht unter dem letzten Messwert liegt; sonst gilt der höchste gesehene Wert.

## Noch offen

- Stagger (`05e0`), Flächen (`4838`, `0e38`): in dieser Aufnahme nicht oder kaum vorhanden; folgt in `feature/rotation-buffs-stagger`.
- Ziel eines **fremden Spielers** und Auswahl **per Kamera**: kein Frame bekannt; das Element „Ziel des Ziels“ zeigt dann nichts.
