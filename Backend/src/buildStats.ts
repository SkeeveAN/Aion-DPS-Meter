import type Database from "better-sqlite3";
import { unpackJson } from "./db/packedJson.js";

/**
 * "What do the best use?": for one class, what the characters of a group (the Top 10, the Top 25, the Top 10 %, everybody) have chosen: skill levels and
 * specialisation stones, the nodes and the most frequent whole build of every Daevanion board, the gear piece, mana stones and godstone per slot, the Pantheon.
 *
 * SQLite has no materialized view, so the answers are stored in `build_stats` and `refreshBuildStats` rebuilds them as a whole (the job in startBuildStats runs it
 * every night). The pages only read that small table. The groups are prefixes of the ranking, so every profile is read once per class and counted into all
 * groups it belongs to.
 *
 * The "typical build" of a Daevanion board is not the most frequent exact selection (nobody has exactly the same 70 nodes) but the densest cluster: the most
 * characters whose boards differ in at most {@link BUILD_RADIUS_SHARE} of the nodes from one another. It is given as the nodes that most of that cluster has (so it
 * is a group result, not a copy of one player's board) and how many characters play something that close.
 *
 * What the numbers are not: popular is not best (the best have better gear, not necessarily better choices), a cell that fewer than {@link MIN_CELL} characters
 * share is never stored (a group result must not point at one player), and a question with fewer than {@link MIN_SAMPLE} characters behind it is "too little data".
 */

export const GROUPS = ["top10", "top25", "pct10", "all"] as const;
export type Group = (typeof GROUPS)[number];
/** How the ranking is made. Only the combat power exists for (nearly) everybody; a measure from the recorded fights can be added as a second one without a schema change. */
export const METRICS = ["power"] as const;
export type Metric = (typeof METRICS)[number];

/** Fewer characters behind a question and the page shows "too little data" instead of a number. */
export const MIN_SAMPLE = 10;
/** A cell (one option of one question) that fewer characters share is not stored. */
export const MIN_CELL = 3;
/** Two Daevanion boards count as the same build when they differ in at most this share of the nodes (but never fewer than 3 nodes): nobody has exactly the same 70 nodes. */
export const BUILD_RADIUS_SHARE = 0.08;
/** The "typical build" of a board is searched only in groups up to this size (the search compares all pairs); the big "all" group gives node frequencies only. */
export const MAX_CLUSTER_GROUP = 2500;
/** The weight of a profile that is older than the patch date (see {@link BuildStatsOptions.patchDate}). */
export const OLD_DATA_WEIGHT = 0.25;
/** The characters below this level are still levelling and do not say what is played at the top. */
export const MIN_LEVEL = 45;

export const TOPICS = ["skill_level", "skill_stone", "board_node", "board_build", "gear_item", "gear_godstone", "gear_manastone", "pantheon", "title", "pet"] as const;
export type Topic = (typeof TOPICS)[number];

export interface BuildStatsOptions {
  /** ISO date of the last big patch: profiles last updated before it count with {@link OLD_DATA_WEIGHT}. Null = everything counts fully. */
  patchDate?: string | null;
  /** Pause between two chunks of profiles (ms), so the job spreads its reading over time on the slow disk. */
  pauseMs?: number;
  /** Profiles read per chunk. */
  chunk?: number;
  now?: Date;
}

/** One stored cell. */
export interface StatRow {
  classId: number;
  metric: Metric;
  grp: Group;
  topic: Topic;
  key: string;
  option: string;
  n: number;
  w: number;
  baseN: number;
  baseW: number;
  detail: string | null;
}

export interface GroupMeta {
  classId: number;
  metric: Metric;
  grp: Group;
  size: number;
  weight: number;
}

/** What one character contributes. */
export interface Facts {
  skills: { id: number; level: number; stones?: number[] }[];
  boards: { board: number; nodes: number[] }[];
  gear: { slot: number; itemId: number; godstone?: number; stones?: { stat: number; tier: number }[] }[];
  pantheon: { slot: number; id: number }[];
  titles: { slot: number; titleId: number }[];
  pet: number | null;
}

function parseArray(text: string): unknown[] {
  try {
    const v: unknown = JSON.parse(text);
    return Array.isArray(v) ? v : [];
  } catch {
    return [];
  }
}

