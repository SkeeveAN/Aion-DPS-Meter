# Offene Aufgaben und Vorhaben

Stand 23.10.2026. Gesammelt aus der Login-Dump-Analyse, den Fenster-Aufnahmen und den Wünschen des Projektleiters. Die Erkenntnisse zu den einzelnen Frames stehen in [protocol/](protocol/) (Einstieg: [protocol/opcode-katalog.md](protocol/opcode-katalog.md)). **Nichts davon ist gebaut oder veröffentlicht, solange hier nicht "umgesetzt" steht.** Releases macht der Projektleiter selbst frei; gebündelt, nicht einzeln.

## 0. Gesammelter Stand für das nächste Release (lokal gebaut, nicht committet)

- Login-Dump im Client abgeschaltet (Erfassung und Upload); der Schalter in den Einstellungen bleibt unverändert sichtbar. Alte Clients senden bis zum Update weiter.
- Doppel-Upload-Sperre: der Client sendet byte-gleiche Boss-Kämpfe einer Sitzung nicht erneut, der Server legt identische Payloads desselben Uploaders nicht noch einmal ab (Antwort `duplicate`).
- Timeline-Kompression (`encounter_timelines.data_gz`, Migration 0065), Packfenster der Uploads 6 statt 24 Stunden, `VACUUM` bei jedem Deploy (Dienst gestoppt, Platzprüfung).
- Server: Hanya (Live-DB) war unter Yustiel statt Kaisinel; erledigt am 23.10.

## 1. Neuentwicklung

Neue sichtbare Funktionen, Seiten und Tools.

### 1.1 Ingame-Rangliste auf der Webseite

**Idee:** Die Ranglisten des Spiels (Abyss, Albtraum, Transzendenz, Aszensusritus, Arena der Stille, Arena der Kooperation, je Klasse) auf aiondps.com anzeigen, mit Legion, Server, Punkten, Statistik und Rangänderung.

**Datenquelle (belegt):** Frame `0x558d`, bis zu 500 Einträge je Liste, kommt, wenn ein Spieler das Ranglistenfenster öffnet. Aufbau und Typen/Klassen in [protocol/login-und-fenster-frames.md](protocol/login-und-fenster-frames.md). Zusätzlich liefert `0x578d` den eigenen Rang je Inhalt. Eine Liste gilt je Fraktion und Region (alle Server-IDs einer Liste im Block 1301 bis 1323), nicht je Server.

**Entwurf:**
- Client: neuer Schalter "Ranglisten hochladen" (Standard aus, Erststart-Wizard/Datenschutzabschnitt anpassen). Beim Eintreffen eines `0x558d` die Liste (Typ, Klasse, Fraktion aus der Server-ID, Zeitpunkt, Einträge) an den Server senden; Größe 36 bis 90 KB je Liste, gzip drückt das auf einen Bruchteil.
- Server: Tabelle je (Typ, Klasse, Fraktion, Region) mit der neuesten Momentaufnahme plus Tagesstand; Plausibilitätsprüfung (Ränge lückenlos 1..n, Punkte absteigend, alle Server im selben Fraktionsblock, JSON-Felder vorhanden); bei mehreren Einsendern gewinnt die neueste vollständige Liste.
- Webseite: Seite "Ranglisten" mit Reitern je Inhalt, Klassenfilter, Suche, Legion/Server als Filter, Anzeige "Stand: vor x Stunden". Verknüpfung mit unseren Spielerseiten (Name + Server -> Spieler).

**Grenzen:** Der Client kann nichts anfordern (nur passiv mitlesen). Die Listen sind nur so aktuell, wie oft ein Nutzer das Fenster öffnet. Asmodier-Listen und NA-Regionen brauchen eigene Einsender. Namen in Ranglisten sind im Spiel öffentlich, trotzdem Opt-in und Datenschutztext ergänzen.

**Offen:** Rangstufen-Symbol bei Albtraum/Transzendenz/Aszensusritus (Regel nur beim Abyss bestätigt), Saisonbezeichnung, ob "Meine Klassen-Rang-Infos" (`0x578d`) gleich mit hochgeladen werden soll.

### 1.2 Client-Tool "Versorgungsgesuche" (täglich, wöchentlich, regulär) mit Verknüpfung zum Crafting

**Ist aufgenommen:** Frame `0x56e2` trägt je Charakter alle vier Tabs (Notfall = täglich, Wöchentlich, Regulär Herstellung, Regulär Boss) mit Gesuch-ID, Fortschritt und Frist; die Gesuche selbst stehen in `PeriodItemCollectionList` (Ziel, akzeptierte Gegenstände, Belohnung). Belegt gegen die Fenster. Die Gesuch-Texte stehen in den Client-Texten (`STR_PERIOD_COLLECTION_*`).

