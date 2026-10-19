import { eq, inArray } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterBuffUsage, encounterParticipants, encounterTimelines, encounters, players } from "./db/schema.js";
import { buffIcon, buffNames, classifyEncounterBuffs, isFood, type BuffKind } from "./encounterBuffs.js";
import { skillIconByName, skillNamesByName } from "./profile.js";

/** A stored time axis of one participant (see uploadSchema.ts timelineSchema), with the start of the upload it came from. */
export interface StoredTimeline {
  origin: string;
  skills: { id: number; name: string }[];
  times: number[];
  index: number[];
  deaths: number[];
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
  buffs: { skillId: number; name: string; names?: Record<string, string>; icon: string | null; iconKind: "skill" | "item" | null; kind: BuffKind; food: boolean; uptimePermille: number; spans: [number, number][] }[];
  /** Per skill (index into skills): its number of uses and which effects were on the player at those moments. */
  skillBuffs: { skill: number; uses: number; buffs: SkillBuffShare[] }[];
}

export interface EncounterTimelineOut {
  durationSeconds: number;
  /** [seconds, thousandths of the boss's health] */
  bossHp: [number, number][];
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

/**
 * The time axis of an encounter, lined up on the encounter's own start: every upload counts from its own start, so each piece is shifted by the difference.
 * Players without a time axis are left out; nothing is invented for them.
 */
export function shapeTimeline(
  input: { startedAt: string; durationSeconds: number; bossHp: { o: string; p: number[] } | null; players: TimelinePlayerIn[] },
  art: { skillIcon: (name: string, className: string) => string | null; skillNames: (name: string, className: string) => Record<string, string> | undefined } = { skillIcon: () => null, skillNames: () => undefined },
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
  for (const p of input.players) {
    if (!p.timeline || p.timeline.times.length === 0) {
      continue;
    }
    const shift = shiftSeconds(p.timeline.origin, start);
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
      buffs.push({ skillId: b.skillId, name: b.skill, names: buffNames(b.skillId, b.skill), icon: art2?.icon ?? null, iconKind: art2?.iconKind ?? null, kind, food: isFood(b.skillId), uptimePermille: b.uptimePermille, spans });
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

    out.push({ participantId: p.id, name: p.name, className: p.className, skills, uses, deaths: p.timeline.deaths.map((d) => round1(shift + d / 10)), buffs, skillBuffs });
  }
  return { durationSeconds: input.durationSeconds, bossHp, players: out };
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
    .select({ id: encounterParticipants.id, name: players.name, className: encounterParticipants.className, data: encounterTimelines.dataJson })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .leftJoin(encounterTimelines, eq(encounterTimelines.participantId, encounterParticipants.id))
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();
  if (!rows.some((r) => r.data)) {
    return null;
  }
  const buffRows = db.select().from(encounterBuffUsage).where(inArray(encounterBuffUsage.participantId, rows.map((r) => r.id))).all();
  const inputs: TimelinePlayerIn[] = rows.map((r) => ({
    id: r.id,
    name: r.name,
    className: r.className,
    timeline: parse<StoredTimeline | null>(r.data, null),
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
    { startedAt: encounter.startedAt, durationSeconds: encounter.durationSeconds, bossHp: parse<{ o: string; p: number[] } | null>(encounter.bossHpJson, null), players: inputs },
    { skillIcon: skillIconByName, skillNames: skillNamesByName },
  );
}
