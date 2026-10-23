# Fertigkeits-, Ziel- und Kettenskill-Frames (Auswertung vom 23.10.2026)

> Von einem Auswerte-Agenten erstellt, Stichprobe 1838 (453 von 453 Frames passen zur Skill-Tabelle) vom Hauptentwickler nachgeprüft. Charakternamen durch Platzhalter ersetzt. Ergänzt [combat-frames.md](combat-frames.md).


Datenbasis: cur3.pkl (83.388 Frames, 29 min, Hauptcharakter; die Zeilen ab Index 80252 sind ein zweiter Login des Hauptcharakters mit PvP-/Arena-Kampf), 185 Login-Dumps (1,66 Mio. Frames), Client-Tabellen (Skill.json, GameShop*, ...).
Notation: Opcode = erste 2 Frame-Bytes (Hex), danach Nutzlast. `varint` = LEB128 wie im Decoder. `u32` = little endian.
Sicherheit: **[belegt]** = Aufbau und Deutung durch Zahlen gedeckt (Stichprobe genannt); **[stark]** = Deutung sehr wahrscheinlich, ein Detail offen; **[Hypothese]** = plausibel, nicht bewiesen; **[offen]**.

Skripte liegen im Scratchpad (b1.py ... b49.py, lib_B.py); alle mit `python3 -I` gelaufen, nichts im Repo geändert.

---------------------------------------------------------------------------------------------------

## 0. Kurzfassung (die wichtigsten Funde)

| Opcode | Bedeutung | Sicherheit |
|---|---|---|
| **1838** | **Kettenskill freigeschaltet**: `alter Skill -> neuer Skill`; neuer Skill hat in Skill.json `ChainSkillPrevSkillId == alter Skill`, Fenster `ChainSkillAvailableTime` = 3000 ms | belegt (603 von 603) |
| **1938** | **Kettenfenster zu**: Kettenskill X benutzt (541x sofort beim Cast) oder abgelaufen (77x, genau 3,05 s nach dem 1838) | belegt (618 Frames) |
| **0138** | **Skill-Einsatz-Quittung nur für den eigenen Spieler**: `00 00 00 | Skill` = akzeptiert; `00 cc 18 | Skill` = abgelehnt (Code `cc`) | belegt (Aufbau), Codes offen |
| **3138** | **eigenes Ziel gewechselt** (`00 00 | Ziel | 00 00`, Ziel 0 = gelöscht), unabhängig von Tab/Kamera | belegt (421 Wechsel, 0 Duplikate) |
| **3238** | **Ziel eines fremden Akteurs (Spieler UND Monster) gewechselt**: `Akteur | 00 | Ziel` (Ziel 0 = gelöscht); kommt direkt vor dessen 0238 | belegt (1173 Frames, 851 von Spielern) |
| **1338 / 0f38 / 1038** | **Skill-Liste des eigenen Charakters im Einzelsatz**: 1338 = Eintrag mit Stufe (ID, ID), 0f38 = Variante (Spezialisierungs-ID), 1038 = Variante entfernt; Login-Block und Preset-Wechsel | belegt |
| **2238** | **Ladungen** (AutoLoad-Skills): `n | Skill | Ladungen` | belegt (Aufbau), Zeitverhalten Hypothese |
| **2e37** | **Ausweichrolle / Verschiebung** eines anderen Akteurs mit Skill-ID `kl000101` (Dodge) + Start- und Verschiebevektor | belegt |
| **4a36** | **Stat-Update (nur Änderungen)** des eigenen Charakters, `u16 id, i32 Wert` wie 0x4936; 70 % leere Herzschläge | belegt (5186 von 5186 parsen) |
| **1c56 + 2656** | **Ausrüstung eines sichtbaren Spielers wurde verstärkt**: 1c56 = Vorher/Nachher-Datensatz, 2656 = `Entität, Art, Stufe alt, Stufe neu` (Art 0x3c = +1, 0x42/0x44 = unverändert) | belegt (Stufe stimmt 449 von 455) |
| **0056 / 0556 / 8ce2** | **Schnellleisten-Inhalt** (Skills mit Kontext-Skills, Tränke/Schriftrollen); 0056 = aktive Seite beim Login, 0556 = geänderte Slots (Preset-Wechsel), 8ce2 = alle 3 Seiten | belegt |
| **078c** | **Kauf-Zähler der Münz-Shops** (`Shop-ID | n | n x (Waren-ID, gekauft) | Serverzeit`) | belegt |
| **0e38** | **bewegte Fläche / Geschoss eines Monsterskills** mit Start- und Endposition | stark |
| **0795 / 0895** | **NPC-Zustandsblock** (derselbe Block wie am Ende des NPC-Spawns): 0895 setzt Zustand 2, 0795 nach 150 ms zurück auf 1 | belegt (Block), Zweck Hypothese |
| 3037 | Position + 2 u16 eines Akteurs, immer gefolgt von 3237 | Hypothese |
| 4437 | `Entität | 00 00`, Takt des eigenen Charakters bei Flug-/Energieänderung | Hypothese |
| 058d, 6d8d, 748d | Entitäts-Zustandsframes (siehe unten) | offen / Hypothese |
| 8c07 | **kommt in keinem Mitschnitt vor** (0 von 1,75 Mio. Frames); vermutlich Tippfehler für 078c | belegt (Fehlen) |
| 1136, 1536 | Verbindungs-/Login-Frames (Schlüsselblock, Sitzungstext) | belegt (Aufbau) |
| 0938, 0538 | bekannt (Windup / Treffer-Notiz bzw. DoT), hier nicht neu bearbeitet | - |

