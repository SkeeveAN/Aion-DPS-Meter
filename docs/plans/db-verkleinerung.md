# Einsatzplan: Datenbank verkleinern (Stand 19.11.2026)

Grundlage: Kopie der lokalen Datenbank vom 19.11. (2,06 GB, 4:04 Uhr von der VM). Alles hier ist auf der Kopie gemessen, nichts wurde am Live-Server getan.

## Ergebnis in einem Satz

Punkt 1 (Profil-Spalten gzippen) bringt **2,06 GB auf 1,37 GB (-691 MB, -34 %)**; Punkt 2 (Aufstellungen ausdünnen) hat **nichts zu tun**; Punkt 3 (Skill-Namen auslagern) wird **nicht gemacht**.

## Punkt 1: Profil-Spalten gzippen (gebaut)

Was: `player_profiles.skills_json`, `daevanion_json`, `gear_json` und `profile_setups.data_json` stehen künftig als gzip-BLOB in der Spalte, wenn der Text mindestens 256 Zeichen hat (kürzere Werte, vor allem `[]`, bleiben Klartext). `board_counts_json` bringt nichts (alle Werte unter 256 Zeichen, bleibt unverändert), `titles_json` ebenso.

Wie (Code im Worktree-Branch):
- `src/db/packedJson.ts`: `packJson`/`unpackJson` und der drizzle-Spaltentyp `packedJson` (Spalte bleibt TEXT, kein Schema-Umbau, keine drizzle-Migration). Lesen erkennt Klartext und gzip (Magic 1f 8b), Schreiben komprimiert (Level 9, Rundreise-Prüfung). Alt- und Neuzeilen leben nebeneinander.
- `src/db/schema.ts`: die vier Spalten benutzen `packedJson`. Alle Stellen, die über drizzle lesen/schreiben (profile.ts, setups.ts, guilds.ts, ncItems.ts, routes/setups.ts), brauchen keine Änderung.
- `src/db/client.ts`: SQL-Funktion `unpack_json(x)` (für Roh-SQL).
- `src/routes/bosses.ts`: das einzige Roh-SQL mit `json_each` über `skills_json` nutzt jetzt `json_each(unpack_json(skills_json))`. `stats/collect.ts` (`gear_json <> '[]'`) bleibt unverändert richtig, weil `[]` Klartext bleibt und ein BLOB immer ungleich `[]` ist.
- `src/packProfiles.ts` + `scripts/pack-profiles.ts`: Migration in Stapeln. Nur Zeilen, die noch Klartext sind (wiederaufnehmbar), jede Zeile nach dem Update zurückgelesen und verglichen, der Update greift nur, solange die Zeile noch den gelesenen Text hat (ein Live-Schreibvorgang gewinnt), ein Stapel = eine Transaktion. `--count` zählt nur, `--unpack` ist der Rückweg.
- `src/packedJson.test.ts` (in `npm test` eingetragen): 5 Tests (Schwelle, Schreiben über drizzle, SQL-Funktion + json_each, Setups, Migration/Wiederholung/Rückweg). `tsc` sauber, `npm test`: 386 von 387 grün; der eine rote Test (`privacyTexts.test.ts`, Dialogtext vs. Datenschutzseite) ist vom Hauptbaum und von dieser Änderung unabhängig (Dateien unberührt).

Messung auf der Kopie:
- 5 Spalten gepackt: skills 79.240 Zeilen, daevanion 78.935, gear 57.186, setups 114.881; **Dauer 20 s ohne Pausen**, 680 MB RAM.
- Rundreise: je 5.000 Zufallszeilen pro Spalte (25.000, davon 18.088 als gzip) vorher/nachher per SHA-256 des Textes verglichen: **0 Abweichungen**; `integrity_check` ok.
- Größe nach `VACUUM INTO` einer neuen Datei: 2.058.747.904 -> **1.367.740.416 Byte** (-691 MB). Mehr als die Schätzung (500 MB), weil die langen Werte auf Überlaufseiten lagen.
- Größen der Spalten danach: skills 19,5 MB, daevanion 49,7 MB, gear 15 MB, setups 51,5 MB (vorher 160 / 162 / 77 / 238).
- Lesen: `buildProfileView` und `setupsOfPlayer` auf der gepackten Kopie 2 bis 35 ms. `json_each` über **alle** 82.088 Profile: 1,4 s statt 0,9 s (die Boss-Seite liest nur den Teil im Bereich, also deutlich weniger).

### Reihenfolge und Befehle auf dem Live-Server

