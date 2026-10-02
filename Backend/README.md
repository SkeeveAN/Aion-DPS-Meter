# Aion DPS-Meter Backend

API + minimalistisches Web-Frontend für `aiondps.com`. Nimmt Boss-Kampf-Uploads vom
[Aion DPS](../) DPS-Meter-Client entgegen, erkennt serverseitig, welche Uploads verschiedener
Gruppenmitglieder zum selben Kampf gehören (siehe `src/matching/merge.ts`), und zeigt Leaderboards
sowie Spielerprofile an.

## Stack

TypeScript + Fastify + better-sqlite3 + drizzle-orm + zod - bewusst identisch zum Schwesterprojekt
`../../timetable/apps/backend`, um Deployment-/Wartungswissen wiederzuverwenden. Kein Postgres:
bei den erwarteten Upload-Mengen (siehe Projekt-Notizen) ist SQLite/WAL im selben Prozess klar
ausreichend, siehe die Diskussion, die dieser Entscheidung vorausging.

Frontend: reines Vanilla-JS/HTML/CSS unter `../Web-Frontend/`, kein Build-Schritt. Die HTML-Seiten
kommen aus `src/routes/pages.ts` (Shell = `../Web-Frontend/index.html` mit pro Seite gefülltem `<head>` und einem
serverseitig gerenderten Inhaltsfragment, siehe `src/seo/`), Assets per `@fastify/static`, API
unter `/api/*`.

## Lokale Entwicklung

```bash
pnpm install
cp .env.example .env
pnpm run db:migrate     # Migrationen + Slug-Backfill (src/db/backfill.ts)
pnpm run db:seed        # "Unbekannt"-Bucket
pnpm run content:sync   # Aion-2-Referenzdaten aus src/data/aion2 in die DB
pnpm run dev            # http://127.0.0.1:4000
pnpm run test           # Matching- und Slug-Tests, node:test
```

## Spiel: nur `aion2`

Der Meter ist reines Aion 2 (das klassische Aion mit Chat.log wurde entfernt; der letzte Stand mit ihm liegt
auf dem Git-Branch `aion1-included`). Die `game`-Spalte in `server_catalog` und `instances` und der `?game=`-
Parameter bleiben, damit gespeicherte Zeilen und alte URLs weiter funktionieren - bedient wird aber nur
`aion2` (`src/constants.ts`: `isGame`, `DEFAULT_GAME`). Zeilen der alten Version (`game = 'aion'`) können noch
in der Datenbank liegen und werden nie ausgeliefert. Konsequenzen:

- Aion-2-Uploads schicken `bossNpcId` mit; `boss_npc_ids` löst das eindeutig auf, weil sich Bossnamen
  dungeonübergreifend wiederholen. Klassennamen werden gegen die neun Klassen in `src/constants.ts` geprüft.
- Server sind `aion2:<slug>` (aus der Server-ID im Charakterpaket des Clients); Spieler werden nur in solchen
  Servern gesucht.
- Jede API nimmt optional `?game=aion2`: `/api/instances`, `/api/instances/:idOrSlug/bosses`,
  `/api/bosses/:idOrSlug/leaderboard`, `/api/bosses/:idOrSlug/mechanics`, `/api/servers`,
  `/api/server-catalog`. Zusätzlich `POST /api/uploads/profiles` (nur Charakterprofile, ohne Kampf).

## URLs, Slugs und SEO

Jede Seite hat eine echte Adresse: `/`, `/download`, `/{game}/instances`,
`/{game}/instances/{slug}`, `/{game}/bosses/{slug}[?server={server-slug}]`, `/{game}/players/{id}`
(noindex). Slugs (`instances.slug`, `bosses.slug`, `server_catalog.slug`) werden nie von Hand
gesetzt: `src/db/backfill.ts` füllt sie nach jeder Migration aus dem englischen Namen
(`../Web-Frontend/game-data.js`, Fallback Rohname), Bosse mit gleichem Namen im selben Spiel bekommen den
Instanz-Slug angehängt. Ein einmal vergebener Slug bleibt auch bei Umbenennung stehen (URLs dürfen
nicht brechen); numerische Alt-URLs antworten mit 301 auf den Slug, alte `#/…`-Links leitet
`../Web-Frontend/app.js` beim Laden um. `robots.txt`/`sitemap.xml` kommen aus `src/routes/seo.ts`
(Spielerprofile, Encounters und Suche bleiben draußen). Serverseitige Dinge, die nicht im Repo
liegen (nginx-Redirects, Search Console), stehen in `deploy/NGINX.md`.

