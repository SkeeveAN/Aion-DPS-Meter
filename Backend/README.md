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
pnpm run db:seed        # "Unbekannt"-Bucket je Spiel
pnpm run content:sync   # Aion-2-Referenzdaten aus src/data/aion2 in die DB
pnpm run dev            # http://127.0.0.1:4000
pnpm run test           # Matching- und Slug-Tests, node:test
```

## Spiele: `aion` und `aion2`

Seit Migration 0024 tragen `server_catalog` und `instances` eine `game`-Spalte (`aion` = klassischer
4.x-Client mit Chat.log-Meter, `aion2` = UE5-Client); `bosses`, `servers`, `players`, `encounters`
erben das Spiel über Instanz bzw. Server. Alles, was die Kennung nicht kennt (Clients bis 0.7.x,
alte URLs), meint `aion`. Konsequenzen:

- Instanznamen sind nur noch **pro Spiel** eindeutig ("Fire Temple" gibt es in beiden), den
  "Unbekannt"-Bucket gibt es einmal je Spiel, und `resolveBossId` (`src/matching/merge.ts`) matcht
  Bossnamen nur innerhalb des Spiels des Uploads - ein Aion-2-Upload landet nie auf einem
  klassischen Boss gleichen Namens.
- Aion-2-Uploads können `bossNpcId` mitschicken; `boss_npc_ids` löst das eindeutig auf, weil sich
  Aion-2-Bossnamen dungeonübergreifend wiederholen. Klassennamen werden für `aion2` gegen die neun
  Klassen in `src/constants.ts` geprüft (für `aion` bewusst nicht - private Server haben verschiedene
  Klassensets).
- Jede API nimmt `?game=` (Default `aion`): `/api/instances`, `/api/instances/:idOrSlug/bosses`,
  `/api/bosses/:idOrSlug/leaderboard`, `/api/bosses/:idOrSlug/mechanics`, `/api/servers`,
  `/api/server-catalog`.

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

Es gibt keine Instanz→Boss-Datenbank aus dem Client (`Client/Data/NpcDatabase.cs` kennt nur NPC-Namen,
keine Zone/Instanz-Zuordnung). Ein unbekannter Bossname landet beim ersten Upload automatisch in
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

## Welcher Server zeigt welche Instanzen

`instances`/`bosses` selbst bleiben unscoped (derselbe Kampf ist derselbe Kampf, egal welcher
Server ihn austrägt), aber welche Instanzen ein Server überhaupt ANBIETET, ist es laut dem Nutzer
nicht: Origin Aion und EuroAion (beide 4.6) teilen sich eine Liste, Aion Riftshade (4.8) hat eine
breitere. Das steuert `server_catalog_instances` (reine Zuordnungstabelle, `GET /api/instances`
filtert per `?serverCatalogId=`). Ein Server ohne Zeilen hier zeigt eine LEERE Instanzliste, nie
eine geratene - gleiche "leer schlägt falsch"-Regel wie oben. Neue Zuordnung anlegen:

```sql
INSERT INTO server_catalog_instances (server_catalog_id, instance_id)
SELECT sc.id, i.id FROM server_catalog sc, instances i
WHERE sc.name = '<Servername>' AND i.name = '<Instanzname>';
```

## Trash-Mobs ausblenden

Ein eindeutiger Trash-Mob wird automatisch schon beim Upload abgelehnt (siehe
`src/npc/trashMobs.ts`, aufgerufen aus `src/routes/uploads.ts`, `400 trash_mob_rejected`): jeder
Name, der im aioncodex-4x-Katalog (`src/data/npc_trash_mob_names.json`, abgeleitet aus des Clients
eigener `Client/assets/npcs/npcs_en_4x.json`) AUSSCHLIESSLICH als Rang "Normal" auftaucht (z.B. "Kobold
Peon"), landet erst gar nicht in der DB - der Client selbst filtert das schon vorher genauso (siehe
`Client/Data/NpcDatabase.IsTrashMob`/`Client/Ui/MainWindow.BuildEncounterUpload`), diese Prüfung ist nur die
Verteidigungslinie gegen einen älteren Client, der das noch nicht kennt.

Neu erzeugen (wenn aioncodex nachzieht oder weitere NPCs dazukommen):

```bash
python3 -c "
import json
data = json.load(open('Client/assets/npcs/npcs_en_4x.json'))
from collections import defaultdict
name_ranks = defaultdict(set)
for d in data:
    name_ranks[d['name']].add(d['rank'])
