import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { Game } from "../constants.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

interface SkillRow {
  id: number;
  name: string;
  icon?: string;
  class?: string;
  slot?: string;
  levels?: number[];
  de?: string;
  fr?: string;
}

// Mirrors the client's own Data/SkillDatabase.cs FindByLocalizedName - a skill name uploaded by an
// English/German/French client must resolve to the same icon regardless of which rank was cast
// (Chat.log always names the actual rank, e.g. "Cleave IV", while the collected dataset only
// carries the rank-I name - see assets/README.md).
const RANK_SUFFIX = /\s+[IVXLCDM]+$/;

// The client's own synthetic label for a hit with no named skill (see Combat/SkillBreakdown.cs:
// `e.Skill ?? "(auto attack)"`) - never a real entry in the collected skill dataset, so it needs
// its own icon rather than falling through to "unknown". Per the user: myaion.eu's own generic
// auto-attack icon.
const AUTO_ATTACK_LABEL = "(auto attack)";
const AUTO_ATTACK_ICON = "icon_action_operation.png";

let rows: SkillRow[] | null = null;

function load(): SkillRow[] {
  if (rows) {
    return rows;
  }
  const filePath = path.join(__dirname, "..", "data", "skills_multilang_4x.json");
  rows = JSON.parse(fs.readFileSync(filePath, "utf8"));
  return rows!;
}

/**
 * Icon filename (e.g. "cbt_fi_wingblade_g1.png") for a skill name as uploaded, or undefined if
 * unknown. `game` gates the lookup: the collected dataset below is Aion 1 only, and some skill
 * names are generic enough (verbs like "Rest"/"Sprint") to coincidentally match an Aion 2 skill
 * of the same name - without this check an Aion 2 upload could silently get handed an Aion 1
 * skill's icon. There is no Aion 2 icon file set yet (see assets/aion2/skills/README.md), so
 * every non-"aion" game returns undefined for now rather than a wrong or broken image.
 */
export function resolveSkillIcon(skillName: string, game: Game = "aion"): string | undefined {
  if (game !== "aion") {
    return undefined;
  }
  if (skillName === AUTO_ATTACK_LABEL) {
    return AUTO_ATTACK_ICON;
  }

  const all = load();

  const exact = all.find((s) => s.name === skillName || s.de === skillName || s.fr === skillName);
  if (exact) {
    return exact.icon;
  }

  const baseName = skillName.replace(RANK_SUFFIX, "");
  const normalized = all.find(
    (s) =>
      s.name.replace(RANK_SUFFIX, "") === baseName ||
      (s.de && s.de.replace(RANK_SUFFIX, "") === baseName) ||
      (s.fr && s.fr.replace(RANK_SUFFIX, "") === baseName),
  );
  return normalized?.icon;
}
