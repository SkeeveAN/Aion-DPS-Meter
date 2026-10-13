# Replay-Karte: Bosskampf von oben abspielen

Branch: `feature/fight-replay-map` · Stand: Kern umgesetzt (frühe Version, siehe „Umsetzungsstand“ am Ende)

## Ziel

Im Detailfenster eines Boss-Kampfes in einer Instanz ein Reiter „Replay“: der Kampf von oben abgespielt. Boss (Blickrichtung, HP) und die eigene Gruppe Moment für Moment; angekündigte Angriffe (Telegraphen) füllen sich bernsteinfarben, blitzen rot beim Aufprall, wer getroffen wird, bekommt einen roten Ring. Unter der Karte eine Zeitachse mit großen Angriffen, Treffern, Toden und Stagger-Phasen, HP als feine Linie. Abspielen/Pause, Geschwindigkeiten ½× bis 4×, Karte ziehen und zoomen. Anfangs mit Kennzeichnung „Früher Test“.

Nur die eigene Gruppe, nie andere Spieler. Nur Boss-Kämpfe in Instanzen.

## Abhängigkeit

Braucht Daten, die im Branch `feature/combat-overlay-cast-target` (Phase 0 und 1: Windup, Fläche, Buffs/Stagger) und hier selbst (Positionen) entschlüsselt werden. Reihenfolge: dort zuerst die Protokollteile, hier darauf aufbauen.

## Ist-Stand

- **Positionen:** `0x3c37` (Entität, 03, x f32, y f32) ist entschlüsselt, aber nur der **eigene** Spieler wird behalten ([Aion2FrameDecoder.cs:993](../../Client/Aion2/Protocol/Aion2FrameDecoder.cs), `NoteLocalPosition`). Monster-Spawnpositionen (`4136`, x/y) und Umgebungsobjekte (`3436`, x/y/z) sind da. Fremde Positionsframes werden verworfen (Filter „IsLocalPlayer“). Positionen werden **nicht über die Zeit gespeichert**.
- Der DaevaMeter nennt weitere Positions-Opcodes: zweites Byte `0x37`, erstes Byte aus `{1a,1b,1c,1d,1e,1f,20,28,29,2a,2f}`, Entität als Varint, danach x/y als f32 (bei manchen 2 Bytes, bei `1f`/`2f` 1 Byte versetzt); `3c37` ist ein Dash. **Zu verifizieren** in unseren Mitschnitten.
- Weitere Positionsquellen im Spiel: Treffer-Position (`0338`), die Position des Casters in Buff-Frames, Gruppenstatus `1c92` (x/y), „Aim“-Block in Boss-Casts (Ziel des Angriffs).
- Karten: `Client/assets/aion2/maps` (Weltkarten). Für Instanzen gibt es keine Karte → Boden wird aus den gesehenen Positionen gelernt.
- Es gibt keinen Replay-Code.

## Datenmodell (eigen)

`FightReplay` je Kampf (JSON, gzip, mit dem Kampf in `fights.db` oder daneben): `t0`, `pos{entityId: {kind, pts:[t,x,y,…]}}`, `bosses[]`, `windups[t, actor, skill, ms, x, y, winkel]`, `aoes[…]`, `hits[t, actor, skill, ziel, schaden]`, `deaths`, `revives`, `hp[t, id, promille]`, `breaks`. Obergrenzen je Liste (~20.000, Windups ~5.000) und insgesamt (~4 Mio Punkte).

## Umsetzung

**Phase 0 – Daten finden (Mitschnitte, siehe Datenquellen):**
1. Prüfen, welche Positions-Opcodes im Strom tatsächlich vorkommen, wie oft (nicht nach festem Takt, sondern „bei Skills, Treffern, Korrekturen“), von wem.
2. Einheiten bestimmen (vermutlich Zentimeter; Boden-Zellengröße 1,5 m, Bossfläche ~8 m).
3. Prüfen: bekommt man die Positionen der Gruppenmitglieder zuverlässig; wie genau ist der eigene Standort, wenn das Spiel die eigene Bewegung nicht zurücksendet.

**Phase 1 – Aufzeichnung (`ReplayRecorder`):**
- Läuft nur bei aktivem Kampf mit Boss in einer Instanz. Ringpuffer von **100 s** je verfolgter Entität, damit die 5 s vor dem Pull nachgetragen werden können.
- Verfolgt: Gruppe und Bosse. Mindestabstand 100 ms (Boss) / 200 ms (Spieler).
- **Sprungfilter:** Bewegungen schneller als ~4,7 m/s (+Toleranz) werden ignoriert, außer sie werden binnen 500 Einheiten oder durch eine zweite Probe in 5 s bestätigt; Spieler weiter als 200 m vom Boss fallen weg.
- Boss-seitig sind: Boss selbst und Mobs binnen 60 m um den Boss.

**Phase 2 – Telegraphen und Flächen lernen:**
- Aus Windup/Aim/Fläche (Opcodes siehe `feature/combat-overlay-cast-target`) Position, Winkel und Dauer.
- **Flächengröße lernen:** Das Spiel sendet sie nicht. Für jeden Treffer eines Boss-Skills auf ein Gruppenmitglied dessen Abstand zum Ankerpunkt zur Trefferzeit (interpolierte Position, nur wenn eine Probe ≤ 1,5 s entfernt ist) sammeln; Radius = 110 % des 85. Perzentils (bei < 5 Werten Maximum) + 0,5 m, begrenzt auf 2..85 m. Lernstand je Skill über Kämpfe zusammenführen (gewichteter Mittelwert, n höchstens 50) und speichern. Ohne Daten Standardgrößen und **gestrichelt** zeichnen.
- Beim eigenen Standort ist die Quelle unsicher (das Spiel sendet sie nicht zurück): aus Effekten, die du setzt, und aus den Boss-Treffern auf dich; das Symbol wird blass, solange es unsicher ist.

