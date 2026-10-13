# Plausibilitätsprüfung für Boss-Uploads (Aion 2)

Branch: `feature/upload-plausibility` · Stand: Plan, noch nichts umgesetzt

## Ziel

Auffällige Kämpfe kommen nicht ungeprüft in die Bestenlisten. Heute stammen fast alle Uploads vom Entwickler selbst; sobald Fremde hochladen, schützt das die Listen vor Fehlern und Missbrauch.

Zielentwurf:
1. Jeder Kill wird mit anderen Läufen desselben Bosses/Schwierigkeitsgrades verglichen: Boss-Gesamt-HP gegen den Referenzbereich, Kampfzeit gegen die schnellste bekannte, DPS je Klasse gegen den Klassen-Median.
2. Auffällige Kills landen in einer **Prüfwarteschlange** und bleiben bis zur Freigabe von den Bestenlisten fern.
3. Gleiche Kills mehrerer Spieler zählen einmal.

## Was wir schon haben (Prüfung des Standes)

| Punkt | Stand |
|---|---|
| Form-Prüfung | Vorhanden, aber nur grobe Obergrenzen: [uploadSchema.ts:5-116](../../Backend/src/uploadSchema.ts) (Gesamtschaden ≤ 2e9, DPS ≤ 5e6, Boss-HP ≤ 1e11, ≤ 300 Teilnehmer, ≤ 80 Skills, Klasse aus `AION2_CLASSES`, `game` = `aion2`). `startedAt`/`endedAt` nur als nichtleere Zeichenketten, **ohne** Datum-, Reihenfolge- oder Dauerprüfung. |
| Upload-Route | [routes/uploads.ts:54-130](../../Backend/src/routes/uploads.ts): genau ein „ich“-Teilnehmer; Instanz-Bosse höchstens 20 verschiedene Namen (Weltboss ausgenommen); **Kill-Prüfung nach unten**: Summe des Gruppenschadens ≥ 0,9 × `bossMaxHp`, sonst `boss_not_killed`. **Kein oberer Wert** (50-facher Schaden geht durch), `bossMaxHp` kommt vom Client. |
| Client-Version | [clientPolicy.ts](../../Backend/src/clientPolicy.ts): neueste immer, ältere 72 h Schonfrist, unbekannte/Forks blockiert. Die Version ist aber nur eine vom Client gesendete Zeichenkette, also nicht fälschungssicher. |
| Rate-Limit | 120 Anfragen/Minute/IP auf `/api/uploads*` und `/api/feedback` ([server.ts:40-50](../../Backend/src/server.ts)). Keine Grenze je Installation/Charakter/Tag, keine Installations-ID. |
| Audit | Tabelle `uploads` mit gesalzenem IP-Hash, Client-Version, Roh-Payload; `status` kennt `pending`/`merged`/`rejected`, die Route schreibt nur `merged` und bei Ausnahme `rejected`. Abgewiesene Kills (`boss_not_killed`) werden nur ins Log geschrieben, nicht gespeichert. |
| Zusammenführen | [matching/merge.ts](../../Backend/src/matching/merge.ts): gleicher Server/Boss/Modus, Start und Ende innerhalb 20 s, Teilnehmer-Ähnlichkeit ≥ 0,5; höchste `bossMaxHp` gewinnt; Zeilen von Mitspielern werden nur überschrieben, wenn die vorhandene Zeile keine Selbstmeldung ist; `mergedUploadCount`. **Gleiche Kills zählen im Wesentlichen schon einmal.** |
| Boss-Katalog | `resolveBossId` ([merge.ts:139-180](../../Backend/src/matching/merge.ts)): NPC-ID, dann Name/Alias. Ein **unbekannter Boss wird nicht abgewiesen**, sondern in „Unbekannt / nicht zugeordnet“ angelegt (nur ausgeblendet). |
| Client-HP-Abgleich | [HpCheck.cs](../../Client/Combat/HpCheck.cs): vergleicht gezählten Schaden mit dem HP-Verlust aus den Server-Frames (Match/Missing/Excess, 1 % Toleranz), wird nur für Warnungen in der Oberfläche genutzt, **nicht** ans Backend gesendet. |
| Bestenlisten | `topGroups` ([routes/bosses.ts:94-112](../../Backend/src/routes/bosses.ts)) sortiert nach `groupIDps` ohne Status-/Freigabe-Filter. |

