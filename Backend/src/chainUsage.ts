import { sqlite } from "./db/client.js";
import { chainsOf } from "./encounterTimeline.js";
import { skillChains } from "./profile.js";
import { timelineJsonOf } from "./uploadArchive.js";

/** What one time axis says about chain skills: per skill and follow-up, the uses and how many were followed in time. */
export function chainRowsOf(timeline: { skills: { id: number; name: string }[]; times: number[]; index: number[] }, table: Record<string, [number, number][]> = skillChains()) {
  const uses = timeline.times.map((t, i) => [t / 10, timeline.index[i]!] as [number, number]).sort((a, b) => a[0] - b[0]);
  // the follow-up's name is not needed here: a skill the table lists is counted, used or not
  return chainsOf(timeline.skills, uses, table, (id) => ({ name: String(id), icon: null })).map((c) => ({ skillId: timeline.skills[c.from]!.id, nextId: c.to.id, offered: c.uses, taken: c.taken }));
}

/** Replaces the chain rows of a participant from its time axis and marks the axis as done. */
export function storeChainUsage(participantId: number, timeline: { skills: { id: number; name: string }[]; times: number[]; index: number[] }): void {
  const rows = chainRowsOf(timeline);
  sqlite.transaction(() => {
    sqlite.prepare("delete from encounter_chain_usage where participant_id = ?").run(participantId);
    const insert = sqlite.prepare("insert or replace into encounter_chain_usage (participant_id, skill_id, next_id, offered, taken) values (?, ?, ?, ?, ?)");
    for (const r of rows) {
      insert.run(participantId, r.skillId, r.nextId, r.offered, r.taken);
    }
    sqlite.prepare("update encounter_timelines set chains_done = 1 where participant_id = ?").run(participantId);
  })();
}

const BATCH = 300;

/** Fills the chain rows of the time axes that have none yet (all that were stored before the table existed); returns how many axes were done. */
export function backfillChainUsage(limit = Number.POSITIVE_INFINITY): number {
  const pick = sqlite.prepare(`select participant_id id, data_json raw, data_gz gz from encounter_timelines where chains_done = 0 order by participant_id limit ${BATCH}`);
  let done = 0;
  while (done < limit) {
    const rows = pick.all() as { id: number; raw: string; gz: Buffer | null }[];
    if (rows.length === 0) {
      break;
    }
    for (const row of rows) {
      let timeline: { skills: { id: number; name: string }[]; times: number[]; index: number[] } | null = null;
      try {
        const json = timelineJsonOf({ dataJson: row.raw, dataGz: row.gz });
        timeline = json ? JSON.parse(json) : null;
      } catch {
        timeline = null; // an unreadable axis is marked done below, with no rows
      }
      if (timeline) {
        storeChainUsage(row.id, timeline);
      } else {
        sqlite.prepare("update encounter_timelines set chains_done = 1 where participant_id = ?").run(row.id);
      }
      done++;
    }
  }
  return done;
}