const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
const rec = (v: unknown): Record<string, unknown> => (v && typeof v === "object" ? (v as Record<string, unknown>) : {});

/** The facts of one profile row (the JSON columns may be plain text or gzip). Anything that does not look right is left out. */
export function factsOf(raw: { skills: unknown; daevanion: unknown; gear: unknown; pantheon: unknown; titles: unknown; pet: number | null }): Facts {
  const skills: Facts["skills"] = [];
  for (const s of parseArray(unpackJson(raw.skills as string | Buffer))) {
    const r = rec(s);
    if (isNum(r.id) && isNum(r.level) && r.level > 0) {
      const stones = Array.isArray(r.stones) ? r.stones.filter(isNum) : undefined;
      skills.push({ id: r.id, level: r.level, ...(stones && stones.some((x) => x > 0) ? { stones } : {}) });
    }
  }
  const boards: Facts["boards"] = [];
  for (const b of parseArray(unpackJson(raw.daevanion as string | Buffer))) {
    const r = rec(b);
    if (isNum(r.board) && Array.isArray(r.nodes) && r.nodes.length > 0) {
      boards.push({ board: r.board, nodes: [...new Set(r.nodes.filter(isNum))].sort((a, c) => a - c) });
    }
  }
  const gear: Facts["gear"] = [];
  for (const g of parseArray(unpackJson(raw.gear as string | Buffer))) {
    const r = rec(g);
    if (isNum(r.slot) && isNum(r.itemId)) {
      const stones = Array.isArray(r.stones)
        ? r.stones.map(rec).filter((s) => isNum(s.stat) && s.stat > 0 && isNum(s.tier)).map((s) => ({ stat: s.stat as number, tier: s.tier as number }))
        : undefined;
      gear.push({ slot: r.slot, itemId: r.itemId, ...(isNum(r.godstone) ? { godstone: r.godstone } : {}), ...(stones && stones.length > 0 ? { stones } : {}) });
    }
  }
  const pantheon: Facts["pantheon"] = [];
  try {
    const p = rec(JSON.parse(unpackJson(raw.pantheon as string | Buffer) || "{}"));
    const first = rec(Array.isArray(p.presets) ? p.presets[0] : undefined);
    for (const s of Array.isArray(first.slots) ? first.slots : []) {
      const r = rec(s);
      if (isNum(r.slot) && isNum(r.id)) {
        pantheon.push({ slot: r.slot, id: r.id });
      }
    }
  } catch {
    // no Pantheon data
  }
  const titles: Facts["titles"] = [];
  for (const t of parseArray(unpackJson(raw.titles as string | Buffer))) {
    const r = rec(t);
    if (isNum(r.slot) && isNum(r.titleId)) {
      titles.push({ slot: r.slot, titleId: r.titleId });
    }
  }
  return { skills, boards, gear, pantheon, titles, pet: raw.pet && raw.pet > 0 ? raw.pet : null };
}

/** How many characters a group holds out of `ranked` ranked ones (`total` = all of them): the Top groups come from the ranked part only. */
export function groupSize(group: Group, ranked: number, total: number): number {
  switch (group) {
    case "top10":
      return Math.min(10, ranked);
    case "top25":
      return Math.min(25, ranked);
    case "pct10":
      return ranked === 0 ? 0 : Math.max(1, Math.ceil(ranked * 0.1));
    case "all":
      return total;
  }
}

interface Cell {
  n: number;
  w: number;
}

const SEP = "\u0001";

const popcount = (x: number): number => {
  x -= (x >>> 1) & 0x55555555;
  x = (x & 0x33333333) + ((x >>> 2) & 0x33333333);
  return (((x + (x >>> 4)) & 0x0f0f0f0f) * 0x01010101) >>> 24;
};

/**
 * The densest cluster of boards: the board with the most others within the radius (itself included), and from that cluster the nodes that at least half of it has.
 * Null when fewer than {@link MIN_SAMPLE} boards exist or the densest cluster has fewer than {@link MIN_CELL} members. Boards are compared as bit sets (a few words each).
 */
