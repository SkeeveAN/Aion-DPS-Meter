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

Genau wie die Instanz-Zuordnung gibt es keine automatische Unterscheidung "echter Boss" vs.
"beliebiger Mob, den die Gruppe zufällig bekämpft hat" - ein Upload legt jeden neuen Bossnamen
gleichwertig an (siehe `src/matching/merge.ts`). Wenn sich ein Eintrag als reiner Trash-Mob
herausstellt (z.B. "Zauberer der Stahlrose" in Steel Rose Cargo - laut Nutzer nur ein regulärer Mob,
nicht der Instanz-Endboss), per SQL markieren statt löschen - Zeile und ihre Encounters bleiben
erhalten (weiter per Direktlink `/api/bosses/:id/leaderboard` erreichbar), verschwinden aber aus
`GET /api/instances/:id/bosses` und damit aus der normalen Bossliste:

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

## Buff-Dauer (welche Buffs im "Buffs"-Feld erscheinen)

Per Nutzeranfrage: das "Buffs"-Feld einer Encounter-Detailseite soll keine kurzen
Kampf-Rotations-Buffs zeigen (z.B. Berserking I, 30s), sondern nur echte, länger stehende
Verstärkungen (> 3 Minuten) - plus "Divine Power" unabhängig von der Dauer (siehe
`src/skills/skillDurations.ts`, `ALWAYS_SHOWN_BUFFS`). Aion selbst nennt keine Dauer im Chat.log -
die Werte in `src/data/skill_durations.json` (Kopie von `assets/skills/skill_durations.json`)
stammen aus dem Beschreibungstext jeder Skillseite auf aioncodex.com ("Increases ... for 30s."/
"... for 1h."), einmalig für alle 974 bekannten Skills abgerufen, nicht geschätzt.

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

Grenzwert (`MIN_DURATION_SECONDS = 180`) und die `ALWAYS_SHOWN_BUFFS`-Ausnahmeliste stehen in
`src/skills/skillDurations.ts` - eine Skill-Ausnahme dort per Namen eintragen, keine SQL nötig.

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
