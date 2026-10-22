/**
 * Read-only: lists the stored encounters the partial-fight rule (src/bossHpRules.ts) would classify as partial, strongest first
 * (lowest share of the boss's real health seen). Writes nothing: the database is opened read-only, nothing is backfilled.
 *
 *   tsx scripts/list-partial-candidates.ts <sqlite file> [--seed <bossHpSeed.json>] [--limit N] [--json]
 *
 * The reference per boss NPC id comes from the table boss_hp_reference of that database (when it has one), else from the seed file
 * (src/content/bossHpSeed.json). The judged numbers come from the raw payloads of the uploads merged into each encounter; an
 * encounter is a candidate only when EVERY one of its uploads is partial (the merge rule). World bosses are never partial.
 * No player names are printed.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync } from "node:zlib";
import Database from "better-sqlite3";
import { classifyFight, type BossHpRef } from "../src/bossHpRules.js";

const args = process.argv.slice(2);
const flag = (name: string) => {
  const i = args.indexOf(name);
  return i >= 0 ? args.splice(i, i + 1 < args.length && !args[i + 1]!.startsWith("--") && name !== "--json" ? 2 : 1) : null;
};
const seedArg = flag("--seed");
const limitArg = flag("--limit");
const asJson = flag("--json") !== null;
const dbPath = args[0];
if (!dbPath) {
  console.error("usage: tsx scripts/list-partial-candidates.ts <sqlite file> [--seed file] [--limit N] [--json]");
  process.exit(2);
}
const limit = limitArg ? Number(limitArg[1]) : Infinity;
const seedFile = seedArg ? seedArg[1]! : path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "src", "content", "bossHpSeed.json");

const db = new Database(dbPath, { readonly: true, fileMustExist: true });

const refs = new Map<number, BossHpRef>();
const hasTable = db.prepare("select 1 from sqlite_master where type = 'table' and name = 'boss_hp_reference'").get() !== undefined;
let refSource = "";
if (hasTable) {
  for (const r of db.prepare("select npc_id, base_hp, party_scaled from boss_hp_reference").all() as { npc_id: number; base_hp: number; party_scaled: number }[]) {
    refs.set(r.npc_id, { baseHp: r.base_hp, partyScaled: r.party_scaled === 1 });
  }
  refSource = "boss_hp_reference table";
}
if (refs.size === 0) {
  const seed = JSON.parse(fs.readFileSync(seedFile, "utf8")) as { npcs: Record<string, { baseHp: number; partyScaled: boolean }> };
  for (const [id, n] of Object.entries(seed.npcs)) {
    refs.set(Number(id), { baseHp: n.baseHp, partyScaled: n.partyScaled });
  }
  refSource = `seed file ${seedFile}`;
}

interface Payload {
  bossNpcId?: number;
  bossMaxHp?: number;
  bossSpawnMaxHp?: number;
  participants?: { name: string; totalDamage?: number }[];
}
const rows = db
  .prepare(
    `select u.id uid, u.matched_encounter_id eid, u.raw_payload_json raw, u.raw_payload_gz gz,
            e.started_at started, e.mode mode, e.boss_max_hp enc_max, e.merged_upload_count merged,
            coalesce(b.name_en, b.name) boss, i.category cat
     from uploads u
     join encounters e on e.id = u.matched_encounter_id
     join bosses b on b.id = e.boss_id
     join instances i on i.id = b.instance_id
     where u.status = 'merged' order by e.id, u.id`,
  )
  .all() as { uid: number; eid: number; raw: string; gz: Buffer | null; started: string; mode: string; enc_max: number | null; merged: number; boss: string; cat: string | null }[];

interface Fight {
  eid: number;
  boss: string;
  mode: string;
  started: string;
  uploads: number;
  partialUploads: number;
  bossMaxHp: number;
  baseHp: number | null;
  realMaxHp: number | null;
  share: number | null;
}
const byEncounter = new Map<number, Fight>();
let unreadable = 0;
for (const row of rows) {
  let payload: Payload;
  try {
    const text = row.raw !== "" ? row.raw : row.gz ? gunzipSync(row.gz).toString("utf8") : "";
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
  const fight = byEncounter.get(row.eid) ?? { eid: row.eid, boss: row.boss, mode: row.mode, started: row.started, uploads: 0, partialUploads: 0, bossMaxHp: 0, baseHp: null, realMaxHp: null, share: null };
  fight.uploads++;
  if (c.isPartial) {
    fight.partialUploads++;
  }
  // The best upload speaks for the encounter: the highest share (a whole upload makes the encounter whole).
  if (c.hpSeenShare !== null && (fight.share === null || c.hpSeenShare > fight.share)) {
    fight.share = c.hpSeenShare;
    fight.realMaxHp = c.bossRealMaxHp;
    fight.baseHp = c.bossSpawnMaxHp ?? ref?.baseHp ?? null;
    fight.bossMaxHp = payload.bossMaxHp ?? 0;
  }
  byEncounter.set(row.eid, fight);
}

const candidates = [...byEncounter.values()].filter((f) => f.uploads > 0 && f.partialUploads === f.uploads).sort((a, b) => (b.share ?? 0) - (a.share ?? 0));
const shown = candidates.slice(0, limit);

if (asJson) {
  console.log(JSON.stringify({ reference: refSource, encounters: byEncounter.size, candidates: candidates.length, rows: shown }, null, 1));
} else {
  console.error(`reference: ${refSource} (${refs.size} NPC ids); ${byEncounter.size} encounters with a readable upload, ${unreadable} uploads without a readable payload`);
  console.log("encounter\tshare\tbossMaxHp\trealMaxHp\tuploads\tstarted\tboss\tmode");
  for (const f of shown) {
    console.log([f.eid, ((f.share ?? 0) * 100).toFixed(1) + "%", f.bossMaxHp, f.realMaxHp, f.uploads, f.started, f.boss, f.mode].join("\t"));
  }
  const perBoss = new Map<string, number>();
  for (const f of candidates) {
    perBoss.set(f.boss, (perBoss.get(f.boss) ?? 0) + 1);
  }
  console.error(`\n${candidates.length} candidate partial encounters of ${byEncounter.size}`);
  for (const [boss, n] of [...perBoss].sort((a, b) => b[1] - a[1]).slice(0, 15)) {
    console.error(`  ${n}\t${boss}`);
  }
}
db.close();