**Fehlt:** Referenzvergleich (HP-Bereich, schnellster Kill, Klassen-Median), Prüfwarteschlange und Freigabe, Dauer-/Reihenfolge-Prüfungen, Obergrenze des Gruppenschadens, Konflikterkennung bei widersprüchlichen Uploads, Installations-ID/Limits, Katalog-Sperre für unbekannte Bosse, HP-Abgleich auf dem Server.

## Umsetzung (kleinste Schritte zuerst)

**Schritt 1 – Formprüfung verschärfen** (`uploadSchema.ts`, `uploads.ts`): `endedAt > startedAt`, Datum gültig und nicht in der Zukunft, Dauer in sinnvollem Rahmen (Untergrenze absolut, Obergrenze), `dps ≈ totalDamage / Dauer` mit Toleranz, Gruppenschaden ≤ 1,3 × `bossMaxHp`. Abgewiesene Uploads mit Grund in `uploads` speichern (`status = rejected`, `reason`).

**Schritt 2 – Freigabestatus** (Drizzle-Migration): Spalten `review_state` (`approved` | `pending` | `rejected`, Standard `approved` für bestehende Zeilen) und `review_reason` an `encounters`; `encounterScope` und alle Bestenlisten-Abfragen filtern auf `approved`; `mergeIntoEncounter` bewertet nach jedem Zusammenführen neu. Vor der Migration Sicherung der Produktions-Datenbank mit `.backup` (nie `cp`, WAL-Modus).

**Schritt 3 – `assessKill(payload, referenz)`** (reine Funktion in `Backend/src`, mit Tests): Referenz aus SQL-Auswertungen über freigegebene Kills desselben Bosses/Modus (Median, schnellster Kill). Markiert als auffällig, wenn zum Beispiel:
- `bossMaxHp` außerhalb von 0,9–1,1 × Median,
- Gruppenschaden > 1,3 × `bossMaxHp`,
- Dauer < 0,6 × schnellster bekannter Kill oder unter einer absoluten Untergrenze,
- DPS eines Spielers > 2,5 × Klassen-Median.
Die Schwellen als Konstanten in `constants.ts`. **Wenige Referenzläufe** (< N): standardmäßig freigeben und im Log vermerken (solange das Projekt in der Beta ist), später `pending`.

**Schritt 4 – Freigabe-Oberfläche:** ein per Token geschützter Endpunkt `GET /api/admin/pending` und `POST /api/admin/encounters/:id/{approve|reject}`; schlichte Seite oder Skript zum Prüfen (nur Betreiber, Token nur in der Server-`.env`).

**Schritt 5 – Client liefert mehr Belege** (optional, Datenschutz beachten): `hpCheck` (Urteil, Verhältnis, gezählt vs. verloren, Overkill) und eine **zufällige Installations-ID** (je Installation einmal erzeugt, nicht personenbezogen, in der README erklärt) ins Upload-Paket; der Server kann damit eine zweite Meinung und Limits je Installation nutzen.

**Schritt 6 – Zusammenführen härten:** Ein erneuter Upload derselben Installation/desselben Charakters für denselben Kampf wird nicht doppelt gezählt; bei überlappenden Teilnehmerzeilen, deren Schaden sich um mehr als 2 % unterscheidet, einen Konflikt vermerken statt stillschweigend zu überschreiben. Unbekannte NPC-IDs: nicht mehr automatisch anlegen, sondern in die Prüfwarteschlange.

## Abnahme

- Eine Fälschung mit winziger `bossMaxHp` und winzigem Schaden oder mit 50-fachem Schaden wird nicht freigegeben.
- Ein normaler Kill (wie die bisherigen eigenen Uploads) bleibt freigegeben; die Bestenlisten ändern sich dadurch nicht.
- Prüfwarteschlange zeigt Grund und Werte; Freigabe/Ablehnung wirken sofort auf die Listen.
- Alle bestehenden Kämpfe bleiben nach der Migration freigegeben und sichtbar.

## Risiken

- Zu strenge Schwellen sperren echte Rekorde → Schwellen als Konstanten, Wirkung zuerst nur loggen („Trockenlauf“) und erst nach Sichtung der Zahlen scharf schalten.
- Migration auf der Produktions-Datenbank: Sicherung, Transaktion, danach Prüfung.
- Tests laufen auf dem Server (lokal fehlen die Build-Werkzeuge für `better-sqlite3`).
