# Rotation, Buff-Laufzeiten und Stagger in der Spielerdetail-Ansicht

Branch: `feature/rotation-buffs-stagger` · Stand: umgesetzt (siehe „Umsetzungsstand“ am Ende)

## Ziel

Die Detailansicht eines Spielers (Doppelklick) bekommt drei neue Teile, ähnlich einer Kampf-Analyse mit Zeitachse:

1. **Rotation:** jeder benutzte Skill in Reihenfolge auf einer Zeitachse (Symbole), Buffs/Support oben, Schadensskills darunter, passive Procs zuletzt (abschaltbar). Skills, die weniger als 0,4 s nach dem vorigen kommen, eine Zeile tiefer („Treppe“). Darunter „Opener“ (die ersten Skills) und „Rotation in Kürze“ (Einsätze, pro Minute, Abstand, erster Einsatz, Schadensanteil).
2. **Buffs:** Laufzeit jedes Buffs auf dem Spieler (Anteil am Kampf, von wem, mehrfach gegebene zählen einmal).
3. **Stagger:** Balken des Bosses und seine Break-Phasen in der Zeitachse; geschätzter Anteil je Spieler (optional, als Schätzung gekennzeichnet).

Dazu eine DPS-Kurve hinter den Skills und eine Mauslinie mit Zeit und DPS.

## Abhängigkeit

Der **Protokoll-Teil** (Buff an/erneuert/Ende, Stagger, Cast-Ereignisse) entsteht in `feature/combat-overlay-cast-target` (Phase 0 und 1: `StatusTracker`, `CastTracker`). Dieser Branch baut die Auswertung und die Oberfläche darauf auf; Reihenfolge: zuerst der Protokoll-Teil dort, dann hier `git rebase`. Bis dahin lassen sich Layout und Auswertung mit Testdaten bauen.

## Ist-Stand

- `DamageEvent` kennt Zeit, Quelle, Ziel, Betrag, Skill, Skill-ID, Crit, DoT-Tick ([DamageEvent.cs](../../Client/Combat/DamageEvent.cs)); `SkillBreakdown` fasst pro Skill zusammen.
- Fensteroberfläche: [PlayerDetailsWindow.xaml(.cs)](../../Client/Ui/PlayerDetailsWindow.xaml.cs), DPS-Kurve ist vorhanden.
- Skill-Symbole: [Aion2SkillIcons.cs](../../Client/Aion2/Protocol/Aion2SkillIcons.cs), Namen `Aion2SkillNames.cs`. Es gibt eine Beobachtungsliste für Raid-Buffs (`rdps_watchlist.json`) und `NoteCast` für Klassen-Skills.
- Nicht vorhanden: Skill-Benutzungen als Ereignisliste, Buff-Spannen, Stagger.

## Kandidaten im Datenstrom (zu verifizieren)

- Skill-Benutzung von Spielern: `0238` (Cast), `0638` (Skill-Ende), `1b38`/`1e38` (Aufladen), `4738` (Abklingzeiten, Liste Skill + ms).
- Buffs: `2a38`/`2b38` an/erneuert (Ziel, Instanz-ID, Effekt-ID u32, Dauer −1 = unbegrenzt, Caster, erneuert-Flag), `2c38` Ende (Liste von Instanzen, Grund).
- Stagger: `05e0` (`03 01` = max + aktuell u32, `00 03` = Break). Dauer des Staggers: Standard 5 s, genauer aus einem Buff mit Pseudo-Skill 1.000.000 am Boss kurz nach dem Break.
- Das Spiel sendet **keine Cast-Zeiten**; unterbrochene oder verfehlte Skills lassen sich nur daran erkennen, dass ein sonst treffender Skill nichts getroffen hat.

## Umsetzung