---------------------------------------------------------------------------------------------------

## 1. Skill-Kette: 0238 -> 0138 -> 1838 / 1938

Ausschnitt aus cur3 (PvP-Fenster, Hauptcharakter = Entität `a40e`), alles im selben 50-ms-Takt:

```
82408 0238 a40e00 | 2401a800 (11010340 Rending Blow) | 0b 02 a4..    eigener Cast
82409 1838 2401a800 01 b442ae00                                      Rending Blow -> Smashing Blow (11420340)
82410 0138 000000 2401a800                                           Quittung ok
82512 0238 ... 11420340 Smashing Blow (Kettenskill)
82513 1938 01 b442ae00                                               Fenster von Smashing Blow zu
82514 0138 000000 b442ae00
82611 0238 Rupture Strike (11030120)  82612 1838 11030120 -> 11040120 (Wrathful Strike)
82613 1938 01 11030120            Rupture Strike war selbst Kettenskill von Keen Strike -> sein Fenster endet
```

### 1838 Kettenskill freigeschaltet [belegt]
`1838 | alter Skill u32 | 01 | neuer Skill u32` (11 Byte).
- 603 von 603 Frames (185 Dumps + cur3): `Skill.json[neu].ChainSkillPrevSkillId == alter Skill`; jedes 1838 kommt innerhalb 300 ms nach dem 0238 des alten Skills (603/603).
- Das Tabellenfeld `ChainSkillAvailableTime` beträgt bei 1124 von 1158 Kettenskills 3000 ms; `ChainSkillActivateRate` 10000 = 100 %. Unser Beispielskill: Rending Blow 11010340 -> Smashing Blow 11420340 (PrevSkill 11010340, 3000 ms); Keen Strike 11020120 -> Rupture Strike 11030120 -> Wrathful Strike 11040120.
- Das Byte `01` ist in allen 603 Fällen 1 (vermutlich "aktivieren"; ein 0 wurde nie gesehen).
- Nur für den **eigenen** Spieler (kein Akteurfeld; kommt nach dem eigenen Cast).

### 1938 Kettenfenster geschlossen [belegt]
`1938 | 01 | Skill u32` (7 Byte).
- 618 Frames: 541 im selben ms wie der Cast des Skills (Kettenskill benutzt), 77 ohne Cast; bei diesen 72 prüfbaren war der Abstand zum 1838 des Skills 2,87 bis 3,05 s (Median 3,05 s) = `ChainSkillAvailableTime` 3000 ms + Takt. Also: **Ablauf nach 3 s**.
- Das Byte ist immer 1 (618/618).
- Anwendung: die Anzeige "Folgeskill bereit" kann aus 1838 starten und aus 1938 enden (ohne Zeitschätzung).

