import { eq, and, inArray, desc } from "drizzle-orm";
import { db } from "../db/client.js";
import { encounterSkillUsage } from "../db/schema.js";
import { resolveSkillIcon } from "./skillIconResolver.js";

const TOP_SKILLS_PER_PARTICIPANT = 8;

export interface TopSkill {
  skillName: string;
  icon?: string;
  hits: number;
}

/**
 * The "Buffs" row myaion.eu shows next to a participant (icon + hit count for their most-used
 * skills) - damage skills only, ranked by total damage like the full skill breakdown
 * (/api/participants/:id) already does, just capped short since this renders inline in a list of
 * many rows rather than as its own page. One query for however many participants a page needs
 * (a boss's top 10 groups, or one encounter's whole roster) rather than one per row.
 */
export function topSkillsByParticipant(participantIds: number[]): Map<number, TopSkill[]> {
  const result = new Map<number, TopSkill[]>();
  if (participantIds.length === 0) {
    return result;
  }

  const rows = db
    .select({
      participantId: encounterSkillUsage.participantId,
      skillName: encounterSkillUsage.skillName,
      hits: encounterSkillUsage.hits,
      totalDamage: encounterSkillUsage.totalDamage,
    })
    .from(encounterSkillUsage)
    .where(and(inArray(encounterSkillUsage.participantId, participantIds), eq(encounterSkillUsage.isHeal, false)))
    .orderBy(desc(encounterSkillUsage.totalDamage))
    .all();

  for (const row of rows) {
    const list = result.get(row.participantId) ?? [];
    if (list.length < TOP_SKILLS_PER_PARTICIPANT) {
      list.push({ skillName: row.skillName, icon: resolveSkillIcon(row.skillName), hits: row.hits });
    }
    result.set(row.participantId, list);
  }

  return result;
}
