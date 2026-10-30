import { eq, inArray } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterBuffUsage, encounterParticipants, encounterTimelines, encounters, players } from "./db/schema.js";
import { buffIcon, buffNames, classifyEncounterBuffs, isFood, type BuffKind } from "./encounterBuffs.js";
import { timelineJsonOf } from "./uploadArchive.js";
import { skillChains, skillIconByName, skillInfoById, skillNamesByName } from "./profile.js";

/** A stored time axis of one participant (see uploadSchema.ts timelineSchema), with the start of the upload it came from. */
export interface StoredTimeline {
  origin: string;
  skills: { id: number; name: string }[];
  times: number[];
  index: number[];
  deaths: number[];
  focus?: { onBoss: number; onOthers: number; switches: number };
  /** Damage per 5-second bucket, add damage, spans of being dead and damage taken (see uploadSchema.ts analysisSchema); from clients that send it. */
  analysis?: StoredAnalysis;
}

export interface StoredAnalysis {
  damage?: number[];
  addDamage?: number;
  dead?: number[];
  taken?: TakenRow[];
}

export interface TakenRow {
  from: "boss" | "add" | "other";
  skill: string;
  hits: number;
  total: number;
  max: number;
}

export interface TimelineBuffIn {
  skillId: number;
  skill: string;
  casts: number;
  uptimePermille: number;
  givers: string[];
  /** Start and end of each span, flattened, tenths of a second from `origin`. */
  spans: number[];
  origin: string;
}

export interface TimelinePlayerIn {
  id: number;
  name: string;
  className: string;
  timeline: StoredTimeline | null;
  buffs: TimelineBuffIn[];
  /** The row's damage is estimated from hit counts (a crowded world boss): it never enters the analysis figures. */
  estimated?: boolean;
}

export interface SkillBuffShare {
  skillId: number;
  /** Share of the skill's uses during which the effect was on the player, 0..1. */
  share: number;
}

export interface TimelinePlayerOut {
  participantId: number;
  name: string;
  className: string;
  skills: { id: number; name: string; names?: Record<string, string>; icon: string | null; iconKind: "skill" | "item"; uses: number }[];
  /** [seconds from the start of the fight, index into skills] */
  uses: [number, number][];
  deaths: number[];
  /** Seconds with the boss selected and with anything else, and the changes from one target to another. */
  focus: { onBoss: number; onOthers: number; switches: number } | null;
  buffs: { skillId: number; name: string; names?: Record<string, string>; icon: string | null; iconKind: "skill" | "item" | null; kind: BuffKind; food: boolean; uptimePermille: number; spans: [number, number][]; usable?: UsableOut }[];
  /** Per skill (index into skills): its number of uses and which effects were on the player at those moments. */
  skillBuffs: { skill: number; uses: number; buffs: SkillBuffShare[] }[];
  /** Chain skills: per used skill with a follow-up, how often the follow-up came within its window. */
  chains: ChainOut[];
}

/** One chain of a player: after `from` the client allows `to` for a few seconds; how many uses of `from` were followed by `to` in time. */
export interface ChainOut {
  /** Index into the player's skills. */
  from: number;
  to: { id: number; name: string; names?: Record<string, string>; icon: string | null; iconKind: "skill" };
  windowSeconds: number;
  uses: number;
  taken: number;
}

/** One effect on the boss: how long it was on, who gave it, and when (seconds from the start of the fight). */
export interface BossDebuffOut {
  skillId: number;
  name: string;
  names?: Record<string, string>;
  icon: string | null;
  iconKind: "skill" | "item" | null;
  casts: number;
  uptimePermille: number;
  givers: string[];
  spans: [number, number][];
}

/** Bucket width of the damage curves, in seconds (the client's buckets). */
export const BUCKET_SECONDS = 5;

/** Seconds of an effect on a player that could count: while he was dead, while alive without using a skill nearby, and the rest. */
export interface UsableOut {
  seconds: number;
  dead: number;
  idle: number;
  active: number;
}

export interface AnalysisPlayerOut {
  participantId: number;
  name: string;
  className: string;
  /** Damage to the boss per bucket, lined up on the start of the encounter. */
  damage: number[];
  addDamage: number;
  deadSeconds: number;
  /** 0..1 of the fight's length. */
  aliveShare: number;
  deaths: number;
  taken: TakenRow[];
}

export interface AnalysisOut {
  bucketSeconds: number;
  groupDamage: number[];
  coverage: { players: number; withAnalysis: number };
  players: AnalysisPlayerOut[];
}