### 0138 Skill-Quittung des eigenen Spielers [belegt Aufbau, Codes offen]
`0138 | 00 cc dd | Skill u32` (9 Byte). 
- `00 00 00` (2042 Frames): Skill akzeptiert; 1866 davon stehen in derselben ms wie das 0238 desselben Skills (Rest: Cast liegt weiter zurück). Der Akteur dieses 0238 ist in allen 34 prüfbaren Dumps immer derselbe = **eigene Entität** (Nutzen: Erkennung der eigenen Entität ohne Namensframe).
- `00 cc 18` (74 Frames): abgelehnt. Fast alle (alle bis auf 2 von 74) liegen unter 500 ms hinter irgendeinem 0238 (Eingabe während der Animation) und ein Teil wiederholt sich im 50-ms-Takt (Taste gedrückt halten). Häufigkeit der Codes `cc` (Dumps+cur3, 74 Frames): 0x13 (30), 0x12 (17), 0x04 (8), 0x1e (7), 0x1b/0x44/0x1d/0x02/0x49 (je 2), 0x4d, 0x08 (je 1); Beispiel Dump 003: `0138 00 04 18 | 7c4ea800` (Rupture Strike) viermal in 0,3 s, jeweils direkt hinter `3b38 0000`.
- Deutung des `cc` als Fehlerart [Hypothese]: In der Tabelle ServerErrorMessage stehen die Skill-Fehler (`kSkill_InOtherSkill`, `kSkill_CoolTime`, `kSkill_NoMP` ...) in dieser Reihenfolge direkt hintereinander; `0x12/0x13` ("in anderer Fertigkeit") passt zu den Fällen "Taste während der Animation". Ein 1:1-Abgleich der Zahlen mit dem Enum ist nicht möglich (Enum-Werte stehen nicht im Client). Brauchbar nur als "abgelehnt, Skill X".

---------------------------------------------------------------------------------------------------

## 2. Ziel-Frames 3138 und 3238 [belegt]

```
3238 | Akteur (varint) | 00 | Ziel (varint)       Ziel 00 = Ziel gelöscht      Länge 6..8
3138 | 00 00 | Ziel (varint) | 00 00                Ziel 00 = gelöscht           Länge 7..9  (eigener Spieler, ohne Akteur)
```
Beispiele (cur3): `3238 a56a00 cb8602` (Monster a56a wählt Spieler cb8602), danach in derselben ms `0238 a56a00 ... cb8602`; `3238 a56a0000` (a56a verliert sein Ziel); `3138 0000 cb8602 0000` -> `0238 a40e00 d85fa900 04 02 cb8602` (Ruinous Blow auf cb8602).
Zahlen:
- 3238: 1173 Frames. Folgt im Abstand <40 Frames ein 0238 desselben Akteurs, hat es dasselbe Ziel: 635 von 641; 519 Frames haben Ziel 0 (gelöscht).
- **Nur bei Änderung**: 1015 "geändert", 152 "erstes Mal", 6 Duplikate; für 3138 421 geändert, 0 Duplikate.
- 851 der 3238-Akteure sind Spieler (vorher per 4536 gesehen), der Rest Monster/unbekannt. -> **Das ist der fehlende "aktuelle Ziel eines fremden Spielers"-Frame** (combat-frames.md sagt "kein Frame bekannt").
- 3138 deckt auch Ziele ohne Tab: von 252 Wechseln auf ein Ziel lagen bei 67 weder 3438 noch 3538 innerhalb 1 s davor (also Kamera-/Auto-Ziel), nur 3438: 45, 3538: 31, beide: 109.
- Das 3138-Ziel ist in 245 von 251 Fällen das Ziel des nächsten 0238.
Folge für den Client: "Ziel des Ziels" lässt sich für alle Akteure aus 3238 + 3138 ableiten, ohne den Umweg über das letzte 0238.

---------------------------------------------------------------------------------------------------

## 3. Skill-Einträge 1338, 0f38, 1038 und 0051 [belegt]