1. **Backend mit dem neuen Code deployen** (normaler Ablauf). Ab da schreibt er gzip und liest beides. Es ändert sich noch nichts an der Datei-Größe. Prüfen: eine Profilseite, eine Setups-Anzeige, die Boss-Klassenseite (Skill-Stufen), die Statistik-Seite laden.
2. **Zählen** (nur lesen): `cd /opt/dpsmeter/Backend && node_modules/.bin/tsx scripts/pack-profiles.ts --count` (erwartet etwa 79 k / 79 k / 57 k / 115 k Klartext-Zeilen).
3. **Packen**: `ionice -c3 nice -n 19 node_modules/.bin/tsx scripts/pack-profiles.ts 400 500` (Pause 400 ms, 500 Zeilen je Stapel; etwa 640 Stapel). Erwartete Laufzeit 10 bis 15 Minuten (Rechenzeit 20 s, der Rest sind Pausen und Plattenzugriffe). Es schreibt neue gzip-Werte (rund 135 MB) und gibt Seiten innerhalb der Datei frei, das sind wenige Plattenzugriffe je Stapel. Bei Last (die 200 IOPS) Pause erhöhen oder per Strg+C anhalten und später fortsetzen.
4. **Nachprüfen**: `--count` zeigt 0 Klartext-Zeilen (bis auf die Kurzen); eine Stichprobe von Profilseiten; Log des Backends auf Fehler beim Lesen.
5. **Datei schrumpfen**: Die Datei bleibt zunächst 2,06 GB groß (freie Seiten innerhalb). Der Sonntags-Cron (`dpsmeter-vacuum`, 4:40 Uhr) macht VACUUM, wenn mindestens 20 % der Seiten frei sind: nach dem Packen sind es etwa 33 %, er läuft also am Sonntag (22.11.) von selbst. Das ist die Stelle mit der Wartung: VACUUM liest und schreibt die ganze Datei (früher etwa 8 Minuten Stillstand, deshalb nachts). Die nächtliche Kopie für die VM (`VACUUM INTO`) und die Kopie auf dem PC sind schon am Tag nach dem Packen klein (1,37 GB).

### Rückweg

Wichtig, die Reihenfolge: Der alte Code kann gzip-Werte nicht lesen. Also **erst entpacken, dann Code zurückrollen**:
1. `scripts/pack-profiles.ts --unpack 400 500` (macht alle BLOBs wieder zu Klartext; läuft mit dem neuen Code, der beides liest).
2. Erst wenn `select count(*) from player_profiles where typeof(skills_json)='blob'` (und für die anderen Spalten) 0 ist, den alten Backend-Code deployen.
Ohne Rückweg: Der neue Code kann dauerhaft bleiben, auch wenn man nie packt.

### Risiken

- Wer die Datenbank außerhalb des Backends liest (eigene Skripte, ad-hoc `json_each`), sieht jetzt BLOBs: in Roh-SQL `unpack_json(spalte)` benutzen (nur in Verbindungen des Backends vorhanden, auf der VM/dem PC mit `sqlite3` oder Node ohne die Funktion: selbst mit `zlib.gunzipSync` lesen). Im Repo gibt es sonst keine Roh-SQL-Stelle auf diese Spalten (geprüft: nur bosses.ts und stats/collect.ts).
- Neue Schreibvorgänge sind etwas CPU-aufwendiger (gzip Level 9 auf einem Profil, ein paar Millisekunden).
- Der Cron-VACUUM am Sonntag hat die schon bekannte Stillstandszeit (siehe oben); alternativ vorher von Hand im Wartungsfenster.

## Punkt 2: Aufstellungen ausdünnen: nichts zu tun

Befund auf der Kopie: `profile_setups` hat 114.961 Zeilen für 57.513 Spieler, aber **genau eine Zeile je Spieler und Art** (57.449 "board" + 57.512 "skills"), keine einzige ersetzte (`superseded_at`) und keine mit Daumen. Das Design (setups.ts: "ein Stand ohne Daumen wird an Ort und Stelle überschrieben, nur bewertete Stände sammeln sich") hält die Geschichte schon klein; die "zwei pro Spieler" waren die zwei Arten, keine Historie. Es gibt also nichts auszudünnen (0 Zeilen, 0 MB bei jeder Regel von 4, 8 oder 13 Wochen), und eine Löschfunktion wäre ohne Wirkung. Die 238 MB sind Inhalt der aktuellen Stände, den Punkt 1 auf 51 MB drückt. (Man könnte den Inhalt der Setups ohne Daumen ganz sparen, weil er dem Profil gleicht; das brächte nur noch etwa 50 MB und macht das Lesen komplizierter: nicht empfohlen.)

## Punkt 3: Skill-Namen auslagern: nicht gemacht

`encounter_skill_usage` hat keine `skill_id`-Spalte: der Name (14 Zeichen im Schnitt, 3.432.682 Zeilen, 46,6 MB, nur 328 verschiedene) **ist** der Schlüssel, die Uploads liefern Namen, keine Nummern. Eine Auslagerung bräuchte eine neue Spalte plus eine Namenstabelle und Umbau von mindestens sechs Abfragen (u. a. `GROUP BY skill_name` über die 3,4 Millionen Zeilen in routes/bosses.ts, seo/pages.ts, profile.ts, matching/merge.ts) für etwa 35 bis 40 MB (knapp 2 %). Bei 200 IOPS ist der Nutzen zu klein für das Risiko. Übersprungen.

## Nicht erfasst (nur genannt)

- `encounter_timelines` (302 MB) sind schon gzip. `encounter_buff_usage.spans_json` (91 MB) lässt sich nicht weiter packen (Gzip macht es größer, gemessen 123 %).
- `uploads.raw_payload_json`: 3.735 Zeilen (175 MB), die noch nicht ins Archiv gewandert sind (warten auf Prüfung oder profil-only, bis 90 Tage). Das ist eigene Logik in uploadArchive.ts und hier unverändert.

## Erwartete Größe nachher

Hauptdatei 1,37 GB (vorher 2,06 GB), Archiv unverändert 0,91 GB.