export interface EncounterTimelineOut {
  durationSeconds: number;
  /** Only when at least one player's client sent the analysis. */
  analysis?: AnalysisOut;
  /** [seconds, thousandths of the boss's health] */
  bossHp: [number, number][];
  /** The effects the players put on the boss, longest first. */
  bossDebuffs: BossDebuffOut[];
  players: TimelinePlayerOut[];
}

/** Effects that were on the player in less of a skill's uses than this are left out of the skill's line. */
const MIN_SKILL_BUFF_SHARE = 0.05;
const MAX_SKILL_LINES = 20;

const shiftSeconds = (origin: string, encounterStart: number): number => (Date.parse(origin) - encounterStart) / 1000;
const round1 = (v: number) => Math.round(v * 10) / 10;

function insideAny(t: number, spans: [number, number][]): boolean {
  return spans.some(([s, e]) => t >= s && t <= e);
}

type Span = [number, number];

/** The union of spans as sorted, disjoint spans; empty and reversed spans are dropped. */
export function unionSpans(spans: Span[]): Span[] {
  const sorted = spans.filter(([s, e]) => e > s).sort((a, b) => a[0] - b[0]);
  const out: Span[] = [];
  for (const [s, e] of sorted) {
    const last = out.at(-1);
    if (last && s <= last[1]) {
      last[1] = Math.max(last[1], e);
    } else {
      out.push([s, e]);
    }
  }
  return out;
}

/** The parts both lists (sorted, disjoint) cover. */
export function intersectSpans(a: Span[], b: Span[]): Span[] {
  const out: Span[] = [];
  let i = 0;
  let j = 0;
  while (i < a.length && j < b.length) {
    const s = Math.max(a[i]![0], b[j]![0]);
    const e = Math.min(a[i]![1], b[j]![1]);
    if (e > s) {
      out.push([s, e]);
    }
    if (a[i]![1] < b[j]![1]) {
      i++;
    } else {
      j++;
    }
  }
  return out;
}

/** The parts of `a` that `b` does not cover (both sorted, disjoint). */
export function subtractSpans(a: Span[], b: Span[]): Span[] {
  const out: Span[] = [];
  for (const [s0, e] of a) {
    let s = s0;
    for (const [bs, be] of b) {
      if (be <= s || bs >= e) {
        continue;
      }
      if (bs > s) {
        out.push([s, bs]);
      }
      s = Math.max(s, be);
    }
    if (s < e) {
      out.push([s, e]);
    }
  }
  return out;
}

const spanLength = (spans: Span[]) => spans.reduce((sum, [s, e]) => sum + (e - s), 0);

/** A skill use keeps an effect "active" for this long to either side; an effect on a living player with no use that near is idle time. */
const ACTIVE_WINDOW_SECONDS = 3;

/**
 * Of the time an effect was on a player: how much he was dead, how much he lived but used no skill within 3 s, and the rest. Known facts only
 * (the effect ran, the player lived, something happened); what the effect did to his damage is not known and not claimed.
 */
export function usableOf(buffSpans: Span[], deadSpans: Span[], useTimes: number[], durationSeconds: number): UsableOut {
  const clip = (spans: Span[]): Span[] => unionSpans(spans.map(([s, e]) => [Math.max(0, s), Math.min(durationSeconds, e)] as Span));
  const on = clip(buffSpans);
  const dead = intersectSpans(on, clip(deadSpans));
  const alive = subtractSpans(on, dead);
  const busy = unionSpans(useTimes.map((t) => [t - ACTIVE_WINDOW_SECONDS, t + ACTIVE_WINDOW_SECONDS] as Span));
  const active = intersectSpans(alive, busy);
  const seconds = spanLength(on);
  const deadSeconds = spanLength(dead);
  const activeSeconds = spanLength(active);
  return { seconds: round1(seconds), dead: round1(deadSeconds), idle: round1(Math.max(0, seconds - deadSeconds - activeSeconds)), active: round1(activeSeconds) };
}

/**
 * Damage per bucket of the uploader's clock, moved onto the encounter's clock: a bucket that starts `shift` seconds after the encounter's start is spread over the
 * encounter's buckets it overlaps, in proportion. The total stays the same.
 */
export function rebinDamage(damage: number[], shift: number, durationSeconds: number): number[] {
  const width = BUCKET_SECONDS;
  const out: number[] = [];
  damage.forEach((amount, i) => {
    if (amount <= 0) {
      return;
    }
    const from = shift + i * width;
    const to = from + width;
    for (let k = Math.max(0, Math.floor(from / width)); k * width < to; k++) {
      const overlap = Math.min(to, (k + 1) * width) - Math.max(from, k * width);
      if (overlap > 0) {
        out[k] = (out[k] ?? 0) + (amount * overlap) / width;
      }
    }
  });
  const length = Math.max(Math.ceil(durationSeconds / width), out.length);
  return Array.from({ length }, (_, k) => Math.round(out[k] ?? 0));
}

