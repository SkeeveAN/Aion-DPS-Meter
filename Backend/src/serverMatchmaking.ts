import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { announcementList } from "./announcements.js";
import { getNc } from "./portraitService.js";
import { noticeText } from "./twitchDrops.js";

// Server matchmaking (which Elyos server shares its world with which Asmodian server). NC publishes it only as a table inside a notice ("[Notice] Server
// Matchmaking Information (Asia)") and edits that notice when the pairings change. The announcement store already lists the notices; this module finds those
// whose title says "matchmaking", fetches their full text, parses the table and keeps every distinct state in a file, so the history of the pairings survives
// NC dropping or rewriting the notice. It is reference data only: a player's faction is still that of their own server.

type FetchFn = typeof fetch;

const BASE = "https://api-global-community.plaync.com/aion2_global/board";
const MAX_BYTES = 2 * 1024 * 1024;
export const MATCHMAKING_FILE = path.resolve(process.env.SERVER_MATCHMAKING_FILE ?? "./data/server-matchmaking.json");

export type MatchPair = { region: string | null; elyos: string; asmodian: string; phase: string | null; updated: string | null };
/** An Abyss pairing: unlike the rift it is not Elyos against Asmodian, the factions of both servers are part of the table (a server can meet its own faction). */
export type AbyssPair = { region: string | null; server1: string; server2: string; faction1: "Elyos" | "Asmodian"; faction2: "Elyos" | "Asmodian" };
type Version = { at: string; pairs: MatchPair[]; abyss?: AbyssPair[] };
export type MatchNotice = { id: string; title: string; region: string | null; postedAt: string; checkedAt: number; pairs: MatchPair[]; abyss?: AbyssPair[]; history: Version[] };

const REGIONS: Record<string, string> = {
  europe: "Europe",
  "na east": "NA East",
  "na west": "NA West",
  latam: "LATAM",
  "latin america": "LATAM",
  "south america": "LATAM",
  asia: "Asia",
};

/** "[Notice] Server Matchmaking Information (Asia)" -> "Asia"; null when the title names no region. */
export function regionOfTitle(title: string): string | null {
  const m = /\(([^()]+)\)\s*$/.exec(title.trim());
  return m ? (REGIONS[m[1].trim().toLowerCase()] ?? null) : null;
}

const phaseOf = (text: string): string | null => (/advanced access/i.test(text) ? "Advanced Access" : /launch/i.test(text) ? "Launch" : null);
const cellsOf = (row: string) => (row.match(/<t[dh][\s\S]*?<\/t[dh]>/gi) ?? []).map((c) => noticeText(c).replace(/\s+/g, " ").trim());

/**
 * The pairs of a notice. NC wrote it in three layouts: rows "Elyos | vs. | Asmodian | Time Updated" with a "Launch Servers" / "Advanced Access Servers" row
 * above each block, rows "Pair # | Elyos | Asmodian", and rows "Elyos | Asmodian". The region is a heading line before each table (none in the Asia notice,
 * which names it in the title). A time cell spans several rows (rowspan): a row that has no fourth cell at all inherits it, an empty fourth cell clears it.
 */