**Zahlen aus den Tabellen (Elyos):**
- Regulär Herstellung: 54 Gesuche, 171 von 228 akzeptierten Gegenständen sind mit einem Rezept herstellbar (Schmieden, Rüstung/Schneiderei, Juwelier, Alchemie, Kochen). Belohnungen bis 114.640 Abyss-Punkte je Gesuch.
- Notfall (täglich): 10 aktive Gesuche, davon im Beispieltag 10 von 26 Gegenständen herstellbar; der Rest sind Drops (Waffen/Rüstung aus Monstern).
- Wöchentlich: 10 aktive, 13 von 29 herstellbar; dazu Rohstoffe (Seelensteine, Rohhaut, Schmelzsteine).
- Regulär Boss: 54 Gesuche, 0 herstellbar (nur Boss-Drops) -> Fundorte über `ItemGetRoute` (Seele -> Monster -> Zone) anzeigen.

**Entwurf des Tools (Tools-Fenster, Muster Checkliste):**
1. Je Charakter (bis 40, wie die Checkliste) vier Reiter mit Fortschritt, Frist und Belohnung; abgeschlossene Gesuche abgehakt, neue/offene hervorgehoben.
2. Zu jedem Gesuch die akzeptierten Gegenstände mit Name und Symbol, getrennt in "herstellbar" und "Drop/Rohstoff".
3. Bei herstellbaren Gegenständen: das benötigte Rezept (nur gelernte, siehe 2.1), das Material je Stück und ein Knopf "in die Merkliste des Crafting-Rechners" (vorhandene `crafting.json`); der Rechner zeigt dann die fehlenden Materialien (bereits gebaut, Tiefe 6).
4. Bei Rohstoffen/Drops: Fundorte (`ItemGetRoute`) und, wo vorhanden, Verweis auf die Pet-Karte (Sammler/Gather-Pfeile).
5. Empfehlung "lohnt sich": Belohnung je Aufwand (Abyss-Punkte je benötigtem Gegenstand).

**Fortschrittsänderungen (geklärt 23.10.):** Bei der Abgabe kommen `0x3ee2` (neuer Fortschritt, absolut) und `0x3fe2` (Belohnung) in derselben Millisekunde, dazu `0x1b56` (Inventar); die Notfall-Gruppen-ID steigt täglich um 1. Damit ist ein Live-Tool möglich (Details in [protocol/login-und-fenster-frames.md](protocol/login-und-fenster-frames.md)). **Verhalten bei Neustart und Teilabgaben (Entwurf):**
- Der Stand kommt nur beim Login/Charakterwechsel; danach nur Änderungsframes. Das Tool muss den Stand je Charakter **lokal speichern** (wie die Checkliste ihre Zähler in `aion2-tickets.json`), mit Gruppen-IDs und Fristen.
- **Meter-Neustart bei laufendem Spiel** (Update, Absturz): der Login-Block fehlt, Abgaben während der Pause sind unbekannt. Das Tool zeigt dann den gespeicherten Stand mit Alter und Hinweis "möglicherweise veraltet; Charakter wechseln oder neu einloggen aktualisiert". Abgelaufene Fristen (täglich 09:00, wöchentlich Mittwoch 09:00) verwerfen den alten Fortschritt; die neuen Gesuche sind erst nach dem Einloggen bekannt.
- **PC aus (Spiel mit aus):** beim nächsten Login kommt der vollständige Stand, nichts geht verloren.
- **Teilabgaben (belegt 23.10.):** Jede Abgabe erzeugt `0x3ee2` mit **absolutem** Fortschritt (z. B. 4 von 5), ohne Belohnungsframe; erst der Abschluss bringt zusätzlich `0x3fe2`. Das Tool kann den Stand also direkt übernehmen und braucht keine Zähler-Rechnung.

**Offene Fragen:** (a) Abgaben im Boss-Reiter sind nicht aufgenommen (der Projektleiter kann dort derzeit nichts abgeben; vermutlich dieselben Frames, beim Bau als "unbelegt" behandeln und sobald möglich nachprüfen); Herstellung ist belegt, der Frame nennt sogar die abgegebene Item-ID. (b) Namen und Symbole der Gegenstände: Texte vorhanden, Symbole nur für einen Teil extrahiert. (c) Asmodier: Gruppen-IDs `64xxxxx` und Rezepte je Fraktion doppelt.