/**
 * The time axis of an encounter, lined up on the encounter's own start: every upload counts from its own start, so each piece is shifted by the difference.
 * Players without a time axis are left out; nothing is invented for them.
 */
export function shapeTimeline(
  input: { startedAt: string; durationSeconds: number; bossHp: { o: string; p: number[] } | null; bossDebuffs?: { o: string; list: TimelineBuffIn[] } | null; players: TimelinePlayerIn[] },
  art: {
    skillIcon: (name: string, className: string) => string | null;
    skillNames: (name: string, className: string) => Record<string, string> | undefined;
    chains?: Record<string, [number, number][]>;
    skillInfo?: (id: number) => { name: string; names?: Record<string, string>; icon: string | null } | null;
  } = { skillIcon: () => null, skillNames: () => undefined },
): EncounterTimelineOut {
  const start = Date.parse(input.startedAt);
  const classified = classifyEncounterBuffs(
    input.players.map((p) => ({ id: p.id, name: p.name, buffs: p.buffs.map((b) => ({ skillId: b.skillId, skill: b.skill, casts: b.casts, uptimePermille: b.uptimePermille, givers: b.givers })) })),
  );

  const bossHp: [number, number][] = [];
  if (input.bossHp) {
    const shift = shiftSeconds(input.bossHp.o, start);
    for (let i = 0; i + 1 < input.bossHp.p.length; i += 2) {
      bossHp.push([round1(shift + input.bossHp.p[i]! / 10), input.bossHp.p[i + 1]!]);
    }
  }

  const out: TimelinePlayerOut[] = [];
  const analysisPlayers: AnalysisPlayerOut[] = [];
  for (const p of input.players) {
    if (!p.timeline || p.timeline.times.length === 0) {
      continue;
    }
    const shift = shiftSeconds(p.timeline.origin, start);
    const analysis = p.timeline.analysis;
    const deadSpans: Span[] = [];
    for (let i = 0; i + 1 < (analysis?.dead?.length ?? 0); i += 2) {
      deadSpans.push([shift + analysis!.dead![i]! / 10, shift + analysis!.dead![i + 1]! / 10]);
    }
    const uses = p.timeline.times.map((t, i) => [round1(shift + t / 10), p.timeline!.index[i]!] as [number, number]).sort((a, b) => a[0] - b[0]);
    const counts = new Map<number, number>();
    for (const [, idx] of uses) {
      counts.set(idx, (counts.get(idx) ?? 0) + 1);
    }
    // a theostone is an item, not a skill: its icon comes from the effect table
    const skills = p.timeline.skills.map((s, i) => {
      const own = art.skillIcon(s.name, p.className);
      const item = own ? null : buffIcon(s.id);
      return { id: s.id, name: s.name, names: art.skillNames(s.name, p.className) ?? buffNames(s.id, s.name), icon: own ?? item?.icon ?? null, iconKind: own ? ("skill" as const) : (item?.iconKind ?? "skill"), uses: counts.get(i) ?? 0 };
    });

    // the effects of this player that have spans, with the page's kind (own / group / consumable)
    const kinds = new Map((classified.get(p.id) ?? []).map((b) => [b.skill, b.kind] as const));
    const buffs: TimelinePlayerOut["buffs"] = [];
    const spansOf = new Map<number, [number, number][]>();
    for (const b of p.buffs) {
      const kind = kinds.get(b.skill);
      if (kind === undefined || b.spans.length < 2) {
        continue;
      }
      const bShift = shiftSeconds(b.origin, start);
      const spans: [number, number][] = [];
      for (let i = 0; i + 1 < b.spans.length; i += 2) {
        spans.push([round1(bShift + b.spans[i]! / 10), round1(bShift + b.spans[i + 1]! / 10)]);
      }
      const art2 = buffIcon(b.skillId);
      // the dead spans are known only from a client that sends the analysis; without them nothing is said about lost time
      const usable = analysis?.dead ? usableOf(spans, deadSpans, uses.map(([t]) => t), input.durationSeconds) : undefined;
      buffs.push({ skillId: b.skillId, name: b.skill, names: buffNames(b.skillId, b.skill), icon: art2?.icon ?? null, iconKind: art2?.iconKind ?? null, kind, food: isFood(b.skillId), uptimePermille: b.uptimePermille, spans, ...(usable ? { usable } : {}) });
      spansOf.set(b.skillId, spans);
    }

    const skillBuffs: TimelinePlayerOut["skillBuffs"] = [];
    const order = [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, MAX_SKILL_LINES);
    for (const [idx, n] of order) {
      const times = uses.filter(([, i]) => i === idx).map(([t]) => t);
      const shares: SkillBuffShare[] = [];
      for (const [skillId, spans] of spansOf) {
        const share = times.filter((t) => insideAny(t, spans)).length / n;
        if (share >= MIN_SKILL_BUFF_SHARE) {
          shares.push({ skillId, share: Math.round(share * 1000) / 1000 });
        }
      }
      skillBuffs.push({ skill: idx, uses: n, buffs: shares.sort((a, b) => b.share - a.share) });
    }

    if (analysis && !p.estimated && p.className !== "?") {
      const deadSeconds = spanLength(unionSpans(deadSpans.map(([s, e]) => [Math.max(0, s), Math.min(input.durationSeconds, e)] as Span)));
      analysisPlayers.push({
        participantId: p.id,
        name: p.name,
        className: p.className,
        damage: rebinDamage(analysis.damage ?? [], shift, input.durationSeconds),
        addDamage: analysis.addDamage ?? 0,
        deadSeconds: round1(deadSeconds),
        aliveShare: input.durationSeconds > 0 ? Math.round(Math.min(1, Math.max(0, 1 - deadSeconds / input.durationSeconds)) * 1000) / 1000 : 1,
        deaths: p.timeline.deaths.length,
        taken: analysis.taken ?? [],
      });
    }
    const chains = chainsOf(p.timeline.skills, uses, art.chains ?? {}, art.skillInfo ?? (() => null));
    out.push({ participantId: p.id, name: p.name, className: p.className, skills, uses, deaths: p.timeline.deaths.map((d) => round1(shift + d / 10)), focus: p.timeline.focus ? { onBoss: round1(p.timeline.focus.onBoss / 10), onOthers: round1(p.timeline.focus.onOthers / 10), switches: p.timeline.focus.switches } : null, buffs, skillBuffs, chains });
  }
  const bossDebuffs: BossDebuffOut[] = [];
  if (input.bossDebuffs) {
    const shift = shiftSeconds(input.bossDebuffs.o, start);
    for (const b of input.bossDebuffs.list) {
      const spans: [number, number][] = [];
      for (let i = 0; i + 1 < (b.spans?.length ?? 0); i += 2) {
        spans.push([round1(shift + b.spans[i]! / 10), round1(shift + b.spans[i + 1]! / 10)]);
      }
      // An effect the client's text tables have no name for (a hidden passive, shown by its number) is no information for the reader.
      const named = buffNames(b.skillId, b.skill);
      if (/^\d+$/.test(b.skill) && !named) {
        continue;
      }
      const art2 = buffIcon(b.skillId);
      bossDebuffs.push({ skillId: b.skillId, name: b.skill, names: named, icon: art2?.icon ?? null, iconKind: art2?.iconKind ?? null, casts: b.casts, uptimePermille: b.uptimePermille, givers: b.givers, spans });
    }
    bossDebuffs.sort((a, b) => b.uptimePermille - a.uptimePermille);
  }
  let analysis: AnalysisOut | undefined;
  if (analysisPlayers.length > 0) {
    const length = Math.max(...analysisPlayers.map((a) => a.damage.length));
    analysis = {
      bucketSeconds: BUCKET_SECONDS,
      groupDamage: Array.from({ length }, (_, k) => analysisPlayers.reduce((sum, a) => sum + (a.damage[k] ?? 0), 0)),
      coverage: { players: input.players.filter((p) => p.className !== "?").length, withAnalysis: analysisPlayers.length },
      players: analysisPlayers,
    };
  }
  return { durationSeconds: input.durationSeconds, ...(analysis ? { analysis } : {}), bossHp, bossDebuffs, players: out };
}

