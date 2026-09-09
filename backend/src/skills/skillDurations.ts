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

// Per the user: shown regardless of duration - a real Cleric/Chanter buff, not found under this
// exact name in aioncodex's 4x skill catalog, real Chat.log, or the collected dataset as of this
// writing (see skill_durations.json's own scrape notes) - kept as a name-based exception rather
// than left out, so it starts working the moment a real "boost" line for it is ever seen, without
// needing the duration data this file otherwise requires.
const ALWAYS_SHOWN_BUFFS = new Set(["Divine Power"]);

// Per the user: "keine Sekunden-Buffs, sondern welche die länger als 3 Minuten gehen" - a short
// combat-rotation buff (e.g. Berserking I, 30s) is noise in a "Buffs" column meant to show a
// player's real, standing reinforcements.
const MIN_DURATION_SECONDS = 180;

let skills: SkillRow[] | null = null;
let durations: Record<string, DurationRow> | null = null;

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
 * combat-rotation buff (see MIN_DURATION_SECONDS) - with Divine Power always shown regardless
 * (see ALWAYS_SHOWN_BUFFS). Duration comes from aioncodex's own skill description text ("Increases
 * ... for 30s.", scraped once into skill_durations.json - see assets/README.md), not guessed.
 * Unknown skills (no aioncodex match, or no duration found on its page) are excluded rather than
 * shown by default - a "Buffs" column that can't tell short from long is worse than an incomplete
 * one that only shows what it could actually verify.
 */
export function isLongLastingBuff(skillName: string): boolean {
  if (ALWAYS_SHOWN_BUFFS.has(skillName)) {
    return true;
  }

  const id = findSkillId(skillName);
  if (id === undefined) {
    return false;
  }

  const row = loadDurations()[String(id)];
  if (!row) {
    return false;
  }

  return row.permanent || (row.durationSeconds !== null && row.durationSeconds > MIN_DURATION_SECONDS);
}
