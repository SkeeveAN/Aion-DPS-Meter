# Abklingzeiten-Frame `0x4738` (und `0x2238`)

Befund vom 14.10.2026 aus den Debug-Aufnahmen (Charakter „Aahz“, `aion2-2026-10-13_17-14-19`: 95 Frames). Der Aufbau wurde zuerst am Strom selbst gefunden; ein anderer Meter nennt dieselben Opcodes (nur Protokollfakten, kein Code).

## Aufbau (belegt)

```
opcode 47 38 | n (1 Byte) | n x ( Skill-/Gruppen-ID (u32 LE) | verbleibende Zeit (varint) )
```

- Alle 95 Frames der Aufnahme lesen ohne Rest bis zum Ende; Einträge je Frame: 25 (59x), 1 (35x), 5 (1x).
- Das Frame ist die **vollständige Abklingzeiten-Liste des lokalen Spielers**. Fertige Skills stehen mit Zeit `0` darin; die 25er-Frames kommen etwa alle 0,6 s, solange etwas läuft, die 1er-Frames nach dem Einsatz eines einzelnen Skills.
- IDs ab 10.000.000 sind Skills (11.250.000 u. a.; `11000100` = Ausweichen). Kleine IDs (`1101`, `1102`, `9079`) und `4899196` stehen bei jedem Frame mit Zeit `0`: vermutlich gemeinsame Abklingzeit-Gruppen (z. B. Tränke); nicht geklärt.
- Beispiel: `47 38 19 | 6041ae00 00 | 4d040000 00 | 77230000 00 | 4e040000 00 | 7cc54a00 00 | 50a9ab00 bcd706 | 24d9a700 00 | ...` = 25 Einträge, Skill 11.250.000 noch 109.500.

## Zeiteinheit (offen)

Die Zahl sinkt gleichmäßig, aber **etwa 2,3 bis 2,5-mal so schnell wie die Uhr** (Median je Skill 2,24 bis 2,54, Skill 11.100.000: 40.600 -> 0 in 27,4 s). Alle Skills zeigen fast denselben Faktor. Zwei Deutungen: der Wert ist in „Grundzeit“ angegeben und der Charakter hat Abklingzeit-Beschleunigung, oder die Einheit ist keine Millisekunde. **Prüfen:** dieselbe Messung ohne Buffs/Ausrüstungsboni und mit anderem Charakter (Chumley); dann ist der Faktor entweder konstant (Einheit) oder ändert sich mit dem Charakter (Beschleunigung, wäre als Wert nutzbar).

## `0x2238` (Ladungen, nur ein Beleg)

`22 38 | 01 | 4a7d1401 | 02`: ein Eintrag; vermutlich Skill-ID, Ladungen und Restzeit bis zur nächsten Ladung. Ein einziges Frame in der Aufnahme, nicht ausgewertet.

## Offen
- Einheit/Faktor (oben), Bedeutung der Kleinst-IDs, `0x2238`.
- Im C#-Decoder seit 14.10.: `Aion2Cooldowns` ([Client/Aion2/Aion2Cooldowns.cs](../../Client/Aion2/Aion2Cooldowns.cs)) hält die letzte Liste des lokalen Spielers (kurze Frames ändern nur ihren Skill). Es gibt **keine Anzeige**; das Assist-Fenster ist vorbereitet, nicht gebaut.
