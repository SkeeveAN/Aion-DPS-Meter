import { eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterParticipants, encounters } from "./db/schema.js";
import { loadEncounterTimeline, type EncounterTimelineOut } from "./encounterTimeline.js";

/** The figures of one fight that two fights are compared by (see docs/plans/combat-analysis.md). A figure nobody sent data for is null, never 0. */
export interface EncounterSummary {
  encounterId: number;
  bossId: number;
  durationSeconds: number;
  groupIDps: number;
  isPartial: boolean;
  /** Damage to the boss of the rows with real figures (estimated rows of a crowded world boss are not added in). */
  bossDamage: number;
  addDamage: number | null;
  deaths: number | null;
  /** Living player-seconds over all player-seconds, 0..1. */
  aliveShare: number | null;
  /** Time with the boss selected over time with anything selected, 0..1. */
  bossFocusShare: number | null;
  /** Of the time group effects were on players: the share in which the player lived and used skills (see usableOf). */
  buffUsableShare: number | null;
  /** Cumulative group damage to the boss per 5 seconds. */
  damageCurve: number[] | null;
  bossHp: [number, number][] | null;
  coverage: { players: number; withTimeline: number; withAnalysis: number };
}

export interface SummaryRosterRow {
  participantId: number;
  className: string;
  estimated: boolean;
  totalDamage: number;
}

const round3 = (v: number) => Math.round(v * 1000) / 1000;

/** Pure: the summary from a fight's row, its roster and its shaped time axis (null when no client sent one). */
export function summarizeEncounter(
  encounter: { id: number; bossId: number; durationSeconds: number; groupIDps: number; isPartial: boolean },
  roster: SummaryRosterRow[],
  timeline: EncounterTimelineOut | null,
): EncounterSummary {
  const real = roster.filter((r) => r.className !== "?");
  const exact = new Set(real.filter((r) => !r.estimated).map((r) => r.participantId));
  const timed = (timeline?.players ?? []).filter((p) => exact.has(p.participantId));
  const analysis = timeline?.analysis ?? null;

  let deaths: number | null = null;
  if (timed.length > 0) {
    deaths = timed.reduce((sum, p) => sum + p.deaths.length, 0);
  }

  let onBoss = 0;
  let onAny = 0;
  for (const p of timed) {
    if (p.focus) {
      onBoss += p.focus.onBoss;
      onAny += p.focus.onBoss + p.focus.onOthers;
    }
  }

  let usable = 0;
  let usableOn = 0;
  for (const p of timed) {
    for (const b of p.buffs) {
      if (b.kind === "group" && b.usable) {
        usable += b.usable.active;
        usableOn += b.usable.seconds;
      }
    }
  }

  let damageCurve: number[] | null = null;
  if (analysis) {
    let run = 0;
    damageCurve = analysis.groupDamage.map((d) => (run += d));
  }

  return {
    encounterId: encounter.id,
    bossId: encounter.bossId,
    durationSeconds: encounter.durationSeconds,
    groupIDps: encounter.groupIDps,
    isPartial: encounter.isPartial,
    bossDamage: real.filter((r) => !r.estimated).reduce((sum, r) => sum + r.totalDamage, 0),
    addDamage: analysis ? analysis.players.reduce((sum, p) => sum + p.addDamage, 0) : null,
    deaths,
    aliveShare: analysis && analysis.players.length > 0 ? round3(analysis.players.reduce((sum, p) => sum + p.aliveShare, 0) / analysis.players.length) : null,
    bossFocusShare: onAny > 0 ? round3(onBoss / onAny) : null,
    buffUsableShare: usableOn > 0 ? round3(usable / usableOn) : null,
    damageCurve,
    bossHp: timeline && timeline.bossHp.length > 0 ? timeline.bossHp : null,
    coverage: { players: real.length, withTimeline: timed.length, withAnalysis: analysis?.players.length ?? 0 },
  };
}

/** The summary of a stored fight, or null when there is no such fight. */
export function loadEncounterSummary(encounterId: number): EncounterSummary | null {
  const encounter = db.select().from(encounters).where(eq(encounters.id, encounterId)).get();
  if (!encounter) {
    return null;
  }
  const roster = db
    .select({ participantId: encounterParticipants.id, className: encounterParticipants.className, estimated: encounterParticipants.estimated, totalDamage: encounterParticipants.totalDamage })
    .from(encounterParticipants)
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();
  return summarizeEncounter(encounter, roster, loadEncounterTimeline(encounterId));
}