/** Window tolerance: the use times have a resolution of 0.1 s and the client counts its 3 s from its own tick. */
const CHAIN_TOLERANCE = 0.3;

/**
 * For every used skill that has a follow-up in the client's table: of its uses, how many were followed by the follow-up within the window.
 * A follow-up never used shows 0 of n (a chain that was missed every time). Skills whose follow-up the table does not know by name are left out.
 */
export function chainsOf(
  skills: { id: number; name: string }[],
  uses: [number, number][],
  table: Record<string, [number, number][]>,
  info: (id: number) => { name: string; names?: Record<string, string>; icon: string | null } | null,
): ChainOut[] {
  const indexOfId = new Map(skills.map((s, i) => [s.id, i] as const));
  const timesOf = new Map<number, number[]>();
  for (const [t, i] of uses) {
    (timesOf.get(i) ?? timesOf.set(i, []).get(i)!).push(t);
  }
  const out: ChainOut[] = [];
  for (const [from, i] of indexOfId) {
    const links = table[String(from)];
    const fromTimes = timesOf.get(i);
    if (!links || !fromTimes || fromTimes.length === 0) {
      continue;
    }
    for (const [toId, windowMs] of links) {
      const toIdx = indexOfId.get(toId);
      const toTimes = toIdx === undefined ? [] : (timesOf.get(toIdx) ?? []);
      const window = windowMs / 1000 + CHAIN_TOLERANCE;
      const taken = fromTimes.filter((t) => toTimes.some((u) => u >= t && u <= t + window)).length;
      const known = info(toId) ?? (toIdx !== undefined ? { name: skills[toIdx]!.name, names: undefined, icon: null } : null);
      if (!known) {
        continue;
      }
      out.push({
        from: i,
        to: { id: toId, name: known.name, names: known.names, icon: known.icon, iconKind: "skill" },
        windowSeconds: windowMs / 1000,
        uses: fromTimes.length,
        taken,
      });
    }
  }
  return out.sort((a, b) => b.uses - a.uses);
}