export function typicalBuild(list: { nodes: number[]; w: number }[]): { n: number; w: number; nodes: number[] } | null {
  if (list.length < MIN_SAMPLE) {
    return null;
  }
  const distinct = [...new Set(list.flatMap((b) => b.nodes))].sort((a, b) => a - b);
  const at = new Map(distinct.map((node, i) => [node, i]));
  const words = Math.ceil(distinct.length / 32);
  const bits = list.map((b) => {
    const v = new Uint32Array(words);
    for (const node of b.nodes) {
      const i = at.get(node)!;
      v[i >>> 5] = (v[i >>> 5]! | (1 << (i & 31))) >>> 0;
    }
    return v;
  });
  const sizes = list.map((b) => b.nodes.length).sort((a, b) => a - b);
  const radius = Math.max(3, Math.round(BUILD_RADIUS_SHARE * sizes[sizes.length >> 1]!));
  const near = (i: number, j: number): boolean => {
    let d = 0;
    const a = bits[i]!;
    const b = bits[j]!;
    for (let k = 0; k < words; k++) {
      d += popcount((a[k]! ^ b[k]!) >>> 0);
      if (d > radius) {
        return false;
      }
    }
    return true;
  };
  const weight = list.map((b) => b.w);
  const count = new Array<number>(list.length).fill(1);
  const mass = [...weight];
  for (let i = 0; i < list.length; i++) {
    for (let j = i + 1; j < list.length; j++) {
      if (near(i, j)) {
        count[i]!++;
        count[j]!++;
        mass[i]! += weight[j]!;
        mass[j]! += weight[i]!;
      }
    }
  }
  let best = 0;
  for (let i = 1; i < list.length; i++) {
    if (mass[i]! > mass[best]! || (mass[i] === mass[best] && count[i]! > count[best]!)) {
      best = i;
    }
  }
  if (count[best]! < MIN_CELL) {
    return null;
  }
  const members = list.filter((_, i) => i === best || near(best, i));
  const total = members.reduce((s, m) => s + m.w, 0);
  const votes = new Map<number, number>();
  for (const m of members) {
    for (const node of m.nodes) {
      votes.set(node, (votes.get(node) ?? 0) + m.w);
    }
  }
  const nodes = [...votes].filter(([, v]) => v >= total / 2).map(([node]) => node).sort((a, b) => a - b);
  return { n: members.length, w: total, nodes };
}

/** Counts the facts of the characters of one group. */
export class GroupAccumulator {
  size = 0;
  weight = 0;
  private cells = new Map<string, Cell>();
  private bases = new Map<string, Cell>();
  private boardLists = new Map<number, { nodes: number[]; w: number }[]>();

  /** `clusterBoards`: keep every board for the search of the typical build (only for groups up to {@link MAX_CLUSTER_GROUP}). */
  constructor(private readonly clusterBoards = true) {}

  private bump(map: Map<string, Cell>, id: string, w: number): void {
    const c = map.get(id);
    if (c) {
      c.n++;
      c.w += w;
    } else {
      map.set(id, { n: 1, w });
    }
  }

  /** One question of one character: it can be asked (base) and these are its answers (one count per distinct answer). */
  private ask(topic: Topic, key: string | number, options: (string | number)[], w: number): void {
    this.bump(this.bases, `${topic}${SEP}${key}`, w);
    for (const o of new Set(options.map(String))) {
      this.bump(this.cells, `${topic}${SEP}${key}${SEP}${o}`, w);
    }
  }

  add(f: Facts, w: number): void {
    this.size++;
    this.weight += w;
    for (const s of f.skills) {
      this.ask("skill_level", s.id, [s.level], w);
      if (s.stones) {
        s.stones.forEach((choice, i) => this.ask("skill_stone", `${s.id}:${i + 1}`, [choice], w));
      }
    }
    for (const b of f.boards) {
      this.ask("board_node", b.board, b.nodes, w);
      if (this.clusterBoards) {
        (this.boardLists.get(b.board) ?? this.boardLists.set(b.board, []).get(b.board)!).push({ nodes: b.nodes, w });
      }
    }
    for (const g of f.gear) {
      this.ask("gear_item", g.slot, [g.itemId], w);
      if (g.godstone !== undefined) {
        this.ask("gear_godstone", g.slot, [g.godstone], w);
      }
      if (g.stones) {
        this.ask("gear_manastone", g.slot, g.stones.map((s) => `${s.stat}:${s.tier}`), w);
      }
    }
    for (const p of f.pantheon) {
      this.ask("pantheon", p.slot, [p.id], w);
    }
    for (const t of f.titles) {
      this.ask("title", t.slot, [t.titleId], w);
    }
    if (f.pet !== null) {
      this.ask("pet", "active", [f.pet], w);
    }
  }