### 1.3 Saisonmissionen (Entscheidung offen)

**Ist aufgenommen:** Frame `0x8356` (Erfolge) enthält die Zähler aller Saisonmissionen (Gruppen `Group_Season_G1_L_*`, wöchentlich oder Saison über `ResetCycleType`), dazu die Wochenpunkte (3.440, Schwellen 1.500/3.000/4.500/6.000/8.000 für die Wochenbelohnung). Belegt gegen alle Fenster.

**Mögliche Umsetzungen (unentschieden):** Reiter in der Checkliste "Saison" (Stufe, Zähler, Frist je Inhalt) oder eigenes kleines Fenster; Titel der Missionen aus `AchievementString`; Hinweis, welche Mission als nächste fertig wird. Aufwand mittel; vor der Umsetzung klären, ob der Nutzen die zusätzliche Pflege rechtfertigt (die Missionen wechseln mit jeder Saison).

### 1.4 Arena-Statistik und Spielerprofil mit Arena-Verlauf

Siegesquote, Wertung und Verlauf (`0x2ae3`, `0x2ce3`, `0x558d`) je Klasse und Server; ein Spielerprofil mit Arena-Verlauf (letzte zehn Spiele mit Mitspielern und Gegnern). Entwurf, noch nicht ausgearbeitet.

### 1.5 Offizielle Rekorde je Boss und Dungeon

Rekorde je Boss/Dungeon (`0x37e2` Albtraum, `0x3193` Transzendenz, `0x3ee3` Aszensusritus) und Ranglisten (`0x558d`) liefern Stufe, Zeit und Punkte pro Spieler. Damit lassen sich Boss-Seiten um "beste Läufe laut Spiel" und die Klassenverteilung der Spitze erweitern. Als Gegenstück zu den Uploads gedacht; baut auf 1.1 auf. Entwurf.

### 1.6 Legionsseiten mit Spielwerten

Rang, Gegenstandsstufe, Kampfkraft und Online-Zeit aller Mitglieder auf Stufe 45 (`0x058a`) ohne NC-Abfrage; Legionszugehörigkeit sichtbarer Spieler (`0x338a`). Erweitert die bestehenden Legions-Seiten. Entwurf.

## 2. Verbesserungen bestehender Funktionen (aus den neuen Erkenntnissen)

Aufwand: klein = ein Frame auswerten und anzeigen; mittel = neue Ansicht; groß = neues Teilsystem. Alles ist Entwurf.

### 2.1 Crafting-Rechner: nur gelernte Rezepte zeigen

Beschlossen am 23.10.2026. Quelle: Frame `0x7e8d` (gelernte Herstellungsrezepte), Wandlungsrezepte `0x8c8d`. Die Listen ändern sich mit Patches (21.10.: acht Rezepte entfernt); keine feste Anzahl voraussetzen. Standard "nur meine Rezepte", solange Daten da sind, sonst alle. Offen: Aktualisierung beim Lernen eines Rezepts während der Sitzung (Änderungsframe unbekannt). Eng mit 1.2 verbunden (Gesuche zeigen nur lernbare/gelernte Rezepte).

### 2.2 Kampf-Analysator (Skill-Anzeige, Buffs, Debuffs, Replay)

| Idee | Frames | Aufwand |
|---|---|---|
| Kettenskills: Zeitfenster (3 s) in der Rotation zeigen und verpasste Ketten zählen | `1838`/`1938` | klein |
| Abgelehnte Eingaben (Skill wurde nicht ausgeführt, meist während einer Animation) als Zähler und Markierung in der Zeitachse | `0138` | klein |
| Ladungen von Skills mit Aufladung als Kurve neben dem Cooldown | `2238` | klein |
| Exakte Skillstufe und Variante je Spieler zum Kampfzeitpunkt (Skill-Anzeige) | `1338`/`0f38`/`1038`, `0051` | klein |
| Ziel fremder Spieler: wer greift wen an, Heiler-Ziele, "Ziel des Ziels" auch für andere | `3238`, `3138` | mittel |
| Replay-Karte: Bewegung der Gruppe und der Gegner ohne Annahmen (statt nur Spawn und Treffer) | `0x..37`-Positionsframes | mittel bis groß |
| Debuffs auf allen Beteiligten (bereits Buff-Frames) um Verstärkungsstufe/Gear ergänzen | `1c56`/`2656` | klein |
| Eigene Entität ohne Namensframe erkennen | `0138` (Quittung) | klein |

### 2.3 Assist-Anzeige