**Phase 3 – Boden lernen:** Alle aufgezeichneten Positionen aller Entitäten in 1,5-m-Zellen je Karte zählen, über Kämpfe aufsummieren (Obergrenzen je Karte), in vier Helligkeitsstufen zeichnen; nach einigen Kämpfen zeigt sich der Umriss der Plattform.

**Phase 4 – Oberfläche:** neues `ReplayView` (Zeichnung über `DrawingVisual`), Boss mit Blickrichtung und HP-Linie, Spieler als Klassensymbole mit linearer Interpolation, Zeitachse mit Markern, Bedienung (Leertaste, ← → je 5 s, Ziehen/Rad/Doppelklick). In `PlayerDetailsWindow`/Kampfdetail als Reiter; für andere Kämpfe Hinweis „nur für Boss-Kämpfe in Instanzen“.

**Phase 5:** Texte in zehn Sprachen, README/Features, Changelog; Aufzeichnung abschaltbar (Datenmenge).

## Abnahme

- Auf einem Mitschnitt (`aion2-replay`) bewegen sich Gruppe und Boss plausibel (keine Sprünge durch die Karte); Telegraphen erscheinen zur richtigen Zeit.
- Gelernte Flächen konvergieren über mehrere Kämpfe desselben Bosses.
- Speicherbedarf je Kampf bleibt unter der Obergrenze, alte Kämpfe ohne Replay brechen nichts.

## Risiken

- Positionsframes anderer Spieler könnten fehlen oder zu selten kommen.
- Eigener Standort nur geschätzt.
- Datenmenge und Rechenzeit beim Speichern; deshalb Obergrenzen und gzip.

## Datenquellen

- **Desktop-Mitschnitte:** `C:\Users\ralf\Desktop\aion2\json\aion2-*.jsonl` (5.–11.10., rund 40 Dateien, 2,7 GB).
- **Debug-Aufnahmen des Clients:** `%APPDATA%\Aion DPS Meter\Debug\aion2-<Datum>-<Charakter>.jsonl` (22 Dateien, 540 MB, 11./12.10.). Gleiches Format: ein JSON pro TCP-Segment `{"T","From","To","Seq","Server","Data":base64}`, Server-Port 13328.
- **Gespeicherte Kämpfe:** `%APPDATA%\Aion DPS Meter\fights.db` (`events_gz`).
- Nur lesen, nie verändern; große Dateien streamen.

## Umsetzungsstand (13.10.2026)

**Fertig und geprüft (Selftests mit echten Frames, Aufnahmen, gezeichnete Bilder):**

- **Phase 0 (Daten):** Positionsframes anderer Entitäten stehen im Strom: Opcodes `1a37 1b37 1c37 1e37 20 37 2837 2937 2a37` (Position 2 Byte hinter der Entität), `1f37 2f37` (1 Byte) und `3c37` (andere Entitäten, bisher verworfen). Auf drei Aufnahmen bilden die Messwerte einer Entität Wege mit Geschwindigkeiten von wenigen Metern pro Sekunde; Einheit Zentimeter. **Kein fester Takt:** etwa ein Messwert je 1,4 s und Spieler (er kommt bei Bewegungen und Skills), die Wege werden deshalb gerade zwischen zwei Messwerten gezeichnet.
- **Phase 1 (Aufzeichnung):** `Aion2CombatState` führt Wege (alle 150 ms höchstens ein Punkt, Sprungfilter: ein Schritt über 35 m/s zählt erst, wenn die nächste Messung zustimmt), das Windup-Protokoll, Tode und die Gesundheit der Bosse; mit dem Kampf in `fights.db` gespeichert (nur Gruppe und Bosse).
- **Phase 2 (Flächen lernen):** Die Größe einer Boss-Fläche wird aus den Abständen der Getroffenen zum Windup-Ort gelernt (110 % des 85. Perzentils + 50 cm, ab fünf Treffern) und in `cast-skills.json` gemerkt; bis dahin 4,5 m gestrichelt.
- **Phase 4 (Oberfläche):** `ReplayView` (Karte: Gitter alle 10 m, Wege, Flächen, Boss mit Blickrichtung, Spieler, Treffer-Ring, Tote), `ReplayTimeline`, `FightReplayWindow` (Abspielen, Leertaste, ← →, 0,5-4×, Namen, Einpassen, Ziehen/Zoomen); Knopf „Replay“ im Spielerfenster; zehn Sprachen; README (alle zehn).
- Zeichenbefehl `render-replay <png> [Sekunde] [Maßstab]`.

**Bewusst offen:**

- **Phase 3 (Boden lernen)** (Umriss der Plattform aus allen gesehenen Positionen) ist nicht gebaut.
- Eigene Position: das Spiel schickt sie, aber bei seltenen Messwerten springt der Punkt; ein „blasses Symbol bei Unsicherheit“ gibt es nicht.
- **Nicht mit einer echten Instanz im Spiel angesehen** (ich starte nichts auf deinem Rechner); der Aufbau aus echten Aufnahmen ist getestet (Wege, Flächen), die Oberfläche mit Beispieldaten. Bitte einmal nach einem echten Bosskampf öffnen.
- Replay nur für Boss-Kämpfe in Instanzen einzuschränken ist nicht umgesetzt: es erscheint bei jedem Kampf, für den Wege gespeichert sind.
- Changelog-Eintrag und Features-Seite folgen beim Merge nach `main`.