1. **Ereignisse sammeln** (`SkillUseEvent`, `BuffSpan`, `StaggerEvent`) im `LiveAggregator` je Kampf, mit Obergrenzen (z. B. 20.000 je Liste) und in `fights.db` mitspeichern (zusätzliche gzip-Spalte; alte Kämpfe ohne Daten zeigen den Reiter leer).
2. **Auswertung** in einer reinen Klasse `RotationAnalysis` (testbar): Opener, Einsätze pro Minute, mittlerer Abstand, Schadensanteil; Buff-Uptime = Vereinigung der Spannen über die Kampfdauer (gleiche Buffs mehrerer Geber zählen einmal; Dauer-Buffs und Auren, die jede Sekunde erneuert werden, ausfiltern).
3. **Stagger-Zuordnung:** Der Balken sagt nur, um wie viel er fiel, nicht durch wen. Jede Abnahme dem unmittelbar vorangegangenen Treffer zuordnen, dessen Skill diesen Wert hat; Werte je Skill lernen und speichern (Tabelle `stagger.json`, Lernstand sichtbar als „Schätzung“).
4. **Oberfläche:** neue Reiter „Rotation“, „Buffs“ in der Detailansicht; Zeitachse als gezeichnete Fläche (`DrawingVisual`), Ziehen/Shift+Rad zum Bewegen, Strg+Rad zum Zoomen, Mauslinie, Hover-Fenster mit Details (Zeitpunkt, wievielter Einsatz, Abstand zum vorigen, Schaden/Heilung/Ziele, Debuffs, Buff-Geber).
5. Schalter in den Einstellungen: „Passive zeigen“, „DPS über Zeit“, „Stagger über Zeit“, Spalte „Stagger“ in der Liste (standardmäßig aus).
6. Texte in allen zehn Sprachen, README/Features-Seite, Bild per Render-Befehl, Changelog.

## Abnahme

- Auf einem Mitschnitt (`aion2-replay`) stimmen Anzahl und Reihenfolge der Skill-Einsätze mit dem Kampf überein (Stichprobe gegen das Spiel).
- Buff-Uptime gleicher Buffs mehrerer Geber wird nicht doppelt gezählt.
- Alte Kämpfe ohne Rotationsdaten brechen die Ansicht nicht.

## Risiken

- Buff- und Cast-Frames sind noch nicht belegt (siehe Abhängigkeit); ohne sie bleibt nur die Skill-Liste aus den Schadensereignissen.
- Speicherbedarf je Kampf: Obergrenzen und Komprimierung vorsehen.

## Datenquellen

- **Desktop-Mitschnitte:** `C:\Users\ralf\Desktop\aion2\json\aion2-*.jsonl` (5.–11.10., rund 40 Dateien, 2,7 GB).
- **Debug-Aufnahmen des Clients:** `%APPDATA%\Aion DPS Meter\Debug\aion2-<Datum>-<Charakter>.jsonl` (22 Dateien, 540 MB, 11./12.10.). Gleiches Format: ein JSON pro TCP-Segment `{"T","From","To","Seq","Server","Data":base64}`, Server-Port 13328.
- **Gespeicherte Kämpfe:** `%APPDATA%\Aion DPS Meter\fights.db` (`events_gz`).
- Nur lesen, nie verändern; große Dateien streamen.

## Umsetzungsstand (13.10.2026)

**Fertig und geprüft (Selftests, echte Aufnahme, gezeichnete Bilder):**

- Protokoll: Skill-Benutzung (`0238`), Effekt-Ereignisse (`2a38`/`2b38`/`2c38` als Roh-Ereignisse mit Ende) und Stagger (`05e0`, belegt) werden je Kampf mitgeschrieben (`Aion2CombatState`); Befund in [docs/protocol/combat-frames.md](../protocol/combat-frames.md).
- Speicherung: `FightExtras` mit dem Kampf in `fights.db` (neue Spalte `extras_gz`, bestehende Datenbanken werden beim Öffnen ergänzt); Verlauf (History-Modus) und Live-Ansicht liefern sie an das Detailfenster.
- Auswertung: `RotationAnalysis` (rein, getestet): Weaves als Treppe, Zählung je Skill (Skill-IDs mit gleichem Namen zusammengefasst), Opener, „Rotation in Kürze“, Effekt-Laufzeit als Vereinigung der Spannen (zwei Geber gleichzeitig zählen einmal), Stagger-Bänder.
- Oberfläche: Reiter **Skills / Rotation / Buffs** im Detailfenster; Zeitachse mit Symbolen, DPS-Kurve dahinter, Stagger-Bänder, Ziehen, Strg + Rad, Hover-Text; Tabellen; Schalter „Passive / DPS über Zeit / Stagger“.
- Texte in zehn Sprachen, README (alle zehn), Bilder; `render-details <png> [skills|rotation|buffs]` zeichnet die Ansicht.

**Bewusst nicht gebaut:**

- Stagger-Anteil je Spieler (Spalte und Linie): nur eine Schätzung möglich (der Frame nennt keinen Verursacher); bleibt eine Erweiterung.
- Debuff-/Buff-Details „von wem“ nur als Namensliste; „Ally boons“ als eigene Symbole in der Zeitachse sind nicht eingebaut.
- Das Ziehen und Zoomen der Zeitachse ist nicht automatisch getestet (echte Mausereignisse nötig); bitte einmal von Hand ansehen.
- Changelog-Eintrag und Features-Seite folgen beim Merge nach `main`.
