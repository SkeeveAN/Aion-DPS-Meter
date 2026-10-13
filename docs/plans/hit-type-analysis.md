# Trefferarten: Perfect, Double, Back, Front (und Multi-Hit, Parade)

Branch: `feature/hit-type-analysis` · Stand: umgesetzt, Rücken/Front noch unbestätigt (siehe „Umsetzungsstand“ am Ende)

## Ziel

Die Skill-Tabelle eines Spielers zeigt zusätzlich zur Crit-Rate die Anteile **Perfect**, **Double** (Doppeltreffer), **Back** (Rückenangriff), **Front** (Frontalangriff), **Multi-Hit** (Anteil und Schaden) und **Parade**. Dazu Kennzahlen im Kopf (Multi-Hit-Rate, Perfect-Rate, Crit-Rate). Anzeige nur für Werte, die **belegt** sind.

## Ist-Stand

- Entschlüsselt: Crit (Typ-Byte == 3, [Aion2FrameDecoder.cs ~688](../../Client/Aion2/Protocol/Aion2FrameDecoder.cs)), Ausweichen (Skill 11000100 → `AvoidEvent`), die 4-Byte-**Trefferzahl 1..9** (gefunden, aber verworfen).
- Frame `0438`: Opcode | Ziel-Varint | **2 Flag-Bytes** | Akteur-Varint | Skill-ID u32 | Sequenz | **Typ-Byte** (2 = normal, 3 = Crit, 8 = andere Art, ~340 Frames, Schaden bei Marker+5 und noch falsch gelesen) | variabler Block | Trefferzahl u32 | 2 Bytes | **Treffer-Art-Varint** (übersprungen, [Zeile ~700](../../Client/Aion2/Protocol/Aion2FrameDecoder.cs)) | Schaden-Varint | Extra-Treffer-Zähler.
- `DamageEvent` kennt nur `IsCritical`, `IsTick`, `SkillId`. `AvoidKind` (Parry/Block/Resist/Dodge) existiert, der Avoid-Opcode ist unbelegt.
- Kandidaten, wo Back/Front/Perfect/Double stecken könnten: die zwei Flag-Bytes hinter dem Ziel, das Typ-Byte, das Treffer-Art-Varint, die Extra-Treffer-Zähler. **Es gibt dafür keinen belegten Befund.**
- DoT-Frames `0538`: etwa 7.000 Frames mit den Flags `0x0c, 0x0e, 0x30, 0x38, 0x4a` werden verworfen (ungeklärt); relevant, falls Treffer-Arten auch dort vorkommen.

## Phase 0 – Zerlegen und Gruppieren (keine Änderung am Meter)

1. Neuer Befehl `aion2-hitdump <jsonl> [port]`: schreibt je Treffer eine Zeile mit allen Roh-Feldern (Flag-Bytes, Typ, Block-Länge und -Bytes, Trefferzahl, Treffer-Art-Varint, Extra-Zähler, Schaden, Skill-ID, Akteur/Ziel-Typ).
2. Auswertung in einem Notizbuch/Skript (`Tools/hit-analysis/`): Häufigkeitsverteilung jedes Feldes, Zusammenhang mit Skill (z. B. Rückenangriff-Skills), mit Crit, mit dem Schadensverhältnis zu demselben Skill ohne Besonderheit (ein Back- oder Perfect-Treffer hat meist einen deutlich anderen Betrag).
3. **Quellen:** die Desktop-Mitschnitte und die Debug-Aufnahmen des Clients (siehe Datenquellen); dazu `fights.db`.
4. **Gezielter Testmitschnitt (Aktion für dich):** 3 Minuten an einer Trainingspuppe, jeweils ca. 1 Minute: nur von hinten, nur von vorn, nur von der Seite, dazu im selben Aufruf alle Skill-Arten. Daneben das Spiel-Kampflog oder ein Bildschirmfoto der Trefferanzeige („Perfect“, „Double“ …) für den Abgleich. Das macht die Zuordnung eindeutig.
5. **Gegenprobe über Position:** Für Rücken/Front die Blickrichtung des Ziels und die Position des Angreifers auswerten (Positionen: eigener Spieler `3c37`, Mob-Spawn `4136`, Blickwinkel in Cast-/Windup-Frames). Passt das Flag zur Geometrie, ist es bestätigt.

Ergebnis: `docs/protocol/hit-kinds.md` mit Feldlayout, Beispielbytes, Trefferquote der Bestätigung.

## Phase 1 – Decoder und Modell

