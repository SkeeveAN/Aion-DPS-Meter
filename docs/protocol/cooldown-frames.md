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
- Im C#-Decoder seit 14.10.: `Aion2Cooldowns` ([Client/Aion2/Aion2Cooldowns.cs](../../Client/Aion2/Aion2Cooldowns.cs)) hält die letzte Liste des lokalen Spielers (kurze Frames ändern nur ihren Skill). Seit 15.10. zeigt das Assist-Fenster (Tools-Menü, `Client/Ui/AssistWindow.cs`) die Liste samt Effekten auf dem eigenen Charakter; die Zeit wird mit Faktor 2,4 geschätzt („~“).

## Geschwindigkeit je Skill (30.10.2026)

- Der Zähler fällt nicht mit einem festen Faktor: Messung an den 5620 Frames der Aufnahme `aion2-2026-10-29_17-17-26-Aahz`. Dieselbe Fertigkeit (11100000, Ruinous Blow) fällt je Einsatz mit 1900 bis 2700 Einheiten je Sekunde, in einzelnen Läufen auch schneller (um 3600), andere Skills mit 800 bis 2400. Die Tabelle (`Skill.json`, `NeedCoolTime`) nennt 45 s für 11100000, die gemessene Abklingzeit liegt bei etwa 16 bis 17 s, ohne dass ein Stein sie verkürzt: weitere Quellen wirken (Abklingzeit-Wert des Charakters, Daevanion-Knoten, Ausrüstung, Skill-Stufe).
- Steine (`SpecializedSkill`, Texte `*_specialized_skill_desc`): 138 von 1503 Texten ändern Abklingzeiten, bei allen acht Klassen. Fest: "-30s cooldown", "-5s", "-1min", "+3% all skill cooldowns". Im Kampf: "-Xs [Skill] cooldown on hit", "Resets cooldown on defeating an enemy / on Critical Hit / on Evasion". Die Tabelle allein sagt die Zeit daher nicht voraus.
- Umsetzung im Assist: `Aion2Cooldowns.Left` teilt die Zahl durch die aus den letzten Zahlen desselben Skills gemessene Geschwindigkeit (Gerade durch die letzten bis zu 8 Werte, bei einem Sprung nur die Werte danach; ab zwei Werten im Abstand von mindestens 0,5 s), davor durch 2400. Gegen die Aufnahme gemessen sinkt der mittlere Fehler der Restzeit von etwa 24 % (fester Faktor) auf etwa 16 %, der 90-%-Wert von 67 % auf 43 %. Exakt ist es nicht, die Anzeige behält das "~".