const parse = <T>(json: string | null | undefined, fallback: T): T => {
  try {
    return json ? (JSON.parse(json) as T) : fallback;
  } catch {
    return fallback;
  }
};

/** Whether any participant of the encounter has a stored time axis (the page shows the timeline only then). */
export function hasTimeline(encounterId: number): boolean {
  return (
    db
      .select({ id: encounterTimelines.participantId })
      .from(encounterTimelines)
      .innerJoin(encounterParticipants, eq(encounterTimelines.participantId, encounterParticipants.id))
      .where(eq(encounterParticipants.encounterId, encounterId))
      .limit(1)
      .get() !== undefined
  );
}

/** The time axis of a stored encounter, or null when no client sent one. */
export function loadEncounterTimeline(encounterId: number): EncounterTimelineOut | null {
  const encounter = db.select().from(encounters).where(eq(encounters.id, encounterId)).get();
  if (!encounter) {
    return null;
  }
  const rows = db
    .select({ id: encounterParticipants.id, name: players.name, className: encounterParticipants.className, estimated: encounterParticipants.estimated, data: encounterTimelines.dataJson, gz: encounterTimelines.dataGz })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .leftJoin(encounterTimelines, eq(encounterTimelines.participantId, encounterParticipants.id))
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();
  if (!rows.some((r) => r.data || r.gz)) {
    return null;
  }
  const buffRows = db.select().from(encounterBuffUsage).where(inArray(encounterBuffUsage.participantId, rows.map((r) => r.id))).all();
  const inputs: TimelinePlayerIn[] = rows.map((r) => ({
    id: r.id,
    name: r.name,
    className: r.className,
    estimated: r.estimated,
    timeline: parse<StoredTimeline | null>(timelineJsonOf({ dataJson: r.data ?? "", dataGz: r.gz ?? null }), null),
    buffs: buffRows
      .filter((b) => b.participantId === r.id && b.skillId > 0)
      .map((b) => {
        const spans = parse<{ o?: string; s?: number[] }>(b.spansJson, {});
        return {
          skillId: b.skillId,
          skill: b.skillName,
          casts: b.casts,
          uptimePermille: b.uptimePermille,
          givers: parse<string[]>(b.giversJson, []).filter((g) => typeof g === "string"),
          spans: Array.isArray(spans.s) ? spans.s : [],
          origin: spans.o ?? encounter.startedAt,
        };
      }),
  }));
  return shapeTimeline(
    {
      startedAt: encounter.startedAt,
      durationSeconds: encounter.durationSeconds,
      bossHp: parse<{ o: string; p: number[] } | null>(encounter.bossHpJson, null),
      bossDebuffs: ((): { o: string; list: TimelineBuffIn[] } | null => {
        const stored = parse<{ o?: string; list?: { skillId: number; skill: string; casts: number; uptimePermille: number; givers: string[]; spans?: number[] }[] } | null>(encounter.bossDebuffsJson, null);
        return stored?.list ? { o: stored.o ?? encounter.startedAt, list: stored.list.map((b) => ({ ...b, spans: b.spans ?? [], origin: stored.o ?? encounter.startedAt })) } : null;
      })(),
      players: inputs,
    },
    { skillIcon: skillIconByName, skillNames: skillNamesByName, chains: skillChains(), skillInfo: skillInfoById },
  );
}