## Aion 2 Content (Instanzen, Bosse, Mechaniken)

Die Aion-2-Referenzdaten liegen als JSON in `src/data/aion2/` (`instances`, `bosses`, `mechanics`,
`classes`) und werden bei jedem Deploy per `pnpm run content:sync` (`src/content/syncAion2.ts`)
additiv in die DB geschrieben - Match über `(game, name)` bzw. `(instance, name)`, nie löschen,
handgesetzte Flags (`is_trash_mob`, `is_solo`, `loot_rules`) bleiben unberührt.

Erzeugt werden die JSONs mit `scripts/derive-aion2-content.ts` aus einem **nicht im Repo
liegenden** Drittanbieter-Datensatz:

```bash
pnpm run content:derive -- --source /pfad/zum/datensatz --review-out ../aion2.review.json
```

Übernommen werden ausschließlich Fakten: Namen wie der Spielclient sie anzeigt (en/ko/zh),
NPC-IDs, Rollen, Level, welche Mechanik zu welchem Boss gehört, Auslösertyp/-prozent und Schwere.
Fremde Texte (Auslöser-Labels, Handlungs- und Detailbeschreibungen, Positionsskizzen) und Bilder
werden **nicht** kopiert - die Felder `trigger.label`, `action`, `detail` starten leer, die
Review-Datei außerhalb des Repos dient nur als Lesestoff für eigene Formulierungen. Eigene Texte
und Übersetzungen (`name.de`/`name.fr`) direkt in den JSONs pflegen; ein erneuter `content:derive`
behält sie und aktualisiert nur die abgeleiteten Felder. Alle so entstandenen DB-Zeilen tragen
`source = 'derived'`; das Frontend blendet dazu einen Herkunftshinweis ein. Sobald eigene
Client-Daten verfügbar sind (EU/NA-Start), ersetzt ein eigener Extraktor den Drittanbieter-Input,
das Zielschema bleibt.

`better-sqlite3` braucht zum Kompilieren entweder einen vorgebauten Binary (üblich für
LTS-Node-Versionen wie 22) oder `make`/`gcc`/`python3` lokal installiert.

## Instanz-/Boss-Zuordnung

Instanzen und Bosse kommen aus `src/data/aion2` (siehe oben); die Namen in acht Sprachen aus den Texttabellen des
Spiels (`../Tools/aion2-dat`). Ein unbekannter Bossname landet beim ersten Upload automatisch in
der Instanz "Unbekannt / nicht zugeordnet" (siehe `src/db/seed.ts`, `src/matching/merge.ts`). Um
ihn einer echten Instanz zuzuordnen: in der `instances`-Tabelle die Zeile anlegen/finden und in
`bosses.instance_id` auf deren `id` umbiegen, z.B.:

```sql
UPDATE bosses SET instance_id = <echte instance id> WHERE name = '<Bossname>';
```

**Nie zwei `bosses`-Zeilen mit demselben `name` in verschiedenen Instanzen desselben Spiels
anlegen** - `resolveBossId` (`src/matching/merge.ts`) matcht innerhalb eines Spiels rein über den
Namen, ohne Zonen-/Instanz-Kontext (der klassische Client lädt keinen hoch) - bei zwei gleichnamigen
Zeilen landet JEDER Upload undeterministisch bei der ersten gefundenen, nie bei der "richtigen".
(Für Aion 2 gilt das nur für Uploads ohne `bossNpcId`, siehe "Spiele" oben.) Real passiert bei "Brigade
General Vasharti" (Rentus-Basis vs. Lost Rentus Base, siehe Migration 0022) - der Name existiert im
Spiel für beide Instanzen identisch, die App kann sie serverseitig nicht auseinanderhalten. Teilen
sich zwei Instanzen denselben Boss wirklich, gehört er in EINE `bosses`-Zeile (Instanz-Zuordnung so
wählen, wie die Gruppe ihn tatsächlich spielt), nicht in zwei.

