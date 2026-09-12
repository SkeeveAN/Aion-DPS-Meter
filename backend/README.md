# Aion DPS-Meter Backend

API + minimalistisches Web-Frontend für `dpsmeter.skeeve.tv`. Nimmt Boss-Kampf-Uploads vom
[AionSniffer](../) DPS-Meter-Client entgegen, erkennt serverseitig, welche Uploads verschiedener
Gruppenmitglieder zum selben Kampf gehören (siehe `src/matching/merge.ts`), und zeigt Leaderboards
sowie Spielerprofile an.

## Stack

TypeScript + Fastify + better-sqlite3 + drizzle-orm + zod - bewusst identisch zum Schwesterprojekt
`../../timetable/apps/backend`, um Deployment-/Wartungswissen wiederzuverwenden. Kein Postgres:
bei den erwarteten Upload-Mengen (siehe Projekt-Notizen) ist SQLite/WAL im selben Prozess klar
ausreichend, siehe die Diskussion, die dieser Entscheidung vorausging.

Frontend: reines Vanilla-JS/HTML/CSS unter `public/`, kein Build-Schritt - von Fastify selbst per
`@fastify/static` unter `/` ausgeliefert, API unter `/api/*`.

## Lokale Entwicklung

```bash
pnpm install
cp .env.example .env
pnpm run db:migrate
pnpm run db:seed
pnpm run dev        # http://127.0.0.1:4000
pnpm run test       # Matching-Algorithmus-Tests, node:test
```

`better-sqlite3` braucht zum Kompilieren entweder einen vorgebauten Binary (üblich für
LTS-Node-Versionen wie 22) oder `make`/`gcc`/`python3` lokal installiert.

## Instanz-/Boss-Zuordnung

Es gibt keine Instanz→Boss-Datenbank aus dem Client (`Data/NpcDatabase.cs` kennt nur NPC-Namen,
keine Zone/Instanz-Zuordnung). Ein unbekannter Bossname landet beim ersten Upload automatisch in
der Instanz "Unbekannt / nicht zugeordnet" (siehe `src/db/seed.ts`, `src/matching/merge.ts`). Um
ihn einer echten Instanz zuzuordnen: in der `instances`-Tabelle die Zeile anlegen/finden und in
`bosses.instance_id` auf deren `id` umbiegen, z.B.:

```sql
UPDATE bosses SET instance_id = <echte instance id> WHERE name = '<Bossname>';
```

Eine passende Instanz-Zeile fehlt noch? Erst per `INSERT INTO instances (name, sort_order) VALUES (...)`
anlegen. Es gibt bewusst keine vorab geratene Instanzliste im Seed - die genaue Instanz-/Boss-Liste
dieses konkreten Servers ist von hier aus nicht zuverlässig bekannt, eine falsche Zuordnung wäre
schlimmer als eine leere.

## Trash-Mobs ausblenden

Ein eindeutiger Trash-Mob wird automatisch schon beim Upload abgelehnt (siehe
`src/npc/trashMobs.ts`, aufgerufen aus `src/routes/uploads.ts`, `400 trash_mob_rejected`): jeder
Name, der im aioncodex-4x-Katalog (`src/data/npc_trash_mob_names.json`, abgeleitet aus des Clients
eigener `assets/npcs/npcs_en_4x.json`) AUSSCHLIESSLICH als Rang "Normal" auftaucht (z.B. "Kobold
Peon"), landet erst gar nicht in der DB - der Client selbst filtert das schon vorher genauso (siehe
`Data/NpcDatabase.IsTrashMob`/`Ui/MainWindow.BuildEncounterUpload`), diese Prüfung ist nur die
Verteidigungslinie gegen einen älteren Client, der das noch nicht kennt.

Neu erzeugen (wenn aioncodex nachzieht oder weitere NPCs dazukommen):

```bash
python3 -c "
import json
data = json.load(open('assets/npcs/npcs_en_4x.json'))
from collections import defaultdict
name_ranks = defaultdict(set)
for d in data:
    name_ranks[d['name']].add(d['rank'])
trash_only = sorted(n for n, r in name_ranks.items() if r == {'Normal'})
json.dump(trash_only, open('backend/src/data/npc_trash_mob_names.json', 'w'), ensure_ascii=False, separators=(',', ':'))
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
`assets/skills/skill_durations.json`/`assets/skills/skill_dp_cost.json`) stammen aus dem
Beschreibungstext jeder Skillseite auf aioncodex.com ("Increases ... for 30s."/"... for 1h." bzw.
"Usage Cost: DP 2000"), einmalig für alle 974 bekannten Skills abgerufen, nicht geschätzt.

Neu erzeugen (wenn aioncodex nachzieht oder weitere Skills dazukommen):

```bash
python3 - <<'PY'
import json, re, time, urllib.request
SRC = "assets/skills/skills_multilang_4x.json"
OUT = "assets/skills/skill_durations.json"
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
cp assets/skills/skill_durations.json backend/src/data/skill_durations.json
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
SRC = "assets/skills/skills_multilang_4x.json"
OUT = "assets/skills/skill_dp_cost.json"
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
cp assets/skills/skill_dp_cost.json backend/src/data/skill_dp_cost.json
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

nginx ist bereits fertig konfiguriert: `dpsmeter.skeeve.tv` (Port 443) proxied auf
`127.0.0.1:4000`, inklusive `/ws`. Deployment folgt exakt dem Muster von
`../../timetable/deploy/`:

```bash
# einmalig, als root auf dem Server:
curl -fsSL https://raw.githubusercontent.com/SkeeveAN/Aion-DPS-Meter/main/backend/deploy/dpsmeter_install \
  -o /usr/local/bin/dpsmeter_install
chmod +x /usr/local/bin/dpsmeter_install
dpsmeter_install

# danach, bei jedem Update:
dpsmeter_install
```

Legt einen eigenen Systemuser `aion-dpsmeter` an, checkt das Repo nach `/opt/dpsmeter` aus und
betreibt den Service unter `systemd` (`aion-dpsmeter-backend.service`).