```
1338 | 01 | ID u32 | 01 | ID u32 | Stufe u8 | Basisstufe u8 | c u8 | 00 | a u8 | b u8 | 00 | Art u8        (20 Byte)
0f38 | 01 | ID u32 |                Stufe | Basisstufe | c | 00 | a | b | 00 | Art                          (15 Byte)
1038 | ID u32                                                                                                 (6 Byte)
```
Dieselben Bytes `Stufe Basisstufe c 00 a b` stehen im Eintrag des 0x0051-Skill-Frames (`01 | ID | Stufe | Basis | c 00 a b | 00 00 00 00 00`): bei 2104 von 2137 identisch (33 Abweichungen = später gelevelt).
- **Stufe - Basisstufe = c + a + b**: 2635 von 2635 Einträgen. Bonus-Summanden, Herkunft nicht geklärt (c ist 0 bis 4 und bei Maximalcharakter fast immer 4; a, b 0 bis 4). [Hypothese: Stufenboni aus Ausrüstung/Daevanion/Aufstieg]
- Beispiel Login: `1338 01 6023b300 01 6023b300 12 0a 04 00 02 02 00 0a` = Identify Weakness (11740000), Stufe 18, Basis 10, Boni 4+2+2 = 8. `0f38 01 2e01a800 11 0a 04 00 01 02 00 06` = Rending Blow Variante 11010350, Stufe 17.
- **Verwendung**
  - Login-Block (jeder Dump): 21 x 1338 (Basis-ID, ID == ID, alle `x0000`), dann 5 x 0f38, ein 2238, 11 x 0f38; gesamt 37 Skills = Skill-Liste. Direkt darauf 5ce2, Effekte.
  - Preset-Wechsel (92e2-Schwung, z. B. cur3 81464): je Skill entweder **nichts mehr/1338** (Variante unverändert: `1338 ID ID` mit der Varianten-ID, 68 Frames in den Dumps; Beispiel Overhead Slam 11170350, Ankle Slice 11200340) oder **1038 (alte Varianten-ID) + 0f38 (neue Varianten-ID)**; danach 0739 mit den Slots. Beispiel: `1038 d027a800` (11020240) + `0f38 .. 5827a800` (11020120) = Keen Strike wechselt Variante; der Login hatte 11020240.
  - Varianten-ID = Basis-ID + (Kombination der gewählten Steine): Rending Blow Preset A Steine (3,5) -> 11010350, Preset B (3,4) -> 11010340 (belegt an 0039/0739); die Regel ist nicht für alle Skills gleich (Paar-Regel "Basis+100*min+10*max der Slots 1+2" passt nur bei 365 von 679 Fällen; Skills mit niedriger Skillstufe/Stigmen weichen ab). **[offen]**
- Letztes Byte `Art`: bei Basis-ID `0a` (10), bei Varianten `06` (6), bei Stigmen/Heil-Kettenskills anderer Klassen 3 oder 5 (Klasse 14/16/17/18; Skill.json: `bShowChainSkillHudUI = true`, SkillLvMax 25, lange Abklingzeit). In 0f38 für Basis-ID `02` (2 Fälle). Bedeutung [offen], aber vom Skilltyp bestimmt, nicht von der Klasse.

### 2238 Ladungen [belegt Aufbau]
`2238 | n u8 | n x (Skill u32 | Ladungen u8)` (n=1: 7 Byte).
- Login: `2238 01 8486a900 02` direkt nach `0f38 .. 8486a9..` (Focused Block 11110020); Skill.json: `AutoLoadType = Time`, `AutoLoadCount = 2`, `AutoLoadTimeList [20000, 20000]`. Über alle Dumps: bei n=1 ist die Ladungszahl == `AutoLoadCount` in 286 von 288 Frames (2 und 3 Ladungen); Frames mit n=3 und n=2 (kleinere Zahlen bzw. Werte um 130-248) sind nicht entschlüsselt und werden hier nicht gedeutet (vermutlich andere Satzform). Löst die Notiz "0x2238 vermutlich Ladungen" in cooldown-frames.md.

---------------------------------------------------------------------------------------------------

## 4. 4a36 Stat-Update [belegt]

`4a36 | Entität (varint) | n u8 | n x (id u16, Wert i32) | 8 Byte (meist 0)`
- 5186 von 5186 Frames in den Dumps parsen genau bis zum Ende; n = 0 in 3580 (Herzschlag, 13 Byte), sonst 1..N; die großen Frames (97/253/421 B beim Login/Zonenwechsel) enthalten die ganze Tabelle; die 8 Byte am Ende sind dort `654a000000000000` = 19045 = der Wert von Stat 193 (HPMax) [Hypothese: aktuelle LP], bei Updates 0.
- Entität ist praktisch immer der eigene Charakter (Entität nie vorher per 4136/4536 gesehen: 5177 von 5186); Hauptcharakter bekommt nach dem Wechsel die ID `a40e`.
- IDs (Häufigste in den Dumps; Namen aus build_species.py): 443 HardHit/Wucht (786x), 128 Critical (747), 196 MaxHPRatio (702), 44 AmplifyCriticalDamage (650), 426, 65, 205 (590, Name offen), 442 Perfect (582), 197, 282 CombatSpeed (539), 159 AbnormalResistance (536), 379 PvEAmplifyDamage (528), 7..17 Hauptwerte/Lord, 193 HPMax, 199 MPMax, 19 FixingDamage, 52 Defense, 31/33 Waffenschaden.
- Zeitlich: Updates folgen 50 ms nach Buff-Frames; cur3 82297 (`a40e 05 | 13 00 ef000000 | 34 00 b8090000 | cd 00 26070000 | f2 00 f07b0200 | f5 00 a00f0000 | 00*8`) kam 50 ms nach dem Cast `0238 a40e ... Speed Scroll` (22 10 103 Verbrauchsgegenstand) - Stat 19 (FixingDamage) stieg 0xef (239) auf 0x112 (274) beim Wechsel zurück. Damit ist 4a36 die "Stat-Änderung durch Buffs" - nutzbar, um Buff-Auswirkungen exakt abzulesen.

