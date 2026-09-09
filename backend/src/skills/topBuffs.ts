import { inArray, desc } from "drizzle-orm";
import { db } from "../db/client.js";
import { encounterBuffUsage } from "../db/schema.js";
import { resolveSkillIcon } from "./skillIconResolver.js";

const TOP_BUFFS_PER_PARTICIPANT = 8;

export interface TopBuff {
  skillName: string;
  icon?: string;
  casts: number;
}

/**
 * The web frontend's "Buffs" column - real reinforcements a participant cast (see the client's
 * ChatLog/BuffCastEvent), ranked by cast count. Replaces an earlier version of this column that
 * showed top DAMAGE skills instead (see topSkills.ts, now unused) - the user pointed out that a
 * damage/heal skill breakdown already has its own place (the roster table itself) and isn't what
 * "Buffs" should mean.
 */
export function topBuffsByParticipant(participantIds: number[]): Map<number, TopBuff[]> {
  const result = new Map<number, TopBuff[]>();
  if (participantIds.length === 0) {
    return result;
  }

  const rows = db
    .select({
      participantId: encounterBuffUsage.participantId,
      skillName: encounterBuffUsage.skillName,
      casts: encounterBuffUsage.casts,
    })
    .from(encounterBuffUsage)
    .where(inArray(encounterBuffUsage.participantId, participantIds))
    .orderBy(desc(encounterBuffUsage.casts))
    .all();

  for (const row of rows) {
    const list = result.get(row.participantId) ?? [];
    if (list.length < TOP_BUFFS_PER_PARTICIPANT) {
      list.push({ skillName: row.skillName, icon: resolveSkillIcon(row.skillName), casts: row.casts });
    }
    result.set(row.participantId, list);
  }

  return result;
}