- `DamageEvent` bekommt ein Feld `HitKind` (Flags: Back, Front, Perfect, Double, Parry, Block, …) und `HitCount` (Multi-Hit). Nur belegte Flags werden gesetzt; unbekannte Werte bleiben als „unbekannt“ erhalten (und werden im Debug-Dump gezählt), nie geraten.
- Selftests auf kleinen Auszügen echter Mitschnitte (Bytefolge → erwartetes Flag).
- Fehler beheben, wenn dabei auffällt: Typ 8 liest den Schaden falsch.

## Phase 2 – Anzeige und Upload

- `SkillBreakdown`/`SkillUsage` um Zähler je Trefferart erweitern; Spalten in [PlayerDetailsWindow](../../Client/Ui/PlayerDetailsWindow.xaml.cs) (ein-/ausblendbar).
- Upload-Paket und Backend-Schema (`uploadSchema.ts`): optionale Felder; Spieler-Seite der Webseite zeigt die Spalten, wenn Daten da sind.
- Hinweis: Crit/Back/Front gelten für den **eigenen** Client verlässlich; für andere Spieler nur, wenn die Flags im Strom für alle Akteure gleich geliefert werden (Phase 0 prüft das ausdrücklich an Fremd-Treffern).
- Texte in zehn Sprachen, README, Features-Seite, Changelog.

## Abnahme

- Auf dem Testmitschnitt stimmen Back/Front mit der bekannten Angriffsrichtung in ≥ 95 % der Treffer überein.
- Perfect/Double stimmen stichprobenartig mit der Anzeige im Spiel überein.
- Nicht belegte Arten erscheinen **nicht** in der Oberfläche.

## Risiken

- Die Arten könnten nur beim eigenen Spieler gesendet werden.
- Ein Teil der Informationen könnte aus mehreren Frames zusammengesetzt sein (Treffer + Nebenframe).

## Datenquellen

- **Desktop-Mitschnitte:** `C:\Users\ralf\Desktop\aion2\json\aion2-*.jsonl` (5.–11.10., rund 40 Dateien, 2,7 GB).
- **Debug-Aufnahmen des Clients:** `%APPDATA%\Aion DPS Meter\Debug\aion2-<Datum>-<Charakter>.jsonl` (22 Dateien, 540 MB, 11./12.10.). Gleiches Format: ein JSON pro TCP-Segment `{"T","From","To","Seq","Server","Data":base64}`, Server-Port 13328.
- **Gespeicherte Kämpfe:** `%APPDATA%\Aion DPS Meter\fights.db` (`events_gz`).
- Nur lesen, nie verändern; große Dateien streamen.

## Umsetzungsstand (13.10.2026)

**Fertig und geprüft (echte Frames im Selftest, drei Aufnahmen mit rund 170.000 Treffern):**

- Phase 0: Befund in [docs/protocol/hit-kinds.md](../protocol/hit-kinds.md) mit der Prüfung der Flags gegen den Schaden. **Doppelt (×2), Perfekt (×1,1) und Parade (×0,4) sind belegt**; Krit stimmt als Gegenprobe (×1,8). **Rücken/Front:** das Richtungs-Byte existiert, hat aber keinen gleichbleibenden Schadenseffekt - nicht bestätigt.
- Phase 1: `DamageEvent` trägt `Kinds` (null = keine Angabe, z. B. Heilungen, DoT, ältere Kämpfe) und `HitCount`; der Decoder liest sie (`ReadHitKinds`); `EventBlob` und `SessionFile` speichern sie (alte Dateien laden weiter, ohne Angabe).
- Phase 2 (Anzeige): Spalten Rücken / Front / Perfekt / Doppelt / Multi je Skill im Detailfenster (Anteil der Treffer, über die Treffer mit Angabe; „–“ ohne Angabe), Summenzeile, Hinweistext mit dem Vorbehalt zu Rücken/Front; zehn Sprachen; README (alle zehn).
- Werkzeug: `aion2-replay` gibt am Ende die Schadenswirkung jedes Flags aus (`HitKindStats`), damit sich jede Aufnahme prüfen lässt.

**Bewusst offen:**

- **Testmitschnitt von dir** (siehe Protokoll-Datei: 3 Minuten, je eine Minute von hinten / vorn / seitlich), um Rücken/Front eindeutig zuzuordnen; danach entscheidet sich, ob die Spalten bleiben oder umbenannt werden.
- Upload-Paket und Backend-Schema (optionale Felder) und die Spielerseite der Website: nicht gebaut; der Upload ist unverändert.
- Treffer-Arten für eingehenden Schaden (Parade-Quote des Spielers) als eigene Anzeige: nicht gebaut.
- Changelog-Eintrag und Features-Seite folgen beim Merge nach `main`.
