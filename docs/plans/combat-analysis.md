# Kampfanalyse: Datenlage, Vertrag zwischen Client, Server und Webseite

Stand 30.10.2026. Ziel: die Kampfseite erklärt, warum ein Versuch gut oder schlecht war. Jede Kennzahl entsteht aus Ereignissen des Datenstroms; nichts wird erfunden, nichts zu einer Gesamtnote verrechnet.

## 1. Was der Datenstrom sicher liefert

| Sachverhalt | Quelle | Stufe |
|---|---|---|
| Wer wen trifft (Quelle, Ziel, Betrag, Skill) | Frame 0x0438 | sicher |
| Boss oder Add | Ziel ist ein vom Server angekündigtes Monster; der Boss ist die Kampfzielentität, jedes andere Monster ein Add | sicher (Monster-Ankündigung) |
| Tod | HP-Frame 0x008d auf 0 und Kill-Frame 0x048d (99 % der Tode) | sicher |
| Wiederbelebung | HP-Frame 0x008d wieder über 0 | abgeleitet (Zeitpunkt des nächsten HP-Frames) |
| Heilungsempfänger | Ziel des 0x0438 mit Heil-Skill (92 % passen zur HP des Empfängers) | sicher |
| Überheilung | kein Feld bekannt, der Decoder liest nur den Betrag; nur aus HP-Sprüngen schätzbar | nicht verfügbar |
| Ziel eines Spielers (Boss-Fokus) | Zielwechsel-Frames 0x3238 (andere) / 0x3138 (eigener); Kamerawahl nicht sichtbar | abgeleitet |
| Buff-Beginn/Ende | Effekt-Frames 0x2a38/0x2b38/0x2c38 | sicher (Ende bei "ohne Ablaufzeit" abgeleitet) |
| Schaden anderer Spieler im Weltboss-Massenkampf | nur Trefferzahlen | geschätzt |

Heilungsempfänger-Matrix und Überheilung gehören nicht zu diesem Paket.

## 2. Upload (Client -> Server): `participants[].timeline.analysis`

Alles optional, alle Zeiten in Zehntelsekunden ab Start des hochgeladenen Fensters (wie die übrige Zeitachse), alle Beträge ganze Zahlen. Ein älterer Client sendet das Objekt nicht.

```
analysis: {
  // Schaden dieses Spielers am Kampfziel (Boss) je 5-Sekunden-Eimer: damage[i] = Sekunden 5i bis 5i+5. Höchstens 720 Einträge (1 Stunde).
  damage: number[]
  // Schaden dieses Spielers an anderen Monstern (Adds) im Fenster, gesamt.
  addDamage: number
  // Tot-Zeiten, flach (Beginn, Ende, Beginn, Ende ...). Ende = Wiederbelebung, sonst Ende des Fensters. Höchstens 80 Einträge.
  dead: number[]
  // Erlittener feindlicher Schaden nach Quelle und Skill. Höchstens 16 Zeilen, größte zuerst.
  taken: { from: "boss" | "add" | "other", skill: string, hits: number, total: number, max: number }[]
}
```

- `from`: "boss" = das Kampfziel, "add" = ein anderes angekündigtes Monster, "other" = alles andere.
- Selbstschaden, Schilde, Heilung zählen nicht.
- Das bestehende Feld `damageTaken` bleibt (nur Boss).
- `deaths` der Zeitachse bleibt unverändert (Zeitpunkte der Tode).

## 3. Server -> Webseite

Gespeichert wird `analysis` im bestehenden Zeitachsen-Blob des Teilnehmers (kein neues Schema). Unbekannte Altdaten = Feld fehlt.

`GET /api/encounters/:id/timeline` bekommt zusätzlich:

```
analysis: {
  bucketSeconds: 5,
  // Gruppenschaden am Boss je Eimer (Summe aller Spieler mit Analyse)
  groupDamage: number[],
  coverage: { players: number, withAnalysis: number },   // players = echte Spieler des Kampfes
  players: [{ participantId, name, className,
              damage: number[], addDamage: number,
              deadSeconds: number, aliveShare: number,   // 0..1 bezogen auf die Kampfdauer
              deaths: number,
              taken: [{ from, skill, hits, total, max }] }]
}
```

und je Eintrag in `players[].buffs[]` ein Feld `usable`:

```
usable: { seconds: number,        // Sekunden, in denen der Effekt auf dem Spieler lief
          dead: number,           // davon tot
          idle: number,           // davon lebend, aber ohne Skill-Einsatz innerhalb von 3 s um den Zeitpunkt
          active: number }        // seconds - dead - idle
```

Berechnet nur, wenn Spans und Tot-Zeiten (analysis.dead) bekannt sind. Nie als "Zusatzschaden" bezeichnen: bekannt ist nur, dass der Effekt lief und etwas geschah.

`GET /api/encounters/:id/summary` (Kennzahlen eines Versuchs für den Vergleich):

```
{ encounterId, bossId, durationSeconds, groupIDps, bossDamage, addDamage: number|null,
  deaths: number|null, aliveShare: number|null,        // aliveShare = lebendige Spielersekunden / Spielersekunden
  bossFocusShare: number|null,                          // onBoss / (onBoss + onOthers) über Spieler mit Fokusdaten
  buffUsableShare: number|null,                         // Summe active / Summe seconds über Gruppenbuffs (Geber = anderer Spieler)
  damageCurve: number[]|null,                           // kumulierter Boss-Schaden je 5 s
  bossHp: [seconds, permille][]|null,
  coverage: { players, withTimeline, withAnalysis } }
```

## 4. Datenqualität (Webseite)

Vier Stufen als Abzeichen mit Erklärung: **sicher**, **abgeleitet**, **geschätzt**, **nicht verfügbar**. Zuordnung der Kennzahlen siehe Tabelle in Abschnitt 1; geschätzte Zeilen (Weltboss-Massenkampf) bleiben "geschätzt". Eine Kennzahl, für die kein Spieler Daten hat, zeigt "nicht verfügbar" statt einer 0.

## 5. Grundsätze

- Kein Gesamtscore, keine Spielerbewertung, kein rDPS.
- Wenig Damage Taken ist nicht "gut", viel nicht "schlecht": nur aufschlüsseln.
- Add-Schaden getrennt zeigen, damit ein Add-Töter nicht schwach aussieht.
- Heilungsempfänger-Matrix, Überheilung, Boss-Warnungen, Mechanik-Fehler sind nicht Teil dieses Pakets.