export function parseMatchmaking(html: string): MatchPair[] {
  const pairs: MatchPair[] = [];
  let region: string | null = null;
  let phase: string | null = null;
  for (const part of html.split(/(<table[\s\S]*?<\/table>)/i)) {
    if (!/^<table/i.test(part)) {
      const lines = noticeText(part).split("\n").map((l) => l.replace(/\s+/g, " ").trim()).filter(Boolean);
      for (const line of lines) {
        region = REGIONS[line.toLowerCase()] ?? region;
        phase = phaseOf(line) && line.length < 60 ? phaseOf(line) : phase;
      }
      continue;
    }
    let updated: string | null = null;
    for (const row of part.match(/<tr[\s\S]*?<\/tr>/gi) ?? []) {
      const cells = cellsOf(row);
      const vs = cells.findIndex((c) => /^vs\.?$/i.test(c));
      let elyos: string | undefined;
      let asmodian: string | undefined;
      let timeAt = -1;
      if (vs >= 1 && vs + 1 < cells.length) {
        [elyos, asmodian, timeAt] = [cells[vs - 1], cells[vs + 1], vs + 2];
      } else if (cells.length === 3 && /^\d+$/.test(cells[0])) {
        [elyos, asmodian] = [cells[1], cells[2]];
      } else if (cells.length === 2 && cells[1] && !/^(elyos|asmodian)$/i.test(cells[0]) && !phaseOf(cells[0])) {
        [elyos, asmodian] = [cells[0], cells[1]];
      } else if (cells.length <= 2 && phaseOf(cells[0] ?? "")) {
        phase = phaseOf(cells[0]);
        updated = null;
        continue;
      }
      if (!elyos || !asmodian || /^(elyos|asmodian)$/i.test(elyos)) {
        continue;
      }
      if (timeAt >= 0 && timeAt < cells.length) {
        updated = cells[timeAt] || null;
      }
      pairs.push({ region, elyos, asmodian, phase, updated });
    }
  }
  return pairs;
}

const factionOf = (cell: string): AbyssPair["faction1"] | null => (/^elyos$/i.test(cell) ? "Elyos" : /^asmodian$/i.test(cell) ? "Asmodian" : null);

/**
 * The Abyss tables of a notice ("Pair # | Server 1 | Server 2 | Server 1 Faction | Server 2 Faction"). They come under their own heading ("Abyss Server Pairings")
 * before the rift tables, with the same region headings as those.
 */
export function parseAbyssMatchmaking(html: string): AbyssPair[] {
  const pairs: AbyssPair[] = [];
  let region: string | null = null;
  for (const part of html.split(/(<table[\s\S]*?<\/table>)/i)) {
    if (!/^<table/i.test(part)) {
      for (const line of noticeText(part).split("\n").map((l) => l.replace(/\s+/g, " ").trim()).filter(Boolean)) {
        region = REGIONS[line.toLowerCase()] ?? region;
      }
      continue;
    }
    for (const row of part.match(/<tr[\s\S]*?<\/tr>/gi) ?? []) {
      const cells = cellsOf(row);
      const [faction1, faction2] = [factionOf(cells[3] ?? ""), factionOf(cells[4] ?? "")];
      if (cells.length === 5 && /^\d+$/.test(cells[0]) && cells[1] && cells[2] && faction1 && faction2) {
        pairs.push({ region, server1: cells[1], server2: cells[2], faction1, faction2 });
      }
    }
  }
  return pairs;
}

let notices = new Map<string, MatchNotice>();
let checkedAt: number | null = null;

export function load(): void {
  try {
    if (existsSync(MATCHMAKING_FILE)) {
      const data = JSON.parse(readFileSync(MATCHMAKING_FILE, "utf8")) as { checkedAt?: number; notices?: MatchNotice[] };
      notices = new Map((data.notices ?? []).filter((n) => typeof n?.id === "string" && Array.isArray(n.pairs)).map((n) => [n.id, n]));
      checkedAt = typeof data.checkedAt === "number" ? data.checkedAt : null;
    }
  } catch {
    notices = new Map();
  }
}
function save(): void {
  try {
    mkdirSync(path.dirname(MATCHMAKING_FILE), { recursive: true });
    const tmp = `${MATCHMAKING_FILE}.tmp`;
    writeFileSync(tmp, JSON.stringify({ checkedAt, notices: [...notices.values()] }));
    renameSync(tmp, MATCHMAKING_FILE);
  } catch {
    // the next refresh writes it again
  }
}
export function resetMatchmaking(): void {
  notices = new Map();
  checkedAt = null;
}

const key = (pairs: MatchPair[], abyss: AbyssPair[] = []) =>
  pairs.map((p) => `${p.region ?? ""}|${p.elyos}|${p.asmodian}|${p.phase ?? ""}`).join(";") + "#" + abyss.map((p) => `${p.region ?? ""}|${p.server1}|${p.server2}|${p.faction1}|${p.faction2}`).join(";");