  /** The cells worth storing: shared by at least {@link MIN_CELL} characters; of the whole builds only the most frequent ones per board. */
  rows(classId: number, metric: Metric, grp: Group): StatRow[] {
    const out: StatRow[] = [];
    for (const [id, c] of this.cells) {
      if (c.n < MIN_CELL) {
        continue;
      }
      const [topic, key, option] = id.split(SEP) as [Topic, string, string];
      const base = this.bases.get(`${topic}${SEP}${key}`)!;
      out.push({ classId, metric, grp, topic, key, option, n: c.n, w: c.w, baseN: base.n, baseW: base.w, detail: null });
    }
    for (const [board, list] of this.boardLists) {
      const typical = typicalBuild(list);
      if (typical) {
        const base = this.bases.get(`board_node${SEP}${board}`)!;
        out.push({ classId, metric, grp, topic: "board_build", key: String(board), option: "typical", n: typical.n, w: typical.w, baseN: base.n, baseW: base.w, detail: JSON.stringify(typical.nodes) });
      }
    }
    return out;
  }
}

interface Candidate {
  playerId: number;
  power: number | null;
  updatedAt: string;
}

/** The characters of a class in ranking order (strongest first); those without a power value come last and only count for "all". */
function candidatesOf(sqlite: Database.Database, classId: number): Candidate[] {
  const rows = sqlite
    .prepare(
      `select pp.player_id as playerId, nc.combat_power as power, pp.updated_at as updatedAt
       from player_profiles pp
       join players p on p.id = pp.player_id
       left join player_nc_character nc on nc.player_id = pp.player_id
       where pp.class_id = ? and p.deleted_at is null and coalesce(pp.level, 0) >= ?
         and not (pp.skills_json = '[]' and pp.daevanion_json = '[]' and pp.gear_json = '[]')`,
    )
    .all(classId, MIN_LEVEL) as Candidate[];
  rows.sort((a, b) => (b.power ?? -1) - (a.power ?? -1) || a.playerId - b.playerId);
  return rows;
}

const pause = (ms: number) => new Promise<void>((r) => (ms > 0 ? setTimeout(r, ms) : setImmediate(r)));

/** Computes the answers of one class (all groups, one pass over the profiles). Reads only. */
export async function computeClass(
  sqlite: Database.Database,
  classId: number,
  opts: BuildStatsOptions = {},
): Promise<{ rows: StatRow[]; meta: GroupMeta[] }> {
  const metric: Metric = "power";
  const candidates = candidatesOf(sqlite, classId);
  const ranked = candidates.filter((c) => c.power !== null && c.power > 0).length;
  const limits = Object.fromEntries(GROUPS.map((g) => [g, groupSize(g, ranked, candidates.length)])) as Record<Group, number>;
  const acc = Object.fromEntries(GROUPS.map((g) => [g, new GroupAccumulator(limits[g] <= MAX_CLUSTER_GROUP)])) as Record<Group, GroupAccumulator>;
  const patch = opts.patchDate ?? null;
  const chunk = opts.chunk ?? 300;
  for (let from = 0; from < candidates.length; from += chunk) {
    const part = candidates.slice(from, from + chunk);
    const ids = part.map((c) => c.playerId);
    const profiles = sqlite
      .prepare(
        `select player_id as playerId, skills_json as skills, daevanion_json as daevanion, gear_json as gear, pantheon_json as pantheon, titles_json as titles, active_pet as pet
         from player_profiles where player_id in (${ids.map(() => "?").join(",")})`,
      )
      .all(...ids) as (Parameters<typeof factsOf>[0] & { playerId: number })[];
    const byId = new Map(profiles.map((p) => [p.playerId, p]));
    part.forEach((cand, i) => {
      const rank = from + i;
      const row = byId.get(cand.playerId);
      if (!row) {
        return;
      }
      const facts = factsOf(row);
      const w = patch !== null && cand.updatedAt < patch ? OLD_DATA_WEIGHT : 1;
      for (const g of GROUPS) {
        if (g === "all" || rank < limits[g]) {
          acc[g].add(facts, w);
        }
      }
    });
    await pause(opts.pauseMs ?? 0);
  }
  const rows: StatRow[] = [];
  const meta: GroupMeta[] = [];
  for (const g of GROUPS) {
    rows.push(...acc[g].rows(classId, metric, g));
    meta.push({ classId, metric, grp: g, size: acc[g].size, weight: acc[g].weight });
  }
  return { rows, meta };
}

