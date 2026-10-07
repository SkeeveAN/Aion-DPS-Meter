import { statSync } from "node:fs";
import { sqlite } from "../db/client.js";
import { env } from "../env.js";
import { splitConquest } from "../../../Web-Frontend/game-data.js";

type Row = Record<string, number | string | null>;
const all = (sql: string, ...params: unknown[]) => sqlite.prepare(sql).all(...params) as Row[];
const one = (sql: string, ...params: unknown[]) => (sqlite.prepare(sql).get(...params) ?? {}) as Row;
const num = (v: unknown) => Number(v ?? 0);

const ASCENSION_STARS: Record<string, number> = { easy: 1, medium: 2, hard: 3, extreme: 4 };

/** A boss's difficulty step as stars: Nightmare level and Transcendence stage 1-10 / 1-4, Ascension Easy to Extreme 1-4. "" when unknown. */
function starsOf(mode: string): string {
  const n = /^\d+$/.test(mode) ? Number(mode) : ASCENSION_STARS[mode] ?? 0;
  return n > 0 && n <= 10 ? "★".repeat(n) : "";
}

/** Totals that are safe to show to everybody: counts only, nothing that identifies a person. */
export function publicStats() {
  const players = num(one("select count(*) n from players").n);
  const own = num(one("select count(*) n from player_profiles where source = 'self'").n);
  const seen = num(one("select count(*) n from player_profiles where source = 'seen'").n);
  const encounters = num(one("select count(*) n from encounters").n);
  const uploads = num(one("select count(*) n from uploads").n);
  const dl = one("select count(*) total, sum(downloaded_at >= datetime('now','-30 days')) last30 from downloads where is_bot = 0");
  const perDay = all(
    "select date(created_at) day, count(*) n from encounters where created_at >= datetime('now','-30 days') group by day order by day",
  ).map((r) => ({ day: String(r.day), count: num(r.n) }));
  const classes = all("select class_name name, count(distinct player_id) n from encounter_participants group by class_name order by n desc limit 12").map((r) => ({ name: String(r.name), count: num(r.n) }));
  const servers = all(
    `select coalesce(s.display_name, s.fingerprint) name, (select c.faction from server_catalog c where c.name = s.display_name and c.game = 'aion2' limit 1) faction, count(*) n from players p join servers s on s.id = p.server_id group by p.server_id order by n desc limit 12`,
  ).map((r) => ({ name: String(r.name), faction: r.faction ? String(r.faction) : "", count: num(r.n) }));
  const topBosses = all(
    `select b.name_en en, b.name name, coalesce(i.name_en, i.name) instance, e.mode mode, count(*) n from encounters e join bosses b on b.id = e.boss_id join instances i on i.id = b.instance_id where b.is_trash_mob = 0 group by e.boss_id, e.mode order by n desc limit 10`,
  ).map((r) => ({ name: String(r.en ?? r.name), instance: String(r.instance), stars: starsOf(String(r.mode ?? "")), count: num(r.n) }));
  return {
    players,
    ownProfiles: own,
    seenProfiles: seen,
    encounters,
    uploads,
    downloads: { total: num(dl.total), last30: num(dl.last30) },
    encountersPerDay: perDay,
    classes,
    servers,
    topBosses,
  };
}