/** Stores a freshly parsed table; a changed set of pairings (not just a new time) adds a version to the history. */
export function recordNotice(item: { id: string; title: string; postedAt: string }, pairs: MatchPair[], now: number, abyss: AbyssPair[] = []): void {
  const known = notices.get(item.id);
  const titleRegion = regionOfTitle(item.title);
  pairs = pairs.map((p) => ({ ...p, region: p.region ?? titleRegion }));
  abyss = abyss.map((p) => ({ ...p, region: p.region ?? titleRegion }));
  const history = known?.history ?? [];
  if (!known || key(known.pairs, known.abyss) !== key(pairs, abyss)) {
    history.push({ at: new Date(now).toISOString(), pairs, ...(abyss.length > 0 ? { abyss } : {}) });
  }
  notices.set(item.id, { id: item.id, title: item.title, region: titleRegion, postedAt: item.postedAt, checkedAt: now, pairs, ...(abyss.length > 0 ? { abyss } : {}), history });
}

/** The search: every English announcement with "matchmaking" in the title is fetched again each run (NC edits the notice in place). Returns how many were parsed. */
export async function refreshMatchmaking(fetchFn: FetchFn = fetch, now = Date.now(), pauseMs = 1_000): Promise<number> {
  const list = announcementList("en").items.filter((x) => /match-?making/i.test(x.title));
  let parsed = 0;
  for (const item of list) {
    try {
      const { body } = await getNc(fetchFn, new URL(`${BASE}/notice_en/article/${encodeURIComponent(item.id)}`), MAX_BYTES);
      const html = (JSON.parse(body.toString("utf8")) as { article?: { content?: { content?: unknown } } })?.article?.content?.content;
      const pairs = typeof html === "string" ? parseMatchmaking(html) : [];
      const abyss = typeof html === "string" ? parseAbyssMatchmaking(html) : [];
      // a notice that merely mentions matchmaking has no table: nothing to keep, a failed parse of a known one keeps the old result
      if (pairs.length > 0 || abyss.length > 0) {
        recordNotice(item, pairs, now, abyss);
        parsed++;
      }
    } catch {
      // try again next time
    }
    if (pauseMs > 0) {
      await new Promise((r) => setTimeout(r, pauseMs));
    }
  }
  if (list.length > 0) {
    checkedAt = now;
    save();
  }
  return parsed;
}

/** The current pairs per region: from the newest notice (by posting time) that has pairs for that region. */
export function matchmakingView(): {
  checkedAt: string | null;
  regions: { region: string | null; noticeId: string; title: string; postedAt: string; pairs: MatchPair[] }[];
  abyss: { region: string | null; noticeId: string; pairs: AbyssPair[] }[];
} {
  const abyssLatest = new Map<string | null, { n: MatchNotice; pairs: AbyssPair[] }>();
  for (const n of notices.values()) {
    for (const region of new Set((n.abyss ?? []).map((p) => p.region))) {
      const cur = abyssLatest.get(region);
      if (!cur || n.postedAt > cur.n.postedAt) {
        abyssLatest.set(region, { n, pairs: (n.abyss ?? []).filter((p) => p.region === region) });
      }
    }
  }
  const latest = new Map<string | null, { n: MatchNotice; pairs: MatchPair[] }>();
  for (const n of notices.values()) {
    for (const region of new Set(n.pairs.map((p) => p.region))) {
      const cur = latest.get(region);
      if (!cur || n.postedAt > cur.n.postedAt) {
        latest.set(region, { n, pairs: n.pairs.filter((p) => p.region === region) });
      }
    }
  }
  return {
    checkedAt: checkedAt === null ? null : new Date(checkedAt).toISOString(),
    regions: [...latest].map(([region, { n, pairs }]) => ({ region, noticeId: n.id, title: n.title, postedAt: n.postedAt, pairs })),
    abyss: [...abyssLatest].map(([region, { n, pairs }]) => ({ region, noticeId: n.id, pairs })),
  };
}