trash_only = sorted(n for n, r in name_ranks.items() if r == {'Normal'})
json.dump(trash_only, open('Backend/src/data/npc_trash_mob_names.json', 'w'), ensure_ascii=False, separators=(',', ':'))
"
```

Das deckt nur den eindeutigen Fall ab (ein Name, der IMMER Rang "Normal" ist - ein Name, der
irgendwo auch als Elite/Heroic/Legendary auftaucht, z.B. "Boreas", wird absichtlich nie automatisch
abgelehnt, um keinen echten Bosskampf fälschlich zu blockieren). Für alles, was diese automatische
Prüfung nicht erfasst - ein Elite-Mob, den der Nutzer für eine bestimmte Instanz trotzdem als
uninteressant einstuft, oder ein Name außerhalb des Katalogs (z.B. "Zauberer der Stahlrose" in Steel
Rose Cargo, laut Nutzer nur ein regulärer Mob, nicht der Instanz-Endboss) - weiterhin die manuelle
Markierung per SQL, Zeile und Encounters bleiben dabei erhalten (weiter per Direktlink
`/api/bosses/:id/leaderboard` erreichbar), verschwinden aber aus `GET /api/instances/:id/bosses` und
damit aus der normalen Bossliste:

```sql
UPDATE bosses SET is_trash_mob = 1 WHERE name = '<Bossname>';
```

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

Aion kennt keine stabile Spieler-ID im Chat.log, nur den Namen - eine echte Umbenennung
("Alhamdulilah" → "Hidan") erzeugt sonst für immer zwei getrennte `players`-Zeilen für dieselbe
Person, ohne dass es je ein automatisches Signal dafür gäbe (das Chat.log kündigt eine Umbenennung
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

## Buff-Dauer (welche Buffs im "Buffs"-Feld erscheinen)

Per Nutzeranfrage: das "Buffs"-Feld einer Encounter-Detailseite soll keine kurzen
Kampf-Rotations-Buffs zeigen (z.B. Berserking I, 30s), sondern nur echte, länger stehende
Verstärkungen (> 3 Minuten) - plus jeden Skill, der Göttliche Kraft/Divine Power kostet, unabhängig
von dessen eigener Dauer (wie auf myaion.eu). Eine frühere Version dieser Regel hatte stattdessen
eine wörtliche Namens-Ausnahme für einen Skill namens "Divine Power" - den es unter diesem exakten
Namen nirgends in aioncodex' 4x-Katalog gibt; aufgefallen an "Daevic Fury I" (Gladiator), das nur
30s hält, aber 2000 DP auf 30 Minuten Abklingzeit kostet und deshalb komplett unsichtbar blieb, bis
diese Regel korrigiert wurde. Aion selbst nennt weder Dauer noch Ressourcenkosten im Chat.log - die
Werte in `src/data/skill_durations.json`/`src/data/skill_dp_cost.json` (Kopien von
`../Client/assets/skills/skill_durations.json`/`../Client/assets/skills/skill_dp_cost.json`) stammen aus dem
Beschreibungstext jeder Skillseite auf aioncodex.com ("Increases ... for 30s."/"... for 1h." bzw.
"Usage Cost: DP 2000"), einmalig für alle 974 bekannten Skills abgerufen, nicht geschätzt.

Neu erzeugen (wenn aioncodex nachzieht oder weitere Skills dazukommen):

```bash
python3 - <<'PY'
import json, re, time, urllib.request
SRC = "Client/assets/skills/skills_multilang_4x.json"
OUT = "Client/assets/skills/skill_durations.json"
skills = json.load(open(SRC, encoding="utf-8"))
ids = sorted({s["id"] for s in skills})
UNIT_RE = r"(hours|hour|hrs|hr|h|minutes|minute|mins|min|m|seconds|second|secs|sec|s)"
DUR_RE = re.compile(r"\bfor\s+(\d+)\s*" + UNIT_RE + r"\b", re.IGNORECASE)
def to_seconds(n, unit):
    unit = unit.lower()
    return n * 3600 if unit.startswith("h") else n * 60 if (unit == "m" or unit.startswith("min")) else n