- Kettenskill-Hinweis ("Smashing Blow jetzt möglich", Restzeit 3 s) aus `1838`/`1938`.
- Aufladungen (`2238`), Schnellleisten-Inhalt (`0056`/`0556`/`8ce2`): nur Skills anzeigen, die auf der Leiste liegen; Preset-Wechsel erkennen (vorhanden).
- Hinweis "Eingabe abgelehnt" (`0138`) als Warnton/Signal.

### 2.4 Dummy-Tester

- Bericht um Kettenskill-Nutzung, abgelehnte Eingaben und Aufladungsverlust ergänzen (es sind die Gründe, warum der Dummy-DPS unter dem theoretischen liegt).
- Vergleich mit Klassen-Durchschnitt aus den Online-Statistiken (2.5), sobald dort belastbare Werte stehen.

### 2.5 Datenqualität und Spielerseiten (Online-Statistiken)

- **Datenqualität:** Die Frames `0x2ce3`, `0x558d`, `0x74e2`, `0x058a`, `0x338a` tragen die **Server-ID, Klasse und Legion direkt vom Spielserver**. Damit lassen sich falsche Server-Zuordnungen korrigieren (der Hanya-Fall: ein Client meldete Server 1305, die Gruppen-Frame-ID war falsch) und der NC-Abgleich entlasten. Das ist wahrscheinlich die wertvollste Datenverbesserung.
- **Spielerseiten:** Gegenstandsstufe und Kampfkraft aus der Legionsliste, Rang je Inhalt (`0x578d`).

### 2.6 Bestehende Online-Statistiken (Encounter-Seite und Boss-Statistik)

Gemeint sind die vorhandenen Seiten `aiondps.com/encounters/<id>` (Gruppen-Zeitleiste, Buffs je Skill) und `aiondps.com/bosses/<boss>/statistics` (iDPS je Klasse, Kampfzeiten der Kills, Anteil der Klassen, Skills je Klasse an diesem Boss, Filter Gear Score). Drei Stufen: **A** = ohne neue Frames machbar (Daten sind schon hochgeladen), **B** = neues Feld im Upload (Client, Schema, Datenschutz), **C** = größeres Teilsystem.

**Encounter-Seite**

| Idee | Stufe | Quelle |
|---|---|---|
| Kettenskills in der Zeitleiste: Kette genutzt oder verpasst (Skill X, Folgeskill innerhalb 3 s), Quote je Spieler und Skill | A | vorhandene Skill-Zeitleiste + Tabelle `Skill` (`ChainSkillPrevSkillId`, `ChainSkillAvailableTime` 3000 ms) |
| "Verworfene Eingaben" (Skill abgelehnt, meist während einer Animation) als Zähler und Markierung | B | `0x0138` |
| Fokus je Spieler: Zeit auf Boss, Adds, Zielwechsel | B | `0x3238` / `0x3138` |
| Ausweichrollen je Spieler | B | `0x2e37` (Skill `kl000101`) |
| Nahkampf-Uptime (Abstand zum Boss) und Bewegungsspur | B/C | `0x..37`-Positionsframes, nur eigene Gruppe, nie fremde Spieler |
| Skillstufe, Variante und Steine des Spielers zum Zeitpunkt des Kampfes neben den Skills | A | `presets_json`/`skills_json` im Profil (aktueller Stand), exakt zum Kampfzeitpunkt über `0x1338`/`0x0f38` (B) |
| Debuffs auf dem Boss als eigene Zeile (Dauer, Quelle) | prüfen | vorhandene Buff-Frames `0x2a38`/`0x2b38`/`0x2c38`, Upload-Umfang prüfen |

**Boss-Statistik**

| Idee | Stufe | Quelle |
|---|---|---|
| **Gear-Score-Abdeckung verbessern:** heute "bei 6 von 20 Läufen bekannt", weil nur der Inspect-Wert des Profils zählt. Die Legionsliste liefert Gegenstandsstufe und Kampfkraft aller Legionsmitglieder auf Stufe 45 **bei jedem Login**, ohne Fenster | B | `0x058a` (nur Legion, nicht Freunde) |
| Zweiter Filter "Kampfkraft" neben dem Gear Score | B | `0x058a`, NC `combat_power` |
| iDPS je Klasse getrennt nach Skill-Varianten/Steinen ("was spielen die Besten?") | A | `presets_json` der Profile |
| Skills je Klasse: Spalten "Kettenquote", "Ø Skillstufe", "abgelehnt" ergänzen | A/B | wie oben |
| Anteil der Klassen im Vergleich zur Klassenverteilung der Top-500-Listen ("Spielerbasis gegenüber Läufen") | B | `0x558d` |
| Offizielle Rekorde je Boss/Dungeon als Gegenprobe ("bester Lauf laut Spiel") für Albtraum, Transzendenz, Aszensusritus | B | `0x37e2`, `0x3193`, `0x3ee3`, Ranglisten |
| Statistik je Server und Fraktion sauber: Server-ID, Klasse und Legion direkt vom Spielserver statt Annahmen | B | `0x2ce3`, `0x558d`, `0x058a`, `0x338a` |