---------------------------------------------------------------------------------------------------

## 5. 1c56 und 2656: Verbesserung der Ausrüstung eines Spielers [belegt]

Ausrüstungs-Eintrag (33 Byte, so auch im 4536-Auftauchen-Frame): `Item u32 | Stufe u8 | Slot u8 | 13 Byte | 03 | 12 Byte | 0e`; die Item-IDs lösen gegen Item.json auf (z. B. 110630050 Aulamus Orb, 210330052 Vakron Helm, 210130052 Vakron Breastplate).

```
1c56 | Entität (varint) | Zähler u32 | 01 | Slot | Datensatz VORHER | Datensatz NACHHER        (77 Byte bei 1 Slot, 65/76/191/248/304/625 bei mehreren)
2656 | Entität (varint) | Art u8 | 03 | Stufe alt u8 | Stufe neu u8                              (immer 6 Byte hinter dem varint)
```
- Beispiel cur3: Spieler `8f60` ("Evil") mit Aulamus Orb: 1c56 `... a2149806 03 01 ...` und `... a2149806 04 01 ...`, 50 ms später `2656 8f60 3c 03 03 04`; die Reihe läuft Stufe 3->4->5->...->10 im Abstand 0,4 bis 1,2 s (cur3 12137..12431). Zweites Beispiel: Dump 079, `a3173c030506 ... 0809 ... 090a`.
- 1c56 mit Item `0` im Vorher-Datensatz: Anlegen (cur3 9246: leerer Slot 3 -> `64e88a0c` Stufe 10); mit zwei verschiedenen Item-IDs: Tausch (cur3 25948: `028e9606` Stufe 13 <-> `318e9606` Stufe 0).
- Zahlen 2656: 1104 Frames, Länge immer 6; **Art 0x3c (60): 1006 x, immer neu = alt + 1**; Art 0x42 (66): 84 x, immer alt == neu; Art 0x44 (68): 14 x, alt == neu. Bei Art 0x3c mit einem 1c56 derselben Entität in den 40 davor liegenden Frames (482 von 1006): Vorher-Stufe == alt in 455/456, Nachher-Stufe == neu in 455/456 (eine Ausnahme).
- Art 0x42/0x44 kommen NIE mit 1c56: Dump 030 (Charakter-B, Stufe 18): fünfmal `b02b 42 03 12 12` im Abstand 1,8 s, dann `b02b 3c 03 12 13` (+1). **Hypothese:** 0x42 = Verbesserungsversuch ohne Stufenänderung (Fehlversuch/Schutz), 0x44 = Fehlschlag mit anschließendem Stufenverlust (Dump 079: `c93f 44 03 01 01`, danach beginnt der Zähler wieder bei 0->1). Alle 2656-Entitäten sind Spieler.
- Die eigenen Verstärkungen (524 der 1006) haben kein 1c56 (nur 2656), vermutlich eigene Frames (0x1b56/0x8456).

---------------------------------------------------------------------------------------------------

## 6. Schnellleiste: 0056, 0556, 8ce2 [belegt]