results = {}
for sid in ids:
    html = urllib.request.urlopen(urllib.request.Request(
        f"https://aioncodex.com/4x/skill/{sid}/", headers={"User-Agent": "Mozilla/5.0"}), timeout=10).read().decode("utf-8", "replace")
    text = re.sub(r"\s+", " ", re.sub("<[^>]+>", " | ", html))
    window = text[text.find("Cooldown:"):][:400]
    m = DUR_RE.search(window)
    results[str(sid)] = {
        "durationSeconds": to_seconds(int(m.group(1)), m.group(2)) if m else None,
        "permanent": "permanent" in window.lower(),
    }
    time.sleep(0.05)
json.dump(results, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
PY
cp Client/assets/skills/skill_durations.json Backend/src/data/skill_durations.json
```

Wichtig: aioncodex schreibt Dauern als Kurzformen (`30s`/`5m`/`1h`), nicht als volle Wörter - eine
frühere Version dieser Regex kannte nur `s`/`min` und übersah dadurch reale Langzeit-Gruppenbuffs
komplett (Word of Wind I mit `for 5m.` und Blessing of Health I mit `for 1h.` kamen beide ohne
Dauer zurück, bis das aufgefallen ist). Immer stichprobenartig gegen bekannte lange Buffs prüfen,
bevor die Datei übernommen wird.

`skill_dp_cost.json` nach demselben Muster neu erzeugen (welche Skills Göttliche Kraft kosten):

```bash
python3 - <<'PY'
import json, re, time, urllib.request
SRC = "Client/assets/skills/skills_multilang_4x.json"
OUT = "Client/assets/skills/skill_dp_cost.json"
skills = json.load(open(SRC, encoding="utf-8"))
ids = sorted({s["id"] for s in skills})
COST_RE = re.compile(r"Usage Cost:\s*([A-Za-z]+)\s*([\d,]*)")
dp_ids = []
for sid in ids:
    html = urllib.request.urlopen(urllib.request.Request(
        f"https://aioncodex.com/4x/skill/{sid}/", headers={"User-Agent": "Mozilla/5.0"}), timeout=10).read().decode("utf-8", "replace")
    m = COST_RE.search(html)
    if m and m.group(1) == "DP":
        dp_ids.append(sid)
    time.sleep(0.05)
json.dump(sorted(dp_ids), open(OUT, "w", encoding="utf-8"), indent=1)
PY
cp Client/assets/skills/skill_dp_cost.json Backend/src/data/skill_dp_cost.json
```

Grenzwert (`MIN_DURATION_SECONDS = 180`) und die DP-Kosten-Prüfung stehen in
`src/skills/skillDurations.ts` (`loadDpCostSkillIds`) - beide id-basiert (über
`skills_multilang_4x.json`'s `<name>`-Zuordnung), keine Namens-Ausnahmeliste und keine SQL nötig.

Nebenbei behoben, als diese Datei entstand: 41 Skillnamen in `skills_multilang_4x.json` trugen noch
rohe HTML-Entities (`Triniel&#39;s Dirk I` statt `Triniel's Dirk I`) - kam beim ursprünglichen
Aufbau der Datei nie zur Auflösung, fiel aber erst hier auf, weil ausgerechnet mehrere DP-Skills
einen Apostroph im Namen tragen und ohne die Korrektur nie gegen echten Chat.log-Text gematcht
hätten. `html.unescape()` auf `name`/`de`/`fr` behebt es dauerhaft.

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