/** Replaces the stored answers of one class in one transaction. */
export function storeClass(sqlite: Database.Database, classId: number, result: { rows: StatRow[]; meta: GroupMeta[] }, builtAt = new Date().toISOString()): void {
  const del1 = sqlite.prepare("delete from build_stats where class_id = ?");
  const del2 = sqlite.prepare("delete from build_stats_meta where class_id = ?");
  const ins = sqlite.prepare(
    "insert into build_stats (class_id, metric, grp, topic, key, option, n, w, base_n, base_w, detail) values (?,?,?,?,?,?,?,?,?,?,?)",
  );
  const insMeta = sqlite.prepare("insert into build_stats_meta (class_id, metric, grp, size, weight, built_at) values (?,?,?,?,?,?)");
  sqlite.transaction(() => {
    del1.run(classId);
    del2.run(classId);
    for (const r of result.rows) {
      ins.run(r.classId, r.metric, r.grp, r.topic, r.key, r.option, r.n, r.w, r.baseN, r.baseW, r.detail);
    }
    for (const m of result.meta) {
      insMeta.run(m.classId, m.metric, m.grp, m.size, m.weight, builtAt);
    }
  })();
}

/** Rebuilds the answers of the given classes (all eight by default). A class that fails keeps its old answers. */
export async function refreshBuildStats(
  sqlite: Database.Database,
  classIds: number[] = [1, 2, 3, 4, 5, 6, 7, 8],
  opts: BuildStatsOptions = {},
): Promise<{ classId: number; rows: number }[]> {
  const done: { classId: number; rows: number }[] = [];
  for (const classId of classIds) {
    const result = await computeClass(sqlite, classId, opts);
    storeClass(sqlite, classId, result, (opts.now ?? new Date()).toISOString());
    done.push({ classId, rows: result.rows.length });
  }
  return done;
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------------------
// Reading

export interface Option {
  option: string;
  n: number;
  /** Share of the characters the question was asked of (weighted), 0..1. */
  share: number;
  detail?: unknown;
}

export interface Question {
  topic: Topic;
  key: string;
  /** Characters the question was asked of. */
  base: number;
  /** Fewer than {@link MIN_SAMPLE}: the page shows "too little data" and no numbers. */
  tooFew: boolean;
  options: Option[];
}

export interface Answer {
  classId: number;
  metric: Metric;
  group: Group;
  /** Characters in the group (ranked ones for the Top groups). */
  size: number;
  builtAt: string | null;
  tooFew: boolean;
  questions: Question[];
}

/** The stored answer of one class and group (optionally one topic). Null when nothing was built yet. */
export function readAnswer(sqlite: Database.Database, classId: number, group: Group, metric: Metric = "power", topic?: Topic): Answer | null {
  const meta = sqlite
    .prepare("select size, built_at as builtAt from build_stats_meta where class_id = ? and metric = ? and grp = ?")
    .get(classId, metric, group) as { size: number; builtAt: string } | undefined;
  if (!meta) {
    return null;
  }
  const rows = sqlite
    .prepare(
      `select topic, key, option, n, w, base_n as baseN, base_w as baseW, detail from build_stats
       where class_id = ? and metric = ? and grp = ? ${topic ? "and topic = ?" : ""} order by topic, key, w desc, option`,
    )
    .all(...(topic ? [classId, metric, group, topic] : [classId, metric, group])) as {
    topic: Topic;
    key: string;
    option: string;
    n: number;
    w: number;
    baseN: number;
    baseW: number;
    detail: string | null;
  }[];
  const questions = new Map<string, Question>();
  for (const r of rows) {
    const id = `${r.topic}${SEP}${r.key}`;
    let q = questions.get(id);
    if (!q) {
      q = { topic: r.topic, key: r.key, base: r.baseN, tooFew: r.baseN < MIN_SAMPLE, options: [] };
      questions.set(id, q);
    }
    if (!q.tooFew) {
      q.options.push({ option: r.option, n: r.n, share: r.baseW > 0 ? r.w / r.baseW : 0, ...(r.detail ? { detail: JSON.parse(r.detail) } : {}) });
    }
  }
  return { classId, metric, group, size: meta.size, builtAt: meta.builtAt, tooFew: meta.size < MIN_SAMPLE, questions: [...questions.values()] };
}

/** The weighted median of a level question (options are levels). Null when the question has too little data. */
export function medianLevel(q: Question): number | null {
  if (q.tooFew || q.options.length === 0) {
    return null;
  }
  const levels = q.options.map((o) => ({ level: Number(o.option), share: o.share })).sort((a, b) => a.level - b.level);
  const total = levels.reduce((s, l) => s + l.share, 0);
  let run = 0;
  for (const l of levels) {
    run += l.share;
    if (run >= total / 2) {
      return l.level;
    }
  }
  return levels[levels.length - 1]!.level;
}

export interface OptimalCharacter {
  classId: number;
  group: Group;
  size: number;
  builtAt: string | null;
  tooFew: boolean;
  /** Per skill the median level. */
  skills: { skill: number; level: number }[];
  /** Per skill and stone slot the most frequent choice. */
  stones: { skill: number; slot: number; choice: number; share: number }[];
  /** Per slot the most frequent piece, godstone, and the mana stones that at least half of the characters have. */
  gear: { slot: number; itemId: number; share: number; godstone?: { id: number; share: number }; manastones: { stat: number; tier: number; share: number }[] }[];
  /** Per board the most frequent whole build (a build somebody plays) and the nodes that most characters have. */
  boards: { board: number; build: { nodes: number[]; share: number } | null; commonNodes: { node: number; share: number }[] }[];
  pantheon: { slot: number; id: number; share: number }[];
  titles: { slot: number; titleId: number; share: number }[];
}

const top = (q: Question) => q.options[0];

/** The "optimal character" of a group from the stored answers: choices by their most frequent option, numbers by their median, whole boards by the most frequent real build. */
export function optimalCharacter(answer: Answer): OptimalCharacter {
  const of = (topic: Topic) => answer.questions.filter((q) => q.topic === topic && !q.tooFew && q.options.length > 0);
  const slotNo = (k: string) => Number(k);
  const stones = of("skill_stone").map((q) => {
    const [skill, slot] = q.key.split(":").map(Number) as [number, number];
    const best = top(q)!;
    return { skill, slot, choice: Number(best.option), share: best.share };
  });
  const godstones = new Map(of("gear_godstone").map((q) => [q.key, top(q)!]));
  const manastones = new Map(of("gear_manastone").map((q) => [q.key, q.options.filter((o) => o.share >= 0.5)]));
  const buildByBoard = new Map(of("board_build").map((q) => [q.key, top(q)!]));
  const boardKeys = [...new Set(answer.questions.filter((q) => q.topic === "board_node").map((q) => q.key))];
  return {
    classId: answer.classId,
    group: answer.group,
    size: answer.size,
    builtAt: answer.builtAt,
    tooFew: answer.tooFew,
    skills: answer.questions
      .filter((q) => q.topic === "skill_level")
      .flatMap((q) => {
        const level = medianLevel(q);
        return level === null ? [] : [{ skill: slotNo(q.key), level }];
      }),
    stones,
    gear: of("gear_item").map((q) => {
      const best = top(q)!;
      const god = godstones.get(q.key);
      return {
        slot: slotNo(q.key),
        itemId: Number(best.option),
        share: best.share,
        ...(god ? { godstone: { id: Number(god.option), share: god.share } } : {}),
        manastones: (manastones.get(q.key) ?? []).map((o) => {
          const [stat, tier] = o.option.split(":").map(Number) as [number, number];
          return { stat, tier, share: o.share };
        }),
      };
    }),
    boards: boardKeys.map((key) => {
      const nodes = answer.questions.find((q) => q.topic === "board_node" && q.key === key && !q.tooFew);
      const build = buildByBoard.get(key);
      return {
        board: slotNo(key),
        build: build && Array.isArray(build.detail) ? { nodes: build.detail as number[], share: build.share } : null,
        commonNodes: (nodes?.options ?? []).filter((o) => o.share >= 0.5).map((o) => ({ node: Number(o.option), share: o.share })),
      };
    }),
    pantheon: of("pantheon").map((q) => ({ slot: slotNo(q.key), id: Number(top(q)!.option), share: top(q)!.share })),
    titles: of("title").map((q) => ({ slot: slotNo(q.key), titleId: Number(top(q)!.option), share: top(q)!.share })),
  };
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------------------
// One character against a group

export interface Comparison {
  classId: number;
  group: Group;
  size: number;
  builtAt: string | null;
  /** The group has fewer than {@link MIN_SAMPLE} characters: no comparison, only the player's own place. */
  tooFew: boolean;
  player: { power: number | null; rank: number | null; ranked: number; inGroup: boolean };
  /** Per skill the own level, the group's median and the difference (own minus median). */
  skills: { skill: number; own: number; median: number; diff: number }[];
  /** Per skill and stone slot what the character chose, what most of the group chose and how many chose the same. Only where the character's stones were read. */
  stones: { skill: number; slot: number; own: number; top: number; topShare: number; ownShare: number }[];
  /** Per slot the own piece against the group's most frequent one; `ownShare` is the share of the group wearing the own piece (0 = fewer than three or none). */
  gear: { slot: number; own: number; top: number; topShare: number; ownShare: number; same: boolean; godstone?: { own: number | null; top: number; topShare: number } }[];
  /** Per board: the nodes most of the group has and the character lacks, the character's nodes that hardly anybody in the group has, and the overlap with the typical build. */
  boards: { board: number; ownNodes: number; commonNodes: number; missing: number[]; rare: number[]; typicalOverlap: number | null }[];
}

/** Where a character stands in its class's ranking: its rank (1 = strongest) and how many ranked characters there are. */
export function rankOf(sqlite: Database.Database, classId: number, power: number | null): { rank: number | null; ranked: number } {
  const base = `from player_profiles pp join players p on p.id = pp.player_id join player_nc_character nc on nc.player_id = pp.player_id
                where pp.class_id = ? and p.deleted_at is null and coalesce(pp.level, 0) >= ? and nc.combat_power > 0`;
  const ranked = (sqlite.prepare(`select count(*) as c ${base}`).get(classId, MIN_LEVEL) as { c: number }).c;
  if (power === null || power <= 0) {
    return { rank: null, ranked };
  }
  const above = (sqlite.prepare(`select count(*) as c ${base} and nc.combat_power > ?`).get(classId, MIN_LEVEL, power) as { c: number }).c;
  return { rank: above + 1, ranked };
}

/**
 * One character against the stored answers of its own class. Null when the character has no class or no profile, or the answers were not built yet.
 * Reads only the stored group result, so it names nobody else. `answers` is the database that holds the stored answers (the same one unless a trial run keeps them apart).
 */
export function compareToGroup(sqlite: Database.Database, playerId: number, group: Group, metric: Metric = "power", answers: Database.Database = sqlite): Comparison | null {
  const row = sqlite
    .prepare(
      `select pp.class_id as classId, pp.skills_json as skills, pp.daevanion_json as daevanion, pp.gear_json as gear, pp.pantheon_json as pantheon, pp.titles_json as titles,
              pp.active_pet as pet, nc.combat_power as power
       from player_profiles pp left join player_nc_character nc on nc.player_id = pp.player_id where pp.player_id = ?`,
    )
    .get(playerId) as (Parameters<typeof factsOf>[0] & { classId: number | null; power: number | null }) | undefined;
  if (!row || row.classId === null) {
    return null;
  }
  const answer = readAnswer(answers, row.classId, group, metric);
  if (!answer) {
    return null;
  }
  const facts = factsOf(row);
  const { rank, ranked } = rankOf(sqlite, row.classId, row.power);
  const q = (topic: Topic, key: string | number) => answer.questions.find((x) => x.topic === topic && x.key === String(key) && !x.tooFew);
  const comparison: Comparison = {
    classId: row.classId,
    group,
    size: answer.size,
    builtAt: answer.builtAt,
    tooFew: answer.tooFew,
    player: { power: row.power, rank, ranked, inGroup: group === "all" || (rank !== null && rank <= answer.size) },
    skills: [],
    stones: [],
    gear: [],
    boards: [],
  };
  if (answer.tooFew) {
    return comparison;
  }
  for (const s of facts.skills) {
    const question = q("skill_level", s.id);
    const median = question ? medianLevel(question) : null;
    if (median !== null) {
      comparison.skills.push({ skill: s.id, own: s.level, median, diff: s.level - median });
    }
    s.stones?.forEach((choice, i) => {
      const stone = q("skill_stone", `${s.id}:${i + 1}`);
      if (stone && stone.options[0]) {
        comparison.stones.push({
          skill: s.id,
          slot: i + 1,
          own: choice,
          top: Number(stone.options[0].option),
          topShare: stone.options[0].share,
          ownShare: stone.options.find((o) => o.option === String(choice))?.share ?? 0,
        });
      }
    });
  }
  const godstones = new Map(answer.questions.filter((x) => x.topic === "gear_godstone" && !x.tooFew).map((x) => [x.key, x]));
  for (const g of facts.gear) {
    const question = q("gear_item", g.slot);
    const best = question?.options[0];
    if (!question || !best) {
      continue;
    }
    const god = godstones.get(String(g.slot))?.options[0];
    comparison.gear.push({
      slot: g.slot,
      own: g.itemId,
      top: Number(best.option),
      topShare: best.share,
      ownShare: question.options.find((o) => o.option === String(g.itemId))?.share ?? 0,
      same: String(g.itemId) === best.option,
      ...(god ? { godstone: { own: g.godstone ?? null, top: Number(god.option), topShare: god.share } } : {}),
    });
  }
  for (const b of facts.boards) {
    const nodes = q("board_node", b.board);
    if (!nodes) {
      continue;
    }
    const own = new Set(b.nodes);
    const share = new Map(nodes.options.map((o) => [Number(o.option), o.share]));
    const common = nodes.options.filter((o) => o.share >= 0.5).map((o) => Number(o.option));
    const typical = q("board_build", b.board)?.options[0]?.detail;
    const typicalNodes = Array.isArray(typical) ? (typical as number[]) : null;
    comparison.boards.push({
      board: b.board,
      ownNodes: b.nodes.length,
      commonNodes: common.length,
      missing: common.filter((n) => !own.has(n)),
      rare: b.nodes.filter((n) => (share.get(n) ?? 0) < 0.25),
      typicalOverlap: typicalNodes && typicalNodes.length > 0 ? typicalNodes.filter((n) => own.has(n)).length / typicalNodes.length : null,
    });
  }
  return comparison;
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------------------
// The job

/** Rebuilds the answers a few minutes after the start when they are missing or a day old, then every night at the given local hour. Off under tests. */
export function startBuildStats(sqlite: Database.Database, log: { info: (o: object, m: string) => void; error: (e: unknown) => void }): void {
  if (process.env.NODE_TEST_CONTEXT) {
    return;
  }
  let running = false;
  const run = async () => {
    if (running) {
      return;
    }
    running = true;
    try {
      const started = Date.now();
      const done = await refreshBuildStats(sqlite, undefined, {
        patchDate: process.env.BUILD_STATS_PATCH_DATE || null,
        pauseMs: Number(process.env.BUILD_STATS_PAUSE_MS ?? 150),
      });
      log.info({ classes: done.length, rows: done.reduce((s, d) => s + d.rows, 0), seconds: Math.round((Date.now() - started) / 1000) }, "build stats rebuilt");
    } catch (err) {
      log.error(err);
    } finally {
      running = false;
    }
  };
  const stale = (hours: number) => {
    const row = sqlite.prepare("select min(built_at) as at from build_stats_meta").get() as { at: string | null };
    return !row.at || Date.now() - Date.parse(row.at) > hours * 3600_000;
  };
  setTimeout(() => {
    if (stale(26)) {
      void run();
    }
  }, 10 * 60_000).unref();
  // Nightly, after the backup (04:30) and the Sunday VACUUM (04:40); the hour is local server time.
  setInterval(() => {
    if (new Date().getHours() === Number(process.env.BUILD_STATS_HOUR ?? 5) && stale(20)) {
      void run();
    }
  }, 15 * 60_000).unref();
}
