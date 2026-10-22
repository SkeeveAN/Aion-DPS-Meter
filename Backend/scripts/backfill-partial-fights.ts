/**
 * Marks the stored boss fights that the partial-fight rule (src/bossHpRules.ts) classifies as partial, after migration 0064 added the columns.
 * Dry run by default: it only prints what it would change; `--apply` writes, in ONE transaction. Take a backup first
 * (sqlite3 <db> ".backup '<file>'"; the live database runs in WAL mode, never a plain copy).
 *
 *   tsx scripts/backfill-partial-fights.ts <sqlite file> [--apply]
 *
 * Reads the reference per boss NPC id from the table boss_hp_reference (seeded by db:migrate) and the judged numbers from the raw payloads
 * of the uploads merged into each encounter; an encounter is marked only when EVERY one of its uploads is partial (the merge rule). Only
 * those encounters are touched: is_partial, hp_seen_share, boss_real_max_hp and boss_spawn_max_hp are set on them, nothing else, and
 * encounters that are already marked are left alone. World bosses are never partial. No player names are printed.
 */
import { gunzipSync } from "node:zlib";
import Database from "better-sqlite3";
import { classifyFight, type BossHpRef } from "../src/bossHpRules.js";

const args = process.argv.slice(2);
const apply = args.includes("--apply");
const dbPath = args.find((a) => !a.startsWith("--"));
if (!dbPath) {
  console.error("usage: tsx scripts/backfill-partial-fights.ts <sqlite file> [--apply]");
  process.exit(2);
}

const db = new Database(dbPath, { fileMustExist: true });
if (db.prepare("select 1 from sqlite_master where type = 'table' and name = 'boss_hp_reference'").get() === undefined) {
  console.error("boss_hp_reference is missing: run db:migrate first");
  process.exit(1);
}
const refs = new Map<number, BossHpRef>();
for (const r of db.prepare("select npc_id, base_hp, party_scaled from boss_hp_reference").all() as { npc_id: number; base_hp: number; party_scaled: number }[]) {
  refs.set(r.npc_id, { baseHp: r.base_hp, partyScaled: r.party_scaled === 1 });
}
if (refs.size === 0) {
  console.error("boss_hp_reference is empty: run db:migrate (it seeds it) first");
  process.exit(1);
}

interface Payload {
  bossNpcId?: number;
  bossMaxHp?: number;
  bossSpawnMaxHp?: number;
  participants?: { name: string; totalDamage?: number }[];
}
const rows = db
  .prepare(
    `select u.matched_encounter_id eid, u.raw_payload_json raw, u.raw_payload_gz gz, e.is_partial already, i.category cat,
            coalesce(b.name_en, b.name) boss, e.started_at started
     from uploads u
     join encounters e on e.id = u.matched_encounter_id
     join bosses b on b.id = e.boss_id
     join instances i on i.id = b.instance_id
     where u.status = 'merged' order by e.id, u.id`,
  )
  .all() as { eid: number; raw: string | null; gz: Buffer | null; already: number; cat: string | null; boss: string; started: string }[];

interface Fight {
  eid: number;
  boss: string;
  started: string;
  already: boolean;
  uploads: number;
  partialUploads: number;
  share: number | null;
  realMaxHp: number | null;
  spawn: number | null;
}
const byEncounter = new Map<number, Fight>();
let unreadable = 0;
for (const row of rows) {
  let payload: Payload;
  try {
    const text = row.raw ? row.raw : row.gz ? gunzipSync(row.gz).toString("utf8") : "";
    if (text === "") {
      unreadable++;
      continue;
    }
    payload = JSON.parse(text) as Payload;
  } catch {
    unreadable++;
    continue;
  }
  const people = new Set((payload.participants ?? []).map((p) => p.name)).size;
  const ref = payload.bossNpcId !== undefined ? (refs.get(payload.bossNpcId) ?? null) : null;
  const damage = (payload.participants ?? []).reduce((sum, p) => sum + (p.totalDamage ?? 0), 0);
  const c = classifyFight({ bossMaxHp: payload.bossMaxHp, bossSpawnMaxHp: payload.bossSpawnMaxHp }, people, ref, row.cat === "worldboss", damage);
  const fight = byEncounter.get(row.eid) ?? { eid: row.eid, boss: row.boss, started: row.started, already: row.already === 1, uploads: 0, partialUploads: 0, share: null, realMaxHp: null, spawn: null };
  fight.uploads++;
  if (c.isPartial) {
    fight.partialUploads++;
  }
  // The best upload speaks for the encounter: the highest share.
  if (c.hpSeenShare !== null && (fight.share === null || c.hpSeenShare > fight.share)) {
    fight.share = c.hpSeenShare;
    fight.realMaxHp = c.bossRealMaxHp;
    fight.spawn = c.bossSpawnMaxHp;
  }
  byEncounter.set(row.eid, fight);
}

const targets = [...byEncounter.values()].filter((f) => f.uploads > 0 && f.partialUploads === f.uploads && !f.already).sort((a, b) => (b.share ?? 0) - (a.share ?? 0));
console.error(`${byEncounter.size} encounters with a readable upload (${unreadable} uploads unreadable); ${targets.length} to mark as partial${apply ? "" : " (dry run, nothing written)"}`);
console.log("encounter\tshare\trealMaxHp\tstarted\tboss");
for (const f of targets) {
  console.log([f.eid, ((f.share ?? 0) * 100).toFixed(1) + "%", f.realMaxHp, f.started, f.boss].join("\t"));
}

if (apply) {
  const update = db.prepare("update encounters set is_partial = 1, hp_seen_share = ?, boss_real_max_hp = ?, boss_spawn_max_hp = ? where id = ? and is_partial = 0");
  const run = db.transaction(() => {
    let changed = 0;
    for (const f of targets) {
      changed += update.run(f.share, f.realMaxHp, f.spawn, f.eid).changes;
    }
    return changed;
  });
  console.error(`marked ${run()} encounters as partial`);
}
db.close();