Eine passende Instanz-Zeile fehlt noch? Erst per
`INSERT INTO instances (name, game, sort_order) VALUES (...)` anlegen (`slug`/`name_en` füllt der
nächste `db:migrate`-Lauf nach). Es gibt bewusst keine vorab geratene Instanzliste im Seed - die genaue Instanz-/Boss-Liste
dieses konkreten Servers ist von hier aus nicht zuverlässig bekannt, eine falsche Zuordnung wäre
schlimmer als eine leere.

## Solo-Bosse (Top 10 pro Klasse statt Top 10 Gruppen)

Ein Boss ist entweder ein echter Gruppenkampf (Standardfall - die Bossseite zeigt die Top 10
Gruppen) oder ein Solo-Übungsziel wie ein Training Dummy (die Bossseite zeigt stattdessen Top 10
pro Klasse). Es gibt keine automatische Erkennung dafür - genau wie bei Trash-Mobs manuell per SQL
markieren:

```sql
UPDATE bosses SET is_solo = 1 WHERE name = '<Bossname>';
```

## Loot-Regeln pflegen

`bosses.loot_rules` ist ein JSON-Array `{item, rule}[]`, das auf der Encounter-Detailseite als
"Loot-Tabelle (bekannte Regeln)" angezeigt wird. Wird nie aus Uploads abgeleitet (Loot ist
grundsätzlich kein Bestandteil eines Uploads, siehe `encounterParticipants` in `src/db/schema.ts`)
- rein manuell gepflegtes Referenzwissen:

```sql
UPDATE bosses
SET loot_rules = '[{"item":"<Item>","rule":"<Regel, z.B. \"1x pro Gruppe, Rolle: Need\">"}]'
WHERE name = '<Bossname>';
```

## Charakter-Umbenennungen (Spieler-Aliase pflegen)

Der Client kennt Spieler nur über den Namen - eine echte Umbenennung
("Alhamdulilah" → "Hidan") erzeugt sonst für immer zwei getrennte `players`-Zeilen für dieselbe
Person, ohne dass es je ein automatisches Signal dafür gäbe (das Spiel kündigt eine Umbenennung
nirgends an). `players.alias_names_normalized` ist deshalb, genau wie `bosses.npc_name_aliases`,
rein manuell gepflegtes Wissen - nie geraten:

```sql
UPDATE players
SET alias_names_normalized = '["<alter Name, klein geschrieben>"]'
WHERE name = '<Aktueller Name>' AND server_id = <ServerId>;
```

Mehrere alte Namen sind ein JSON-Array mit mehreren Einträgen. Ein Alias-Treffer aktualisiert nie
`name`/`name_normalized` der Zielzeile zurück auf den alten Namen - ein später erneut hochgeladenes
altes Chat.log darf den aktuellen Anzeigenamen nicht wieder zurückdrehen. Innerhalb EINES einzelnen
Uploads werden zwei Teilnehmer, die auf dieselbe `players`-Zeile auflösen (direkter Name-Treffer
oder Alias), automatisch zu einem einzigen Eintrag zusammengeführt (Schaden/Heilung addiert,
Skill-Listen gemerged) - siehe `mergeDuplicateParticipants` in `src/matching/merge.ts`.

## Deployment (alfahosting)

nginx ist bereits fertig konfiguriert: `aiondps.com` (Port 443) proxied auf
`127.0.0.1:4000`, inklusive `/ws`. Deployment folgt exakt dem Muster von
`../../timetable/deploy/`:

```bash
# einmalig, als root auf dem Server:
curl -fsSL https://raw.githubusercontent.com/SkeeveAN/Aion-DPS-Meter/main/Backend/deploy/dpsmeter_install \
  -o /usr/local/bin/dpsmeter_install
chmod +x /usr/local/bin/dpsmeter_install
dpsmeter_install

# danach, bei jedem Update:
dpsmeter_install
```

Legt einen eigenen Systemuser `aion-dpsmeter` an, checkt das Repo nach `/opt/dpsmeter` aus und
betreibt den Service unter `systemd` (`aion-dpsmeter-backend.service`).