```
0056 | n (0x12 = 18) | n Einträge:
   Skill-Eintrag (42 B): 01 | Slot | 13 x 00 | Skill A u32 | 04 | A u32 | B u32 | C u32 | 10 x 00
   Item-Eintrag  (25 B): 00 | Slot | 01 | 8 x 00 | Item u32 | 10 x 00
0556 | n | n Einträge (nur Skill-Einträge, 42 B)         87 B (n=2) oder 255 B (n=6)
8ce2 | Kopf (Liste 01..2d) | 3 Seiten (03 01 slot ..., 03 02 ..., 03 03 ...) | Stigma-Block | ...        1226 B
```
- 0056 (Login, 623 B): Slots 0,1,2,3,4,7 und 0x14..0x17 = Skills (Keen Strike, Rage Burst, Rending Blow, Leaping Slam/Rush Strike, Mocking Blade, Focused Block, Ruinous Blow, Crushing Wave, Ankle Slice/Aerial Snare, Sword Aura Rampage, Lunge Stance/Zikel's Blessing), Slots 0x0a..0x11 = Items (510116006 Superior Life Serum, 510126003 Mana Potion, 510146002 Superior Healing Potion, 531060003 Speed Scroll, 531060004 Courage Scroll, 531060027 Benediction Scroll ...). `QuickSlotData` hat Slots 1..20.
- B und C (zusätzliche Skill-IDs hinter A) sind "Kontext-Skills" der Quickslot (Beispiel Slot 1: A = 11020000? / B,C = Overhead Slam, Rending Blow); passt zu `PCContextSkillSlot.ContextChangeSkillList` [Hypothese für die Rolle].
- 0556 kommt nur im Preset-Wechsel-Schwung (cur3 81460, direkt vor `92e2`): `0556 06 | 01 01 ... | 01 04 | 01 05 | 01 07 | 01 15 | 01 16` = die 6 Slots, die sich ändern (Slot 1, 4, 5, 7, 21, 22).
- 8ce2 enthält die vollständigen 3 Seiten (Seite = Preset-Seite 1..3, je 24 Slots) plus Item-Slots. Bekannt als "skillbar"; der Aufbau passt zu 0056/0556.

---------------------------------------------------------------------------------------------------

## 7. 078c Kaufzähler der Münz-Shops [belegt]
`078c | 00 00 | Shop-ID u32 | n u8 | n x (Waren-ID u32, gekauft u32) | Serverzeit u64 (ms UTC)`
- Shop-ID 990007 = `coin_shop_festa` (GameShop.json), 990008 = `coin_shop_season`; Waren-IDs in GameShopGoods (z. B. 405015104 `shop_L_FieldEvent_Point_0001`, Daevanion-Punkt, Limit 10, 406023027 `shop_L_SeasonS1_Material_0004` Limit 10 pro Account).
- Gekauft-Werte stimmen mit den Limits (10 von 10, 15/15, 35/35; Hauptcharakter 2 von 15).
- Die Serverzeit entspricht der Aufnahmezeit (Dump 014: 11:08:52 MESZ = 09:08:52 UTC). Kommt auch mitten im Kampf (Kontext 0238/0438), d. h. beim Öffnen/Aktualisieren des Shops oder Kauf. Ergänzt die Notiz zu 0x8e56/0x8f56 (anderer Shop-Typ).

---------------------------------------------------------------------------------------------------

## 8. 2e37 Ausweichrolle / Verschiebung [belegt]

`2e37 | Entität (varint) | 73 | Skill u32 | 01 | x f32 | y f32 | z f32 | Blick-x f32 | Blick-y f32 | 00 00 00 00 | Wert f32 | Entität u32 | dx f32 | dy f32 | 00 00 00 00 | 01`
- 2958 Frames. Skill-ID liegt bei Byte 4 hinter dem varint; 2869 der 2958 Frames haben genau diese Länge 55 mit 2-Byte-Entität. Die Skill-IDs sind fast immer **Dodge**: `kl000101` (Klasse 11..18; 456/431/418/338/331/298/284/174 Frames) und `kl000100` (14..40 je Klasse); Rest (30 Frames): Monsterskills "Attack"/"Rapid Slice"/"Ankle Slice", Byte 4 = `33`.
- (dx, dy) hat immer die Länge 100,0 (Rollstrecke 1 m; z. B. (-74,08 | 67,17), (-52,77 | 84,94)); das Blickpaar ist ein Einheitsvektor. Das u32 hinter dem Wert ist wieder die Entität. Der f32 Wert (18.000..43.000) ist [offen].
- Beispiel: `2e37 8411 73 a5660301 01 d3ac7f47 e8148886.. ` -> Entität 2180, Skill 17000101 (Dodge), Position (65452,8 | 17418,5 | 31485,9). Für die Rolle des Spielers selbst ist das Ausweich-Cast 11000100/11000101 in 0238.
- Nutzbar für Karte/Positions-Verfolgung anderer Spieler; kein Schaden.

---------------------------------------------------------------------------------------------------

## 9. 0e38 bewegte Fläche / Geschoss [stark]
```
0e38 | Akteur varint | 02 | Ziel varint (00 = keins) | Skill u32 | Folge u8 | ID u32 | x1 f32 | y1 f32 | z1 f32 | Schritt u32 | Schritt u16 | 01 | x2 f32 | y2 f32 | z2 f32       (47/48 Byte)
```
- Beispiel Dump 009: `0e38 b2b002 02 00 7a771800 05 c5d5d207 | 93fd7246 0b9ba8c6 06da7644 | 02000000 0200 01 | 32c77446 1616a8c6 776c7b44` -> Akteur b2b002, Skill 1603450 (Monster-Skill "Attack"), Start (15.551 | -21.581 | 984), Ende (15.552+...). cur3 83101: Akteur 9beb02, Ziel a40e (Hauptcharakter), Skill 1216360, Start (-38.823 | 20.958 | 10.112) -> Ende (-38.861 | 20.951 | 10.112). Aufeinanderfolgende Frames desselben Akteurs wandern die Positionen weiter, Folge-Byte zählt hoch (05, 09, 0b, 0c, 0d, 10) und `Schritt` ist 1/2.
- Passt zur Notiz "bewegte Fläche (0e38)" in combat-frames.md; Details (ID u32, Schritt) offen.

---------------------------------------------------------------------------------------------------

## 10. 0795 / 0895 NPC-Zustandsblock [Block belegt, Zweck Hypothese]
`0795 | NPC-Entität (varint) | Block (9 Byte: b1 04 00 00 01 00 00 00 00)`, `0895` gleich mit Zustand `02`.
- Der Block ist **bit-identisch mit dem letzten 9-Byte-Block des NPC-Spawn-Frames 4136** der selben Entität: 659 von 659 geprüften 0795 (nach `...25 00 00 00 | 12 04 00 00 01 00 00 00 00 | ...`).
- Reihenfolge: 0895 (Zustand 2) -> nach median 0,150 s 0795 (Zustand 1): 1587 Paare, 89 0795 ohne vorheriges 0895 (Aufnahme beginnt mittendrin). Betroffene NPCs sind friedliche (Villager 2930807, Tarbeon 2939172, Legion Base Resident, Dawn Legionnaire, Händler ...); `b1` ist pro NPC-Typ konstant (Villager 0xe4, Tarbeon 0x12, Mercenary 0x1e), das zweite Byte `04`/`0c`/`27`/`0f` ebenso.
- Deutung [Hypothese]: kurzer Zustandswechsel des NPC (Sprechblase/Idle-Aktion; NpcBehaviorCase hat Idle/Talk-Einträge). Bei jedem 0895 kein weiterer Kampfbezug.

---------------------------------------------------------------------------------------------------

## 11. Übrige Opcodes

### 3037 [Hypothese]
`3037 | Entität varint | x f32 | y f32 | z f32 | u16 | u16 | 01` (21 Byte), 108 Frames; in 106 von 108 direkt (nächster Frame) gefolgt von `3237 | Entität | 08 00 00 ...` (Positionsfamilie 1a37..3237, siehe position-frames.md); beide u16 gleich in 83/108. Position stimmt mit Spielerkoordinaten (z ca. 31.488). Wahrscheinlich Start eines Skills mit Positionsabgleich eines Spielers (Cast `0238` kommt kurz davor).

### 4437 [Hypothese]
`4437 | Entität varint | 00 00` (5-6 Byte), 282 Frames, immer die eigene Entität. Folgt auf `0036/1d37`, danach `008d a40e 01 01 06 | u32` (Flugenergie fällt in 750er-Schritten) und `4637 | Entität | f32` (Höhe, Doku position-frames.md). Am Ende von cur3 (10:56:25.5) jede 50 ms gemeinsam mit `3b38 0000`/`3d38 0000`. -> Takt-Frame der Flug-/Energieanzeige, kein Skillbezug.

### 3b38 und 3d38 (nicht in der Aufgabenliste, aber auffällig)
`3b38 | 00 00` und `3d38 | 00 00` (je 4 Byte), je ca. 2190 in den Dumps; in cur3 erst ab 10:56:19.478 (nach dem ersten Kill/Sterben im Kampf), dann alle 0,5 s, am Ende alle 50 ms. Gehen auch dem 0138-Fehlerframe unmittelbar voraus (Dump 003: `3b38 0000` -> `0138 00 04 18 ...`). Zweck [offen] (Kampfzustand/Takt).

### 058d, 6d8d [offen]
`058d | Entität varint`, `6d8d | Entität varint` (je 5 Byte). Die Entität wurde per 3436 (Env-Spawn) gesehen. Sequenz (sehr häufig): `3836 | Entität | 3 B`, `3a36 | Entität | 2 B | 8 x 00`, `6d8d | Entität`, `3b36 | Entität` (z. B. 002-Hauptcharakter Index 1196-1199: Entität 23616 `c0b801`; 176 mal in der Reihenfolge 0238 -> 3836 -> 3a36 -> 6d8d -> 3b36 -> 0238), oder `... 6d8d, 058d`. Die Entität ist in nur 33 von 2662 Fällen Ziel des Treffers davor -> kein Bezug zu Skill/Treffer, eher Sicht-/Interaktionskette einer Umgebungsentität. Nicht weiter gelöst.

### 748d [offen]
`748d | Entität varint | 00 | 05 | X varint | Typ u8` (8/10 Byte; X = 0 oder ein Wert wie 17329/32842/26577/29280/36735; Typ 1..5). Betrifft in 100 % der gesehenen Fälle Spieler (4536). X ist pro Mitschnitt für viele Spieler gleich (z. B. 32842 in Dump 001 57 x) und kein Tabellenwert -> eher Gruppen-/Legions-/Marker-Nummer. Zweck [offen].

### 1136, 1536 (Verbindungsaufbau) [belegt Aufbau]
- `1136 | 00 00 | 60 ea 00 00 | 80 02 | 256 B hoher Entropie | 00*8 | f9 ff ff ff` (278 B): Schlüssel-/Nonce-Block der Verbindung (neue TCP-Verbindung beginnt damit; Doku 11.10.).
- `1536 | 00 00 | 04 u40 (ms Epoche, a1 01 00 00 04 31 f9 67) | 04 u40 (ms Epoche) | 00 04 00 | 2b | "457427:27E75CFA-3C2D-4284-9417-A25134DFD7B3" | 04 "3304" | 04 "5301" | 03 1e 00` (80 B): zwei Zeitstempel (ms, Oktober 2026), Sitzungs-Text (Konto-ID : GUID), zwei 4-stellige Zahlenstrings (Server-/Welt-ID, vgl. Notiz "Login 1. String = Server-ID"). Nicht skillbezogen.

### 0938 / 0538 / 0338
Bekannt; hier unverändert. Hinweis zu 0938 in cur3: Windup-Dauer-Varint wie in combat-frames.md; keine Änderung am Aufbau gefunden.

---------------------------------------------------------------------------------------------------

## 12. Zeitliche Korrelation Treffer / Cast (Ergänzung zum Auftrag)

Ablauf des eigenen Casts (cur3 82511..82514, 82408..82410), alle Frames im selben 50-ms-Paket:
`3138 (Ziel gewechselt, optional)` -> `0238 (Cast, Seq-Byte zählt hoch: 0b,0c,0d..)` -> `1838` (falls der Skill einen Kettenskill freischaltet) / `1938` (falls er selbst Kettenskill war) -> `0138 000000` -> danach `0438` Treffer 50-250 ms später, `0638 | Akteur | Skill | Seq | 00` (Skill-Ende) und im Fehlerfall `0138 00 cc 18`.
Fremde Akteure: `3238 (Ziel)` -> `0238` -> (bei Spielern) `2e37` für die Ausweichrolle; Windup `0938` vor dem Schaden bekannt.
Die Skill-ID in 1338/0f38/1038 ist immer die ID aus der Skill-Tabelle (Basis-ID oder Varianten-ID); im Gefecht trägt 0238/0438 dieselbe Varianten-ID (z. B. 11010340 in cur3 82408).

## 13. Was offen bleibt
- Bedeutung der Fehlercodes `cc` in 0138 (nur Häufigkeiten/Kontext).
- Letztes Byte `Art` in 1338/0f38 (10/6/3/5/2) und die Bonusbytes c/a/b (Quelle).
- Genaue Regel Steine -> Varianten-ID.
- 2656-Art 0x42/0x44 (Hypothese Fehlversuch), f32 in 2e37, Zähler/Schritt in 0e38, 748d-X, 6d8d/058d, 3b38/3d38, 3037/4437.
- 0138-Fehlerframes decken nicht den "Abklingzeit"-Fall ab (kein eindeutiger Code gefunden); für Abklingzeiten bleibt 0x4738 maßgeblich.
