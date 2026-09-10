import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

interface SkillRow {
  id: number;
  name: string;
  class?: string;
  de?: string;
  fr?: string;
}

interface DurationRow {
  durationSeconds: number | null;
  permanent: boolean;
}

// Mirrors skillIconResolver.ts's rank-normalization: Chat.log always names the actual rank cast
// (e.g. "Charge IV"), while the collected dataset only carries the rank-I entry.
const RANK_SUFFIX = /\s+[IVXLCDM]+$/;

// Per the user: "keine Sekunden-Buffs, sondern welche die länger als 3 Minuten gehen" - a short
// combat-rotation buff (e.g. Berserking I, 30s) is noise in a "Buffs" column meant to show a
// player's real, standing reinforcements.
const MIN_DURATION_SECONDS = 180;

let skills: SkillRow[] | null = null;
let durations: Record<string, DurationRow> | null = null;
let dpCostSkillIds: Set<number> | null = null;

function loadSkills(): SkillRow[] {
  if (!skills) {
    const filePath = path.join(__dirname, "..", "data", "skills_multilang_4x.json");
    skills = JSON.parse(fs.readFileSync(filePath, "utf8"));
  }
  return skills!;
}

function loadDurations(): Record<string, DurationRow> {
  if (!durations) {
    const filePath = path.join(__dirname, "..", "data", "skill_durations.json");
    durations = JSON.parse(fs.readFileSync(filePath, "utf8"));
  }
  return durations!;
}

// Per the user: a buff that costs Divine Power must always show, regardless of its own duration -
// not because it's literally named "Divine Power" (an earlier version of this file guessed that;
// no such skill exists under that exact name anywhere in aioncodex's 4x catalog), but because
// SPENDING the resource is itself the noteworthy event, same as myaion.eu's own Buffs column.
// Daevic Fury I is the case that surfaced this: only a 30s buff, but costs 2000 DP on a 30-minute
// cooldown - exactly the kind of cast MIN_DURATION_SECONDS alone would wrongly hide. The 60 skill
// ids here were found by scraping every one of the 974 known skills' own aioncodex page for its
// "Usage Cost: DP <n>" line (see skill_dp_cost.json's sibling regen script in assets/README.md) -
// not guessed, and not the same list as ALWAYS_SHOWN_BUFFS used to be (that literal-name exception
// is gone; use this id-based set instead).
function loadDpCostSkillIds(): Set<number> {
  if (!dpCostSkillIds) {
    const filePath = path.join(__dirname, "..", "data", "skill_dp_cost.json");
    const ids: number[] = JSON.parse(fs.readFileSync(filePath, "utf8"));
    dpCostSkillIds = new Set(ids);
  }
  return dpCostSkillIds;
}

function findSkillId(skillName: string): number | undefined {
  const all = loadSkills();
  const exact = all.find((s) => s.name === skillName || s.de === skillName || s.fr === skillName);
  if (exact) {
    return exact.id;
  }

  const baseName = skillName.replace(RANK_SUFFIX, "");
  const normalized = all.find(
    (s) =>
      s.name.replace(RANK_SUFFIX, "") === baseName ||
      (s.de && s.de.replace(RANK_SUFFIX, "") === baseName) ||
      (s.fr && s.fr.replace(RANK_SUFFIX, "") === baseName),
  );
  return normalized?.id;
}

/**
 * Per the user: the "Buffs" column must only show real, standing reinforcements - not a short
 * combat-rotation buff (see MIN_DURATION_SECONDS) - with any Divine-Power-costing cast always shown
 * regardless of its own duration (see loadDpCostSkillIds). Duration comes from aioncodex's own
 * skill description text ("Increases ... for 30s.", scraped once into skill_durations.json - see
 * assets/README.md), not guessed. Unknown skills (no aioncodex match, or no duration found on its
 * page) are excluded rather than shown by default - a "Buffs" column that can't tell short from
 * long is worse than an incomplete one that only shows what it could actually verify.
 */
export function isLongLastingBuff(skillName: string): boolean {
  const id = findSkillId(skillName);
  if (id === undefined) {
    return false;
  }

  if (loadDpCostSkillIds().has(id)) {
    return true;
  }

  const row = loadDurations()[String(id)];
  if (!row) {
    return false;
  }

  return row.permanent || (row.durationSeconds !== null && row.durationSeconds > MIN_DURATION_SECONDS);
}