Reihenfolge-Empfehlung: zuerst alles der Stufe A (keine Protokolländerung, wenig Risiko), danach das Gear-Score-Feld aus der Legionsliste (größter Gewinn für den vorhandenen Filter), danach Fokus/Quittung/Positionen mit Datenschutzprüfung.

### 2.7 Interaktive Karte (Pet-Karte)

**Eigene Position besser bestimmen.** Für die eigene Entität gibt es **keine** laufenden Positionsframes (belegt auch in der neuen Aufnahme: nur Zonenframe, Korrekturen `0x3c37`, Effekt-/Treffer-Frames mit Position). Verbesserungswege:
1. Alle spärlichen Quellen zusammenführen und dazwischen interpolieren: Zonenframe (Spawn), `0x3c37` (Korrektur/Dash), die letzten 12 Byte der Effekt-Frames `0x2a38`/`0x2b38`, die Position im Treffer-Frame `0x0338` bei eigenen Angriffen.
2. Position aus der **Umgebung** ableiten (am 23.10.2026 an 1.653 Messpunkten geprüft, an denen die eigene Position aus Effekt-Frames exakt bekannt ist): Mittelwert aller lebenden Entitäten in Reichweite (NPC-Spawns `0x4136`, Position an Byte 14, Bewegungsframes, ohne Entfernte `0x4236`/`0x4736`) trifft die eigene Position im Median auf **9,9 m**, 90 % unter 20,8 m (bei 11 bis 30 Entitäten 7,7 m). Der letzte eigene Fix ist nach 5 bis 15 s im Mittel 68 m falsch, die Umgebungsschätzung ist dann deutlich besser; solange der letzte Fix jünger als 2 s ist (im Kampf der Normalfall, 1.634 von 1.689 Abständen), ist er auf 0,5 m genau. Spawn- und Entfernen-Ereignisse geschehen im Median bei **28 m** Abstand (Quartile 22 bis 32 m): eine Ring-Multilateration über diese Ereignisse (Radius 28 m) ist mit 14 bis 17 m Fehler **nicht** besser als der Mittelwert. Zeitliche Glättung über mehrere Sekunden verschlechtert das Ergebnis (Bewegung). Empfehlung: im Kampf die exakten Fixes nutzen, dazwischen und außerhalb des Kampfes die Umgebungsschätzung (grobe Genauigkeit etwa 10 m), beides mit einem einfachen Bewegungsmodell verbinden.
3. Bildschirmauswertung (Minikarte/Pfeil) als Ergänzung; nur wenn der Projektleiter das für diese Funktion ausdrücklich will.

**Wo sind die anderen Objekte.** Neu bekannt: Positionen und Deltas aller Spieler und Mobs in Reichweite ([protocol/bewegungs-frames.md](protocol/bewegungs-frames.md)). Anzeige-Ideen: Mobs live (Spawn `0x4136` plus Deltas statt statischer Position), Spielermarker (nur lokal, nie hochladen), Umgebungsobjekte `0x3436` (Sammelpunkte, Kibelisken) mit "wird gerade benutzt" (`0x3836`/`0x3a36`/`0x3b36`, Bedeutung Hypothese), Boss-Position für den Weltboss-Timer.

### 2.8 Weitere kleine Verbesserungen

- **Checkliste:** garantierte Belohnung je Dungeon (`0x1257`) als Balken, Arena-Versuche, Wochenpunkte, Saisonmissionen (1.3), Versorgungsgesuche (1.2).
- **Charakterfenster:** Flügel (`0x7256`), Skins (`0x5b8d`, Garderobe-Sammelwerte), Götterstein-Kombinationen (`0x3356`), Teleporter und Titel als "x von y", Erfolge-Gesamtfortschritt.
- **Upload-Plausibilität:** Rekorde und Ränge aus den Frames als Gegenprobe zu hochgeladenen Läufen.

Offene Entschlüsselungsfragen: [offene_entschluesselungsfragen.md](offene_entschluesselungsfragen.md). Die projektweiten Regeln stehen in der Projekt-Memory (`project_global_rules.md`).