/** Operator view: everything above plus who is active, which client versions are out there and the health of the intake. */
export function privateStats() {
  const uploadsPerDay = all(
    "select date(received_at) day, count(*) n, count(distinct ip_hash) u from uploads where received_at >= datetime('now','-30 days') group by day order by day",
  ).map((r) => ({ day: String(r.day), uploads: num(r.n), uploaders: num(r.u) }));
  const activeUploaders = (days: number) => num(one("select count(distinct ip_hash) n from uploads where received_at >= datetime('now', ?)", `-${days} days`).n);
  const versions = all(
    `select client_version v, count(*) n, count(distinct ip_hash) u, max(received_at) last from uploads
     where received_at >= datetime('now','-30 days') group by client_version order by last desc`,
  ).map((r) => ({ version: String(r.v || "?"), uploads: num(r.n), uploaders: num(r.u), lastUpload: String(r.last) }));
  const status = Object.fromEntries(all("select status s, count(*) n from uploads group by status").map((r) => [String(r.s), num(r.n)]));
  const newPlayersPerWeek = all(
    "select strftime('%Y-W%W', first_seen_at) week, count(*) n from players group by week order by week desc limit 8",
  ).map((r) => ({ week: String(r.week), count: num(r.n) }));
  const downloadsPerDay = all(
    "select date(downloaded_at) day, count(*) n, count(distinct ip_hash) u from downloads where is_bot = 0 and downloaded_at >= datetime('now','-30 days') group by day order by day",
  ).map((r) => ({ day: String(r.day), clicks: num(r.n), unique: num(r.u) }));
  const downloadsByTag = all("select tag, count(*) n from downloads where is_bot = 0 group by tag order by max(id) desc limit 8").map((r) => ({ tag: String(r.tag), count: num(r.n) }));
  // Which game servers (IP:port) the clients captured from, with the server ids they reported there: the
  // evidence for telling the regions apart.
  const gameServers = all(
    `select game_server addr, count(*) n, group_concat(distinct client_server_id) ids from uploads where game_server is not null group by game_server order by n desc limit 40`,
  ).map((r) => ({ address: String(r.addr), uploads: num(r.n), serverIds: String(r.ids ?? "") }));
  const last = one("select max(received_at) a from uploads").a;
  const completeness = one(
    `select sum(gear_json <> '[]') gear, sum(skills_json <> '[]') skills, sum(daevanion_json <> '[]') daevanion, count(*) n from player_profiles where source = 'self'`,
  );
  let dbBytes = 0;
  try {
    dbBytes = statSync(env.DATABASE_PATH).size;
  } catch {
    // size is informational only
  }
  // Every boss with at least one fight; conquest bosses carry their star rating ("Vakron ★★") like on the site.
  const bossFights = all(
    `select coalesce(b.name_en, b.name) name, coalesce(i.name_en, i.name) instance, count(*) n from encounters e join bosses b on b.id = e.boss_id join instances i on i.id = b.instance_id where b.is_trash_mob = 0 group by e.boss_id order by n desc`,
  ).map((r) => {
    const conquest = splitConquest(String(r.instance));
    return { boss: conquest ? `${r.name} ${conquest.stars}` : String(r.name), instance: conquest ? conquest.base : String(r.instance), count: num(r.n) };
  });
  return {
    ...publicStats(),
    bossFights,
    gameServers,
    uploadsPerDay,
    activeUploaders: { d1: activeUploaders(1), d7: activeUploaders(7), d30: activeUploaders(30) },
    versions,
    uploadStatus: status,
    newPlayersPerWeek,
    downloadsPerDay,
    downloadsByTag,
    botDownloads: num(one("select count(*) n from downloads where is_bot = 1").n),
    lastUpload: last ? String(last) : null,
    ownProfileCompleteness: { gear: num(completeness.gear), skills: num(completeness.skills), daevanion: num(completeness.daevanion), total: num(completeness.n) },
    dbBytes,
  };
}

const esc = (v: string) => v.replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\n/g, " ");

/** Prometheus text exposition of the operator numbers. */
export function metricsText(): string {
  const p = privateStats();
  const out: string[] = [];
  const metric = (name: string, help: string, type: "gauge" | "counter", samples: [Record<string, string> | null, number][]) => {
    out.push(`# HELP ${name} ${help}`, `# TYPE ${name} ${type}`);
    for (const [labels, value] of samples) {
      const l = labels ? `{${Object.entries(labels).map(([k, v]) => `${k}="${esc(v)}"`).join(",")}}` : "";
      out.push(`${name}${l} ${value}`);
    }
  };
  metric("aiondps_players", "Distinct players known (name + server).", "gauge", [[null, p.players]]);
  metric("aiondps_profiles", "Player profiles by source (self = own client, seen = seen in a group).", "gauge", [[{ source: "self" }, p.ownProfiles], [{ source: "seen" }, p.seenProfiles]]);
  metric("aiondps_encounters_total", "Boss fights stored.", "counter", [[null, p.encounters]]);
  metric("aiondps_uploads_total", "Uploads received, by status.", "counter", Object.entries(p.uploadStatus).map(([s, n]) => [{ status: s }, n]));
  metric("aiondps_boss_fights", "Boss fights per boss (conquest bosses with their stars).", "gauge", p.bossFights.map((b) => [{ boss: b.boss, dungeon: b.instance }, b.count]));
  metric("aiondps_downloads_total", "Download button clicks through the website (bots excluded).", "counter", [[null, p.downloads.total]]);
  metric("aiondps_active_uploaders", "Distinct uploader hashes with an upload in the last N days.", "gauge", [[{ days: "1" }, p.activeUploaders.d1], [{ days: "7" }, p.activeUploaders.d7], [{ days: "30" }, p.activeUploaders.d30]]);
  metric("aiondps_client_version_uploads_30d", "Uploads of the last 30 days per client version.", "gauge", p.versions.map((v) => [{ version: v.version }, v.uploads]));
  metric("aiondps_last_upload_timestamp_seconds", "Time of the newest upload.", "gauge", [[null, p.lastUpload ? Math.floor(Date.parse(p.lastUpload.replace(" ", "T") + "Z") / 1000) : 0]]);
  metric("aiondps_database_size_bytes", "Size of the SQLite file.", "gauge", [[null, p.dbBytes]]);
  return out.join("\n") + "\n";
}
